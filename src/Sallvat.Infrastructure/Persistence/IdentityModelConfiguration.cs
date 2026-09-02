using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sallvat.Application.Authorization;
using Sallvat.Infrastructure.Identity;

namespace Sallvat.Infrastructure.Persistence;

internal static class IdentityModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        ConfigureUser(modelBuilder.Entity<ApplicationUser>());
        ConfigureRole(modelBuilder.Entity<IdentityRole<Guid>>());

        modelBuilder.Entity<IdentityUserClaim<Guid>>(builder =>
        {
            builder.ToTable("application_user_claim");
            builder.HasKey(claim => claim.Id)
                .HasName("pk_application_user_claim");
            builder.Property(claim => claim.Id).HasColumnName("id");
            builder.Property(claim => claim.UserId).HasColumnName("user_id");
            builder.Property(claim => claim.ClaimType).HasColumnName("claim_type");
            builder.Property(claim => claim.ClaimValue).HasColumnName("claim_value");
            builder.HasIndex(claim => claim.UserId)
                .HasDatabaseName("ix_application_user_claim_user_id");
            builder.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(claim => claim.UserId)
                .HasConstraintName("fk_application_user_claim_user");
        });
        modelBuilder.Entity<IdentityUserRole<Guid>>(builder =>
        {
            builder.ToTable("application_user_role");
            builder.HasKey(role => new { role.UserId, role.RoleId })
                .HasName("pk_application_user_role");
            builder.Property(role => role.UserId).HasColumnName("user_id");
            builder.Property(role => role.RoleId).HasColumnName("role_id");
            builder.HasIndex(role => role.RoleId)
                .HasDatabaseName("ix_application_user_role_role_id");
            builder.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(role => role.UserId)
                .HasConstraintName("fk_application_user_role_user");
            builder.HasOne<IdentityRole<Guid>>()
                .WithMany()
                .HasForeignKey(role => role.RoleId)
                .HasConstraintName("fk_application_user_role_role");
        });
        modelBuilder.Entity<IdentityUserLogin<Guid>>(builder =>
        {
            builder.ToTable("application_user_login");
            builder.HasKey(login => new
            {
                login.LoginProvider,
                login.ProviderKey,
            })
                .HasName("pk_application_user_login");
            builder.Property(login => login.LoginProvider).HasColumnName("login_provider");
            builder.Property(login => login.ProviderKey).HasColumnName("provider_key");
            builder.Property(login => login.ProviderDisplayName).HasColumnName("provider_display_name");
            builder.Property(login => login.UserId).HasColumnName("user_id");
            builder.HasIndex(login => login.UserId)
                .HasDatabaseName("ix_application_user_login_user_id");
            builder.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(login => login.UserId)
                .HasConstraintName("fk_application_user_login_user");
        });
        modelBuilder.Entity<IdentityRoleClaim<Guid>>(builder =>
        {
            builder.ToTable("application_role_claim");
            builder.HasKey(claim => claim.Id)
                .HasName("pk_application_role_claim");
            builder.Property(claim => claim.Id).HasColumnName("id");
            builder.Property(claim => claim.RoleId).HasColumnName("role_id");
            builder.Property(claim => claim.ClaimType).HasColumnName("claim_type");
            builder.Property(claim => claim.ClaimValue).HasColumnName("claim_value");
            builder.HasIndex(claim => claim.RoleId)
                .HasDatabaseName("ix_application_role_claim_role_id");
            builder.HasOne<IdentityRole<Guid>>()
                .WithMany()
                .HasForeignKey(claim => claim.RoleId)
                .HasConstraintName("fk_application_role_claim_role");
        });
        modelBuilder.Entity<IdentityUserToken<Guid>>(builder =>
        {
            builder.ToTable("application_user_token");
            builder.HasKey(token => new
            {
                token.UserId,
                token.LoginProvider,
                token.Name,
            })
                .HasName("pk_application_user_token");
            builder.Property(token => token.UserId).HasColumnName("user_id");
            builder.Property(token => token.LoginProvider).HasColumnName("login_provider");
            builder.Property(token => token.Name).HasColumnName("name");
            builder.Property(token => token.Value).HasColumnName("value");
            builder.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .HasConstraintName("fk_application_user_token_user");
        });
    }

    private static void ConfigureUser(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("application_user");
        builder.HasKey(user => user.Id)
            .HasName("pk_application_user");
        builder.Property(user => user.Id).HasColumnName("id");
        builder.Property(user => user.UserName)
            .HasColumnName("user_name")
            .IsRequired();
        builder.Property(user => user.NormalizedUserName)
            .HasColumnName("normalized_user_name")
            .IsRequired();
        builder.Property(user => user.Email)
            .HasColumnName("email")
            .HasMaxLength(254)
            .IsRequired();
        builder.Property(user => user.NormalizedEmail)
            .HasColumnName("normalized_email")
            .HasMaxLength(254)
            .IsRequired();
        builder.Property(user => user.EmailConfirmed).HasColumnName("email_confirmed");
        builder.Property(user => user.PasswordHash).HasColumnName("password_hash");
        builder.Property(user => user.SecurityStamp).HasColumnName("security_stamp");
        builder.Property(user => user.ConcurrencyStamp).HasColumnName("concurrency_stamp");
        builder.Property(user => user.PhoneNumber).HasColumnName("phone_number");
        builder.Property(user => user.PhoneNumberConfirmed).HasColumnName("phone_number_confirmed");
        builder.Property(user => user.TwoFactorEnabled).HasColumnName("two_factor_enabled");
        builder.Property(user => user.LockoutEnd).HasColumnName("lockout_end");
        builder.Property(user => user.LockoutEnabled).HasColumnName("lockout_enabled");
        builder.Property(user => user.AccessFailedCount).HasColumnName("access_failed_count");

        builder.HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("ux_application_user_normalized_email");
        builder.HasIndex(user => user.NormalizedUserName)
            .IsUnique()
            .HasDatabaseName("ux_application_user_normalized_user_name");
    }

    private static void ConfigureRole(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.ToTable("application_role");
        builder.HasKey(role => role.Id)
            .HasName("pk_application_role");
        builder.Property(role => role.Id).HasColumnName("id");
        builder.Property(role => role.Name)
            .HasColumnName("name")
            .IsRequired();
        builder.Property(role => role.NormalizedName)
            .HasColumnName("normalized_name")
            .IsRequired();
        builder.Property(role => role.ConcurrencyStamp).HasColumnName("concurrency_stamp");
        builder.HasIndex(role => role.NormalizedName)
            .IsUnique()
            .HasDatabaseName("ux_application_role_normalized_name");

        builder.HasData(
            new IdentityRole<Guid>
            {
                Id = IdentityRoleIds.Customer,
                Name = RoleNames.Customer,
                NormalizedName = RoleNames.Customer.ToUpperInvariant(),
                ConcurrencyStamp = "01990b9a-6c57-7ebf-9c06-2fb7680aa8dc",
            },
            new IdentityRole<Guid>
            {
                Id = IdentityRoleIds.Admin,
                Name = RoleNames.Admin,
                NormalizedName = RoleNames.Admin.ToUpperInvariant(),
                ConcurrencyStamp = "01990b9a-6c57-7ebf-9c06-33b5d3ee1511",
            });
    }
}
