using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sallvat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRefundRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_refund_request",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    environment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    external_order_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    external_payment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payment_version = table.Column<Guid>(type: "uuid", nullable: false),
                    order_version = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    concurrency_version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_refund_request", x => x.id);
                    table.CheckConstraint("ck_refund_request_amount", "amount > 0 AND currency = 'BRL'");
                    table.CheckConstraint("ck_refund_request_external_ids", "external_order_id ~ '^ORD[A-Za-z0-9_-]+$' AND external_payment_id ~ '^PAY[A-Za-z0-9_-]+$'");
                    table.CheckConstraint("ck_refund_request_ids", "id <> '00000000-0000-0000-0000-000000000000'::uuid AND actor_user_id <> '00000000-0000-0000-0000-000000000000'::uuid AND payment_version <> '00000000-0000-0000-0000-000000000000'::uuid AND order_version <> '00000000-0000-0000-0000-000000000000'::uuid AND concurrency_version <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_refund_request_reason", "reason IN ('CustomerRequest', 'FulfillmentUnavailable', 'OperationalCorrection')");
                    table.CheckConstraint("ck_refund_request_state", "state = 'Prepared' AND environment = 'Sandbox'");
                    table.ForeignKey(
                        name: "FK_payment_refund_request_application_user_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "application_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_refund_request_payment_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_refund_request_actor_user_id",
                table: "payment_refund_request",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_refund_request_state_created_at_utc",
                table: "payment_refund_request",
                columns: new[] { "state", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_refund_request_payment",
                table: "payment_refund_request",
                column: "payment_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM payment_refund_request) THEN
                        RAISE EXCEPTION 'Cannot remove durable refund intentions; preserve payment_refund_request history.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "payment_refund_request");
        }
    }
}
