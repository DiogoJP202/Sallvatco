using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Sallvat.Domain.Customers;
using Sallvat.Infrastructure.Identity;
using Sallvat.Infrastructure.Persistence;
using Sallvat.IntegrationTests.Web;

namespace Sallvat.IntegrationTests.Persistence;

public sealed class SallvatDbContextTests
{
    [Fact]
    public async Task IdentityAndCustomerModelMatchesTheInitialMigration()
    {
        await using var application = new SallvatWebApplicationFactory();
        using var scope = application.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();

        var tableNames = context.Model
            .GetEntityTypes()
            .Where(entityType => !entityType.IsOwned())
            .Select(entityType => entityType.GetTableName() ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "address",
                "application_role",
                "application_role_claim",
                "application_user",
                "application_user_claim",
                "application_user_login",
                "application_user_role",
                "application_user_token",
                "customer",
            ],
            tableNames);

        var user = context.Model.FindEntityType(typeof(ApplicationUser));
        Assert.Equal(
            typeof(Guid),
            user!.FindPrimaryKey()!.Properties.Single().ClrType);
        var emailIndex = Assert.Single(
            user.GetIndexes(),
            index => index.Properties.Single().Name ==
                nameof(IdentityUser<Guid>.NormalizedEmail));
        Assert.True(emailIndex.IsUnique);

        var roleNames = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(IdentityRole<Guid>))!
            .GetSeedData()
            .Select(row => Assert.IsType<string>(
                row[nameof(IdentityRole<Guid>.Name)]))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["Admin", "Customer"], roleNames);

        var customer = context.Model.FindEntityType(typeof(Customer));
        var userIndex = Assert.Single(
            customer!.GetIndexes(),
            index => index.Properties.Single().Name ==
                nameof(Customer.ApplicationUserId));
        Assert.True(userIndex.IsUnique);
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
