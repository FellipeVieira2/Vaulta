using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vaulta.Web.Api;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

public sealed class ReverseProxyTests
{
    private static Task<IHost> Server() => new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
        .UseEnvironment("Production")
        .ConfigureServices(services =>
        {
            services.AddTrustedReverseProxy(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ReverseProxy:KnownProxies:0"] = "172.30.42.1" }).Build());
            services.AddRouting();
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = 429;
                options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress!.ToString(), _ => new FixedWindowRateLimiterOptions
                    { PermitLimit = 1, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            });
        })
        .Configure(app =>
        {
            app.UseForwardedHeaders();
            app.UseProductionTransport();
            app.UseRouting();
            app.UseRateLimiter();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/auth-test", context => context.Response.WriteAsync("ok")).RequireRateLimiting("auth");
                endpoints.MapGet("/{**path}", context => context.Response.WriteAsync(
                    context.Request.Scheme + "|" + context.Connection.RemoteIpAddress));
            });
        })).StartAsync();

    private static Task<HttpContext> Send(TestServer server, string remote, string path, string? forwardedIp = null) =>
        server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
            context.Request.Method = "GET";
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("api.example.com");
            context.Request.Path = path;
            if (forwardedIp is not null)
            {
                context.Request.Headers["X-Forwarded-For"] = forwardedIp;
                context.Request.Headers["X-Forwarded-Proto"] = "https";
            }
        });

    [Theory]
    [InlineData("172.30.42.1")]
    [InlineData("::ffff:172.30.42.1")]
    public async Task TrustedGatewayPreservesOriginalSchemeAndIpWithoutRedirectLoop(string gateway)
    {
        using var host = await Server();
        var server = host.GetTestServer();
        var response = await Send(server, gateway, "/api/test", "203.0.113.10");
        Assert.Equal(200, response.Response.StatusCode);
        Assert.Equal("https", response.Request.Scheme);
        Assert.Equal(IPAddress.Parse("203.0.113.10"), response.Connection.RemoteIpAddress);
        Assert.False(response.Response.Headers.ContainsKey("Location"));
        Assert.True(response.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }

    [Fact]
    public async Task UnknownProxyCannotSpoofHttpsOrIp()
    {
        using var host = await Server();
        var server = host.GetTestServer();
        var response = await Send(server, "198.51.100.20", "/api/test", "203.0.113.10");
        Assert.Equal(307, response.Response.StatusCode);
        Assert.Equal("http", response.Request.Scheme);
        Assert.Equal(IPAddress.Parse("198.51.100.20"), response.Connection.RemoteIpAddress);
        Assert.Equal("https://api.example.com/api/test", response.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("/health", 200)]
    [InlineData("/health/ready", 200)]
    [InlineData("/health/other", 307)]
    public async Task OnlyExactHealthPathsAllowHttp(string path, int expectedStatus)
    {
        using var host = await Server();
        var server = host.GetTestServer();
        Assert.Equal(expectedStatus, (await Send(server, "172.30.42.1", path)).Response.StatusCode);
    }

    [Fact]
    public async Task RateLimiterPartitionsByForwardedClientIp()
    {
        using var host = await Server();
        var server = host.GetTestServer();
        Assert.Equal(200, (await Send(server, "172.30.42.1", "/auth-test", "203.0.113.10")).Response.StatusCode);
        Assert.Equal(429, (await Send(server, "172.30.42.1", "/auth-test", "203.0.113.10")).Response.StatusCode);
        Assert.Equal(200, (await Send(server, "172.30.42.1", "/auth-test", "203.0.113.11")).Response.StatusCode);
    }
}
