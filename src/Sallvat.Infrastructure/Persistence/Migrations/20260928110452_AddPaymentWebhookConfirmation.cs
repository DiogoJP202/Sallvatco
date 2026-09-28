using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sallvat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentWebhookConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attention",
                table: "payment");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_dispatch",
                table: "payment");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "confirmed_at_utc",
                table: "payment",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_payment_id",
                table: "payment",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "provider_updated_at_utc",
                table: "payment",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payment_webhook_event",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    delivery_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payment_id = table.Column<long>(type: "bigint", nullable: false),
                    external_order_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_webhook_event", x => x.id);
                    table.CheckConstraint("ck_webhook_delivery", "delivery_key ~ '^[0-9A-F]{64}$'");
                    table.CheckConstraint("ck_webhook_outcome", "outcome IN ('Observed', 'Confirmed', 'RequiresAttention')");
                    table.ForeignKey(
                        name: "FK_payment_webhook_event_payment_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_payment_external_payment",
                table: "payment",
                columns: new[] { "provider", "environment", "external_payment_id" },
                unique: true,
                filter: "external_payment_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attention",
                table: "payment",
                sql: "(status = 'RequiresAttention' AND attention_reason IS NOT NULL AND attention_reason IN ('PreferenceOutcomeUnknown', 'LatePreferenceResponse', 'OrderOutcomeUnknown', 'LateOrderResponse', 'CanonicalMismatch', 'FinancialReview', 'LateApproval')) OR (status <> 'RequiresAttention' AND attention_reason IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_confirmation",
                table: "payment",
                sql: "(external_payment_id IS NULL AND confirmed_at_utc IS NULL AND provider_updated_at_utc IS NULL AND status <> 'Approved') OR (external_payment_id IS NOT NULL AND external_payment_id ~ '^PAY[A-Za-z0-9_-]+$' AND confirmed_at_utc IS NOT NULL AND provider_updated_at_utc IS NOT NULL AND confirmed_at_utc >= created_at_utc AND confirmed_at_utc <= updated_at_utc AND external_order_id IS NOT NULL AND status IN ('Approved', 'RequiresAttention'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_dispatch",
                table: "payment",
                sql: "(dispatch_state = 'NotStarted' AND dispatch_token IS NULL AND dispatch_started_at_utc IS NULL AND external_order_id IS NULL) OR (dispatch_state IN ('Sending', 'Completed', 'RequiresAttention') AND preference_id IS NULL AND dispatch_token IS NOT NULL AND dispatch_token <> '00000000-0000-0000-0000-000000000000'::uuid AND dispatch_started_at_utc IS NOT NULL AND dispatch_started_at_utc >= created_at_utc AND dispatch_started_at_utc <= updated_at_utc AND ((dispatch_state = 'Sending' AND status = 'Created' AND external_order_id IS NULL) OR (dispatch_state = 'Completed' AND external_order_id IS NOT NULL AND status IN ('Pending', 'Approved')) OR (dispatch_state = 'RequiresAttention' AND status = 'RequiresAttention')))");

            migrationBuilder.CreateIndex(
                name: "IX_payment_webhook_event_outcome_received_at_utc",
                table: "payment_webhook_event",
                columns: new[] { "outcome", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_webhook_event_payment_id",
                table: "payment_webhook_event",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ux_webhook_delivery",
                table: "payment_webhook_event",
                column: "delivery_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM payment_webhook_event)
                       OR EXISTS (SELECT 1 FROM payment WHERE external_payment_id IS NOT NULL
                           OR attention_reason IN ('CanonicalMismatch', 'FinancialReview', 'LateApproval')) THEN
                        RAISE EXCEPTION 'Cannot remove webhook audit or payment confirmation evidence';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "payment_webhook_event");

            migrationBuilder.DropIndex(
                name: "ux_payment_external_payment",
                table: "payment");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attention",
                table: "payment");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_confirmation",
                table: "payment");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_dispatch",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "confirmed_at_utc",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "external_payment_id",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "provider_updated_at_utc",
                table: "payment");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attention",
                table: "payment",
                sql: "(status = 'RequiresAttention' AND attention_reason IS NOT NULL AND attention_reason IN ('PreferenceOutcomeUnknown', 'LatePreferenceResponse', 'OrderOutcomeUnknown', 'LateOrderResponse')) OR (status <> 'RequiresAttention' AND attention_reason IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_dispatch",
                table: "payment",
                sql: "(dispatch_state = 'NotStarted' AND dispatch_token IS NULL AND dispatch_started_at_utc IS NULL AND external_order_id IS NULL) OR (dispatch_state IN ('Sending', 'Completed', 'RequiresAttention') AND preference_id IS NULL AND dispatch_token IS NOT NULL AND dispatch_token <> '00000000-0000-0000-0000-000000000000'::uuid AND dispatch_started_at_utc IS NOT NULL AND dispatch_started_at_utc >= created_at_utc AND dispatch_started_at_utc <= updated_at_utc AND ((dispatch_state = 'Sending' AND status = 'Created' AND external_order_id IS NULL) OR (dispatch_state = 'Completed' AND external_order_id IS NOT NULL AND status = 'Pending') OR (dispatch_state = 'RequiresAttention' AND status = 'RequiresAttention')))");
        }
    }
}
