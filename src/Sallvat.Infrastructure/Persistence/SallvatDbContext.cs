using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sallvat.Domain.Customers;
using Sallvat.Infrastructure.Identity;

namespace Sallvat.Infrastructure.Persistence;

public sealed class SallvatDbContext(
    DbContextOptions<SallvatDbContext> options) :
    IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Address> Addresses => Set<Address>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(
            typeof(SallvatDbContext).Assembly);
        IdentityModelConfiguration.Configure(builder);
    }
}
