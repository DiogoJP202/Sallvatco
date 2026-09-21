using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sallvat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    environment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    external_reference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    preference_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attention_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    concurrency_version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment", x => x.id);
                    table.CheckConstraint("ck_payment_amount", "amount > 0 AND currency = 'BRL'");
                    table.CheckConstraint("ck_payment_attention", "(status = 'RequiresAttention' AND attention_reason IS NOT NULL AND attention_reason IN ('PreferenceOutcomeUnknown', 'LatePreferenceResponse')) OR (status <> 'RequiresAttention' AND attention_reason IS NULL)");
                    table.CheckConstraint("ck_payment_environment", "environment IN ('Sandbox', 'Production')");
                    table.CheckConstraint("ck_payment_idempotency_key", "idempotency_key <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_payment_pending_preference", "status <> 'Pending' OR preference_id IS NOT NULL");
                    table.CheckConstraint("ck_payment_preference", "preference_id IS NULL OR preference_id ~ '^[A-Za-z0-9_-]+$'");
                    table.CheckConstraint("ck_payment_provider", "provider = 'MercadoPago'");
                    table.CheckConstraint("ck_payment_reference", "length(btrim(external_reference)) > 0");
                    table.CheckConstraint("ck_payment_status", "status IN ('Created', 'Pending', 'Approved', 'Rejected', 'Cancelled', 'Expired', 'Refunded', 'RequiresAttention')");
                    table.CheckConstraint("ck_payment_timestamps", "expires_at_utc > created_at_utc AND updated_at_utc >= created_at_utc");
                    table.ForeignKey(
                        name: "fk_payment_order",
                        column: x => x.order_id,
                        principalTable: "order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_status_expiration",
                table: "payment",
                columns: new[] { "status", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_payment_idempotency",
                table: "payment",
                columns: new[] { "provider", "environment", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_preference",
                table: "payment",
                columns: new[] { "provider", "environment", "preference_id" },
                unique: true,
                filter: "preference_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_payment_unresolved_order",
                table: "payment",
                column: "order_id",
                unique: true,
                filter: "status IN ('Created', 'Pending', 'Approved', 'RequiresAttention')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment");
        }
    }
}
