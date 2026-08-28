using Microsoft.EntityFrameworkCore;

namespace Sallvat.Infrastructure.Persistence;

public sealed class SallvatDbContext(
    DbContextOptions<SallvatDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(SallvatDbContext).Assembly);
    }
}
