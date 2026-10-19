using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Sallvat.Web.Configuration;

public static class ReverseProxyConfiguration
{
    public static void AddTrustedReverseProxy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool>("ReverseProxy:Enabled");
        var address = configuration["ReverseProxy:KnownProxy"];
        if (enabled && (!IPAddress.TryParse(address, out var parsed)
            || parsed.Equals(IPAddress.Any) || parsed.Equals(IPAddress.IPv6Any)))
        {
            throw new InvalidOperationException(
                "ReverseProxy:KnownProxy must be one explicit proxy IP when enabled.");
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = enabled
                ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                : ForwardedHeaders.None;
            options.ForwardLimit = 1;
            options.RequireHeaderSymmetry = true;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            if (enabled)
            {
                options.KnownProxies.Add(IPAddress.Parse(address!));
            }
        });
    }
}
