using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sallvat.Application.Accounts;
using Sallvat.Infrastructure.Persistence;

namespace Sallvat.IntegrationTests.Web;

public sealed class AccountWebApplicationFactory :
    SallvatWebApplicationFactory
{
    private readonly string databaseName = $"sallvat-{Guid.NewGuid():N}";

    public FakeAccountEmailSender EmailSender { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<SallvatDbContext>>();
            services.RemoveAll<
                IDbContextOptionsConfiguration<SallvatDbContext>>();
            services.RemoveAll<SallvatDbContext>();
            services.AddDbContext<SallvatDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<SallvatDbContext>();
        await context.Database.EnsureCreatedAsync();
    }
}

public sealed class FakeAccountEmailSender : IEmailSender
{
    private readonly List<AccountEmailDelivery> deliveries = [];
    private readonly Lock syncRoot = new();

    public IReadOnlyList<AccountEmailDelivery> Deliveries
    {
        get
        {
            lock (syncRoot)
            {
                return deliveries.ToArray();
            }
        }
    }

    public Task SendEmailConfirmationAsync(
        string recipientEmail,
        Uri confirmationUrl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Add(new AccountEmailDelivery(
            recipientEmail,
            confirmationUrl,
            IsPasswordReset: false));

        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(
        string recipientEmail,
        Uri resetUrl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Add(new AccountEmailDelivery(
            recipientEmail,
            resetUrl,
            IsPasswordReset: true));

        return Task.CompletedTask;
    }

    private void Add(AccountEmailDelivery delivery)
    {
        lock (syncRoot)
        {
            deliveries.Add(delivery);
        }
    }
}

public sealed record AccountEmailDelivery(
    string RecipientEmail,
    Uri ActionUrl,
    bool IsPasswordReset);
