using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sallvat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRefundDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_webhook_outcome",
                table: "payment_webhook_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refund_request_state",
                table: "payment_refund_request");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_confirmation",
                table: "payment");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_dispatch",
                table: "payment");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "confirmed_at_utc",
                table: "payment_refund_request",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_refund_id",
                table: "payment_refund_request",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "started_at_utc",
                table: "payment_refund_request",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_webhook_outcome",
                table: "payment_webhook_event",
                sql: "outcome IN ('Observed', 'Confirmed', 'RequiresAttention', 'Refunded')");

            migrationBuilder.CreateIndex(
                name: "ux_refund_external_id",
                table: "payment_refund_request",
                column: "external_refund_id",
                unique: true,
                filter: "external_refund_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refund_request_state",
                table: "payment_refund_request",
                sql: "environment = 'Sandbox' AND ((state = 'Prepared' AND started_at_utc IS NULL) OR (state IN ('Sending', 'AwaitingConfirmation', 'Confirmed') AND started_at_utc IS NOT NULL) OR state = 'RequiresAttention') AND (started_at_utc IS NULL OR started_at_utc >= created_at_utc) AND ((state = 'Confirmed' AND external_refund_id IS NOT NULL AND external_refund_id ~ '^REF[A-Za-z0-9_-]+$' AND confirmed_at_utc IS NOT NULL AND confirmed_at_utc >= started_at_utc) OR (state <> 'Confirmed' AND external_refund_id IS NULL AND confirmed_at_utc IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_confirmation",
                table: "payment",
                sql: "(external_payment_id IS NULL AND confirmed_at_utc IS NULL AND provider_updated_at_utc IS NULL AND status NOT IN ('Approved', 'Refunded')) OR (external_payment_id IS NOT NULL AND external_payment_id ~ '^PAY[A-Za-z0-9_-]+$' AND confirmed_at_utc IS NOT NULL AND provider_updated_at_utc IS NOT NULL AND confirmed_at_utc >= created_at_utc AND confirmed_at_utc <= updated_at_utc AND external_order_id IS NOT NULL AND status IN ('Approved', 'RequiresAttention', 'Refunded'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_dispatch",
                table: "payment",
                sql: "(dispatch_state = 'NotStarted' AND dispatch_token IS NULL AND dispatch_started_at_utc IS NULL AND external_order_id IS NULL) OR (dispatch_state IN ('Sending', 'Completed', 'RequiresAttention') AND preference_id IS NULL AND dispatch_token IS NOT NULL AND dispatch_token <> '00000000-0000-0000-0000-000000000000'::uuid AND dispatch_started_at_utc IS NOT NULL AND dispatch_started_at_utc >= created_at_utc AND dispatch_started_at_utc <= updated_at_utc AND ((dispatch_state = 'Sending' AND status = 'Created' AND external_order_id IS NULL) OR (dispatch_state = 'Completed' AND external_order_id IS NOT NULL AND status IN ('Pending', 'Approved', 'Refunded')) OR (dispatch_state = 'RequiresAttention' AND status = 'RequiresAttention')))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM payment_refund_request WHERE state <> 'Prepared' OR started_at_utc IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM payment WHERE status = 'Refunded')
                        OR EXISTS (SELECT 1 FROM payment_webhook_event WHERE outcome = 'Refunded') THEN
                        RAISE EXCEPTION 'Cannot remove refund dispatch or confirmation evidence.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_webhook_outcome",
                table: "payment_webhook_event");

            migrationBuilder.DropIndex(
                name: "ux_refund_external_id",
                table: "payment_refund_request");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refund_request_state",
                table: "payment_refund_request");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_confirmation",
                table: "payment");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_dispatch",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "confirmed_at_utc",
                table: "payment_refund_request");

            migrationBuilder.DropColumn(
                name: "external_refund_id",
                table: "payment_refund_request");

            migrationBuilder.DropColumn(
                name: "started_at_utc",
                table: "payment_refund_request");

            migrationBuilder.AddCheckConstraint(
                name: "ck_webhook_outcome",
                table: "payment_webhook_event",
                sql: "outcome IN ('Observed', 'Confirmed', 'RequiresAttention')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refund_request_state",
                table: "payment_refund_request",
                sql: "state = 'Prepared' AND environment = 'Sandbox'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_confirmation",
                table: "payment",
                sql: "(external_payment_id IS NULL AND confirmed_at_utc IS NULL AND provider_updated_at_utc IS NULL AND status <> 'Approved') OR (external_payment_id IS NOT NULL AND external_payment_id ~ '^PAY[A-Za-z0-9_-]+$' AND confirmed_at_utc IS NOT NULL AND provider_updated_at_utc IS NOT NULL AND confirmed_at_utc >= created_at_utc AND confirmed_at_utc <= updated_at_utc AND external_order_id IS NOT NULL AND status IN ('Approved', 'RequiresAttention'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_dispatch",
                table: "payment",
                sql: "(dispatch_state = 'NotStarted' AND dispatch_token IS NULL AND dispatch_started_at_utc IS NULL AND external_order_id IS NULL) OR (dispatch_state IN ('Sending', 'Completed', 'RequiresAttention') AND preference_id IS NULL AND dispatch_token IS NOT NULL AND dispatch_token <> '00000000-0000-0000-0000-000000000000'::uuid AND dispatch_started_at_utc IS NOT NULL AND dispatch_started_at_utc >= created_at_utc AND dispatch_started_at_utc <= updated_at_utc AND ((dispatch_state = 'Sending' AND status = 'Created' AND external_order_id IS NULL) OR (dispatch_state = 'Completed' AND external_order_id IS NOT NULL AND status IN ('Pending', 'Approved')) OR (dispatch_state = 'RequiresAttention' AND status = 'RequiresAttention')))");
        }
    }
}
