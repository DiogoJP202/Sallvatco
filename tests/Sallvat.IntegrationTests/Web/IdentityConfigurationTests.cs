using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sallvat.Application.Authorization;

namespace Sallvat.IntegrationTests.Web;

public sealed class IdentityConfigurationTests
{
    [Fact]
    public async Task IdentityUsesConfirmedEmailLockoutAndSecureCookie()
    {
        await using var application = new SallvatWebApplicationFactory();
        using var scope = application.Services.CreateScope();
        var services = scope.ServiceProvider;
        var identity = services
            .GetRequiredService<IOptions<IdentityOptions>>()
            .Value;
        var cookie = services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var tokenProvider = services
            .GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>()
            .Value;

        Assert.True(identity.User.RequireUniqueEmail);
        Assert.True(identity.SignIn.RequireConfirmedAccount);
        Assert.True(identity.SignIn.RequireConfirmedEmail);
        Assert.Equal(IdentitySchemaVersions.Version2, identity.Stores.SchemaVersion);
        Assert.True(identity.Lockout.AllowedForNewUsers);
        Assert.Equal(5, identity.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(
            TimeSpan.FromMinutes(15),
            identity.Lockout.DefaultLockoutTimeSpan);
        Assert.Equal(12, identity.Password.RequiredLength);
        Assert.Equal("Sallvat.Auth.Testing", cookie.Cookie.Name);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.True(cookie.Cookie.IsEssential);
        Assert.Equal(SameSiteMode.Lax, cookie.Cookie.SameSite);
        Assert.Equal(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.Equal(TimeSpan.FromHours(8), cookie.ExpireTimeSpan);
        Assert.Equal(TimeSpan.FromHours(3), tokenProvider.TokenLifespan);
    }

    [Fact]
    public async Task AdminPolicyRequiresOnlyTheAdminRole()
    {
        await using var application = new SallvatWebApplicationFactory();
        var provider = application.Services
            .GetRequiredService<IAuthorizationPolicyProvider>();

        var policy = await provider.GetPolicyAsync(RoleNames.Admin);
        var roleRequirement = Assert.Single(
            policy!.Requirements.OfType<RolesAuthorizationRequirement>());

        Assert.Equal([RoleNames.Admin], roleRequirement.AllowedRoles);
        Assert.DoesNotContain(RoleNames.Customer, roleRequirement.AllowedRoles);
    }
}
