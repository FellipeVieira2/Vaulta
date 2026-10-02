using System.Net;
using System.Net.Mail;

namespace Vaulta.App.Core.Identity;

public sealed record LoginState(bool IsBusy = false, string? EmailError = null, string? PasswordError = null, string? Message = null);

/// <summary>Native login validation and request state; credentials are never retained.</summary>
public sealed class LoginFlow(Func<string, string, CancellationToken, Task> authenticate, TimeSpan? timeout = null)
{
    private CancellationTokenSource? _pending;
    private int _submitting;
    public LoginState State { get; private set; } = new();
    public event EventHandler? StateChanged;

    public async Task<bool> SubmitAsync(string? email, string? password, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _submitting, 1, 0) != 0) return false;
        try
        {
            var normalizedEmail = email?.Trim() ?? "";
            var emailValid = normalizedEmail.Length <= 320 && MailAddress.TryCreate(normalizedEmail, out var address)
                && address.Address == normalizedEmail && normalizedEmail.Contains('@');
            var emailError = emailValid ? null : "Informe um e-mail válido.";
            var passwordError = string.IsNullOrEmpty(password) ? "Informe sua senha." : null;
            if (emailError is not null || passwordError is not null)
            {
                SetState(new(EmailError: emailError, PasswordError: passwordError));
                return false;
            }

            using var timeoutCancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
            _pending = requestCancellation;
            SetState(new(IsBusy: true));
            try
            {
                await authenticate(normalizedEmail, password!, requestCancellation.Token).WaitAsync(requestCancellation.Token);
                requestCancellation.Token.ThrowIfCancellationRequested();
                SetState(new());
                return true;
            }
            catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
            {
                SetState(new(Message: timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                    ? "A conexão demorou mais que o esperado. Tente novamente." : null));
                return false;
            }
            catch (Exception exception)
            {
                SetState(new(Message: SafeMessage(exception)));
                return false;
            }
            finally { _pending = null; }
        }
        finally { Interlocked.Exchange(ref _submitting, 0); }
    }

    public void CancelPending() => _pending?.Cancel();

    private void SetState(LoginState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string SafeMessage(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => "E-mail ou senha incorretos.",
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "Muitas tentativas. Aguarde um pouco e tente novamente.",
        HttpRequestException { StatusCode: HttpStatusCode.BadRequest } => "Confira seu e-mail e sua senha.",
        HttpRequestException { StatusCode: null } => "Não foi possível conectar. Verifique sua internet e tente novamente.",
        OperationCanceledException => "A conexão demorou mais que o esperado. Tente novamente.",
        _ => "Não foi possível entrar. Tente novamente em instantes."
    };
}
