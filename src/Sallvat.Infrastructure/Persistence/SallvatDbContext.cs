using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sallvat.Domain.Auditing;
using Sallvat.Domain.Carts;
using Sallvat.Domain.Catalog;
using Sallvat.Domain.Customers;
using Sallvat.Domain.Inventory;
using Sallvat.Domain.Orders;
using Sallvat.Domain.Promotions;
using Sallvat.Infrastructure.Identity;

namespace Sallvat.Infrastructure.Persistence;

public sealed class SallvatDbContext(
    DbContextOptions<SallvatDbContext> options) :
    IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Address> Addresses => Set<Address>();

    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<CartItem> CartItems => Set<CartItem>();

    public DbSet<Coupon> Coupons => Set<Coupon>();

    public DbSet<CouponRedemption> CouponRedemptions =>
        Set<CouponRedemption>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<ProductSlugHistory> ProductSlugHistory =>
        Set<ProductSlugHistory>();

    public DbSet<InventoryMovement> InventoryMovements =>
        Set<InventoryMovement>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<OrderAddress> OrderAddresses => Set<OrderAddress>();

    public DbSet<StockReservation> StockReservations =>
        Set<StockReservation>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(
            typeof(SallvatDbContext).Assembly);
        builder.HasSequence<long>("order_number_sequence")
            .StartsAt(1_000L);
        IdentityModelConfiguration.Configure(builder);
    }
}
