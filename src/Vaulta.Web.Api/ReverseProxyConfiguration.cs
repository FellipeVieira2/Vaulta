using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Vaulta.Web.Api;

public static class ReverseProxyConfiguration
{
    public static IServiceCollection AddTrustedReverseProxy(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.RequireHeaderSymmetry = true;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.KnownProxies.Add(IPAddress.Loopback);
            options.KnownProxies.Add(IPAddress.IPv6Loopback);
            foreach (var value in configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
            {
                if (!IPAddress.TryParse(value, out var address) || address.Equals(IPAddress.Any) ||
                    address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None))
                    throw new InvalidOperationException("ReverseProxy:KnownProxies requires individual trusted IP addresses.");
                options.KnownProxies.Add(address);
            }
        });
        services.AddHttpsRedirection(options => options.HttpsPort = 443);
        return services;
    }

    public static IApplicationBuilder UseProductionTransport(this IApplicationBuilder app)
    {
        app.UseHsts();
        // Only exact liveness/readiness paths are exempt for host-local HTTP probes.
        app.UseWhen(context => context.Request.Path != "/health" && context.Request.Path != "/health/ready",
            branch => branch.UseHttpsRedirection());
        return app;
    }
}
