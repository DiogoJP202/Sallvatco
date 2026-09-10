using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sallvat.Application.Accounts;
using Sallvat.Application.Carts;
using Sallvat.Application.Catalog;
using Sallvat.Application.Checkout;
using Sallvat.Application.Orders;
using Sallvat.Application.Promotions;
using Sallvat.Application.Time;
using Sallvat.Infrastructure.Carts;
using Sallvat.Infrastructure.Catalog;
using Sallvat.Infrastructure.Checkout;
using Sallvat.Infrastructure.Identity;
using Sallvat.Infrastructure.Orders;
using Sallvat.Infrastructure.Persistence;
using Sallvat.Infrastructure.Promotions;
using Sallvat.Infrastructure.Storage;
using Sallvat.Infrastructure.Time;

namespace Sallvat.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<DatabaseOptions>()
            .Configure(options =>
                options.ConnectionString =
                    configuration.GetConnectionString("SallvatDatabase")
                    ?? string.Empty)
            .Validate(
                options => !string.IsNullOrWhiteSpace(
                    options.ConnectionString),
                "Connection string 'SallvatDatabase' is required.")
            .ValidateOnStart();

        services.AddDbContext<SallvatDbContext>((serviceProvider, options) =>
        {
            var databaseOptions = serviceProvider
                .GetRequiredService<IOptions<DatabaseOptions>>()
                .Value;

            options.UseNpgsql(
                databaseOptions.ConnectionString,
                npgsql =>
                {
                    npgsql.MigrationsAssembly(
                        typeof(SallvatDbContext).Assembly.GetName().Name);
                    npgsql.MigrationsHistoryTable("__ef_migrations_history");
                });
        });
        services
            .AddOptions<OrderOptions>()
            .Bind(configuration.GetSection(OrderOptions.SectionName))
            .Validate(
                options => options.ReservationMinutes is >= 5 and <= 1_440,
                "Orders:ReservationMinutes must be between 5 and 1440.")
            .ValidateOnStart();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IOrderLifecycleService, OrderLifecycleService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ICouponService, CouponService>();
        services.AddSingleton<IImageStorage, LocalImageStorage>();
        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
