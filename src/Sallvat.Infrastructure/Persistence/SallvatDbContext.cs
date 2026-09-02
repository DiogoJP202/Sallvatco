using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sallvat.Domain.Auditing;
using Sallvat.Domain.Catalog;
using Sallvat.Domain.Customers;
using Sallvat.Domain.Inventory;
using Sallvat.Infrastructure.Identity;

namespace Sallvat.Infrastructure.Persistence;

public sealed class SallvatDbContext(
    DbContextOptions<SallvatDbContext> options) :
    IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Address> Addresses => Set<Address>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<ProductSlugHistory> ProductSlugHistory =>
        Set<ProductSlugHistory>();

    public DbSet<InventoryMovement> InventoryMovements =>
        Set<InventoryMovement>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(
            typeof(SallvatDbContext).Assembly);
        IdentityModelConfiguration.Configure(builder);
    }
}
