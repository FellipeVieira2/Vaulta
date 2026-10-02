using System.Net;
using Vaulta.App.Core.Identity;
using Xunit;

namespace Vaulta.App.Core.UnitTests.Identity;

public sealed class LoginFlowTests
{
    [Theory]
    [InlineData("", "", true, true)]
    [InlineData("invalid", "secret", true, false)]
    [InlineData("person@example.com", "", false, true)]
    public async Task InvalidFieldsNeverSendCredentials(string email, string password, bool emailError, bool passwordError)
    {
        var calls = 0;
        var flow = new LoginFlow((_, _, _) => { calls++; return Task.CompletedTask; });
        Assert.False(await flow.SubmitAsync(email, password));
        Assert.Equal(0, calls);
        Assert.Equal(emailError, flow.State.EmailError is not null);
        Assert.Equal(passwordError, flow.State.PasswordError is not null);
        Assert.False(flow.State.IsBusy);
    }

    [Fact]
    public async Task SuccessTrimsEmailPreservesPasswordAndPublishesLoading()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? sentEmail = null, sentPassword = null;
        var flow = new LoginFlow((email, password, _) => { sentEmail = email; sentPassword = password; return ready.Task; });
        var changes = new List<bool>();
        flow.StateChanged += (_, _) => changes.Add(flow.State.IsBusy);
        var pending = flow.SubmitAsync(" person@example.com ", " password ");
        Assert.True(flow.State.IsBusy);
        ready.SetResult();
        Assert.True(await pending);
        Assert.Equal("person@example.com", sentEmail);
        Assert.Equal(" password ", sentPassword);
        Assert.Equal(new[] { true, false }, changes);
        Assert.Null(flow.State.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "E-mail ou senha incorretos.")]
    [InlineData(HttpStatusCode.TooManyRequests, "Muitas tentativas. Aguarde um pouco e tente novamente.")]
    [InlineData(HttpStatusCode.InternalServerError, "Não foi possível entrar. Tente novamente em instantes.")]
    public async Task ServerErrorsShowSafeMessagesAndPermitRetry(HttpStatusCode status, string message)
    {
        var failed = true;
        var flow = new LoginFlow((_, _, _) => failed
            ? Task.FromException(new HttpRequestException("secret token password response body", null, status))
            : Task.CompletedTask);
        Assert.False(await flow.SubmitAsync("person@example.com", "secret"));
        Assert.Equal(message, flow.State.Message);
        Assert.False(flow.State.IsBusy);
        failed = false;
        Assert.True(await flow.SubmitAsync("person@example.com", "secret"));
        Assert.Null(flow.State.Message);
    }

    [Fact]
    public async Task DuplicateSubmissionMakesOnlyOneAuthenticationCall()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var flow = new LoginFlow((_, _, _) => { calls++; return ready.Task; });
        var first = flow.SubmitAsync("person@example.com", "secret");
        Assert.False(await flow.SubmitAsync("person@example.com", "secret"));
        Assert.Equal(1, calls);
        ready.SetResult();
        Assert.True(await first);
    }

    [Fact]
    public async Task LeavingScreenCancelsRequestAndSuppressesLateSuccess()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        var flow = new LoginFlow((_, _, token) => { received = token; return ready.Task; });
        var pending = flow.SubmitAsync("person@example.com", "secret");
        flow.CancelPending();
        Assert.False(await pending);
        Assert.True(received.IsCancellationRequested);
        ready.SetResult();
        Assert.False(flow.State.IsBusy);
        Assert.Null(flow.State.Message);
    }

    [Fact]
    public async Task TimeoutReturnsRetryMessageEvenWhenTransportIgnoresCancellation()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flow = new LoginFlow((_, _, _) => ready.Task, TimeSpan.FromMilliseconds(30));
        Assert.False(await flow.SubmitAsync("person@example.com", "secret"));
        Assert.False(flow.State.IsBusy);
        Assert.Equal("A conexão demorou mais que o esperado. Tente novamente.", flow.State.Message);
        ready.SetResult();
    }

    [Fact]
    public async Task OfflineDoesNotExposeExceptionOrCredentials()
    {
        var flow = new LoginFlow((_, _, _) => Task.FromException(new HttpRequestException("private password")));
        Assert.False(await flow.SubmitAsync("person@example.com", "secret"));
        Assert.Equal("Não foi possível conectar. Verifique sua internet e tente novamente.", flow.State.Message);
    }
}
