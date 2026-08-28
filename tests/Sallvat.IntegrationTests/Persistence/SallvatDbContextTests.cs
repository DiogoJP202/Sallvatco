using Microsoft.EntityFrameworkCore;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.IntegrationTests.Persistence;

public sealed class SallvatDbContextTests
{
    [Fact]
    public void EmptyModelCanBeCreatedWithoutConnectingToDatabase()
    {
        var options = new DbContextOptionsBuilder<SallvatDbContext>()
            .UseNpgsql(
                "Host=127.0.0.1;Database=sallvat;Username=sallvat;Password=test")
            .Options;

        using var context = new SallvatDbContext(options);

        Assert.Empty(context.Model.GetEntityTypes());
    }
}
