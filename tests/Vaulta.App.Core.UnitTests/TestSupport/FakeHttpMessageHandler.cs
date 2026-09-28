namespace Vaulta.App.Core.UnitTests.TestSupport;

/// <summary>Records every request it sees and responds via a caller-provided delegate. No real network calls.</summary>
public sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : this(request => Task.FromResult(handler(request)))
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return await handler(request);
    }
}
