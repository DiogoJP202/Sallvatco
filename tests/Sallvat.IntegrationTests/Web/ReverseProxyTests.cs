using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sallvat.Web.Configuration;

namespace Sallvat.IntegrationTests.Web;

public sealed class ReverseProxyTests
{
    [Theory]
    [InlineData(true, "172.29.240.2", "203.0.113.8", "https", "203.0.113.8")]
    [InlineData(true, "172.29.240.3", "203.0.113.8", "http", "172.29.240.3")]
    [InlineData(false, "172.29.240.2", "203.0.113.8", "http", "172.29.240.2")]
    [InlineData(true, "172.29.240.2", "198.51.100.5, 203.0.113.8", "http", "172.29.240.2")]
    public async Task OnlyExplicitProxyWithSymmetricHeadersCanChangeSchemeAndAddress(
        bool enabled, string peer, string forwardedFor, string expectedScheme, string expectedIp)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ReverseProxy:Enabled"] = enabled.ToString(),
                ["ReverseProxy:KnownProxy"] = "172.29.240.2",
            }).Build();
        using var host = await new HostBuilder().ConfigureWebHost(web => web
            .UseTestServer()
            .ConfigureServices(services => services.AddTrustedReverseProxy(configuration))
            .Configure(app =>
            {
                app.UseForwardedHeaders();
                app.Run(context =>
                {
                    context.Response.Headers["Test-Scheme"] = context.Request.Scheme;
                    context.Response.Headers["Test-IP"] = context.Connection.RemoteIpAddress!.ToString();
                    context.Response.Headers["Test-Host"] = context.Request.Host.Value;
                    return Task.CompletedTask;
                });
            })).StartAsync();
        var server = host.GetTestServer();

        var response = await server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("staging.example.invalid");
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
            context.Request.Headers["X-Forwarded-Host"] = "attacker.invalid";
        });

        Assert.Equal(expectedScheme, response.Response.Headers["Test-Scheme"].ToString());
        Assert.Equal(expectedIp, response.Response.Headers["Test-IP"].ToString());
        Assert.Equal("staging.example.invalid", response.Response.Headers["Test-Host"].ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("172.29.240.0/28")]
    [InlineData("*")]
    public void EnabledProxyRequiresOneConcreteAddress(string? address)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ReverseProxy:Enabled"] = "true",
                ["ReverseProxy:KnownProxy"] = address,
            }).Build();
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddTrustedReverseProxy(configuration));
    }
}
