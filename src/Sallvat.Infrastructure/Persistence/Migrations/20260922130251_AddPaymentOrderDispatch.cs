using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sallvat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentOrderDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attention",
                table: "payment");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_pending_preference",
                table: "payment");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dispatch_started_at_utc",
                table: "payment",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dispatch_state",
                table: "payment",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotStarted");

            migrationBuilder.AddColumn<Guid>(
                name: "dispatch_token",
                table: "payment",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_order_id",
                table: "payment",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_external_order",
                table: "payment",
                columns: new[] { "provider", "environment", "external_order_id" },
                unique: true,
                filter: "external_order_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attention",
                table: "payment",
                sql: "(status = 'RequiresAttention' AND attention_reason IS NOT NULL AND attention_reason IN ('PreferenceOutcomeUnknown', 'LatePreferenceResponse', 'OrderOutcomeUnknown', 'LateOrderResponse')) OR (status <> 'RequiresAttention' AND attention_reason IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_dispatch",
                table: "payment",
                sql: "(dispatch_state = 'NotStarted' AND dispatch_token IS NULL AND dispatch_started_at_utc IS NULL AND external_order_id IS NULL) OR (dispatch_state IN ('Sending', 'Completed', 'RequiresAttention') AND preference_id IS NULL AND dispatch_token IS NOT NULL AND dispatch_token <> '00000000-0000-0000-0000-000000000000'::uuid AND dispatch_started_at_utc IS NOT NULL AND dispatch_started_at_utc >= created_at_utc AND dispatch_started_at_utc <= updated_at_utc AND ((dispatch_state = 'Sending' AND status = 'Created' AND external_order_id IS NULL) OR (dispatch_state = 'Completed' AND external_order_id IS NOT NULL AND status = 'Pending') OR (dispatch_state = 'RequiresAttention' AND status = 'RequiresAttention')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_external_order",
                table: "payment",
                sql: "external_order_id IS NULL OR external_order_id ~ '^ORD[A-Za-z0-9_-]*$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_pending_resource",
                table: "payment",
                sql: "status <> 'Pending' OR preference_id IS NOT NULL OR external_order_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_resource_exclusive",
                table: "payment",
                sql: "preference_id IS NULL OR external_order_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Erasing a Sending claim would permit a second external POST after rollback.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM payment WHERE dispatch_state <> 'NotStarted') THEN
                        RAISE EXCEPTION 'Cannot roll back payment dispatch while durable claims exist';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ux_payment_external_order",
                table: "payment");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_attention",
                table: "payment");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_dispatch",
                table: "payment");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_external_order",
                table: "payment");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_pending_resource",
                table: "payment");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_resource_exclusive",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "dispatch_started_at_utc",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "dispatch_state",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "dispatch_token",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "external_order_id",
                table: "payment");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_attention",
                table: "payment",
                sql: "(status = 'RequiresAttention' AND attention_reason IS NOT NULL AND attention_reason IN ('PreferenceOutcomeUnknown', 'LatePreferenceResponse')) OR (status <> 'RequiresAttention' AND attention_reason IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_pending_preference",
                table: "payment",
                sql: "status <> 'Pending' OR preference_id IS NOT NULL");
        }
    }
}
