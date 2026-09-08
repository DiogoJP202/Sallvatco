using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sallvat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCoupons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "coupon_id",
                table: "cart",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "coupon",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    normalized_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    discount_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    minimum_subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    starts_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    total_usage_limit = table.Column<int>(type: "integer", nullable: true),
                    usage_limit_per_identity = table.Column<int>(type: "integer", nullable: true),
                    claimed_usage_count = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    concurrency_version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coupon", x => x.id);
                    table.CheckConstraint("ck_coupon_claimed_usage_count", "claimed_usage_count >= 0 AND (total_usage_limit IS NULL OR claimed_usage_count <= total_usage_limit)");
                    table.CheckConstraint("ck_coupon_discount_type", "discount_type IN ('Percentage', 'FixedAmount')");
                    table.CheckConstraint("ck_coupon_minimum_subtotal", "minimum_subtotal >= 0");
                    table.CheckConstraint("ck_coupon_percentage", "discount_type <> 'Percentage' OR value <= 100");
                    table.CheckConstraint("ck_coupon_total_usage_limit", "total_usage_limit IS NULL OR total_usage_limit > 0");
                    table.CheckConstraint("ck_coupon_usage_limit_per_identity", "usage_limit_per_identity IS NULL OR usage_limit_per_identity > 0");
                    table.CheckConstraint("ck_coupon_value", "value > 0");
                    table.CheckConstraint("ck_coupon_window", "starts_at_utc IS NULL OR expires_at_utc IS NULL OR expires_at_utc > starts_at_utc");
                });

            migrationBuilder.CreateTable(
                name: "coupon_redemption",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    coupon_id = table.Column<long>(type: "bigint", nullable: false),
                    reservation_key = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<long>(type: "bigint", nullable: true),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reserved_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    released_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    concurrency_version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coupon_redemption", x => x.id);
                    table.CheckConstraint("ck_coupon_redemption_discount", "discount_amount > 0");
                    table.CheckConstraint("ck_coupon_redemption_expiration", "expires_at_utc > reserved_at_utc");
                    table.CheckConstraint("ck_coupon_redemption_identity", "customer_id IS NOT NULL OR normalized_email IS NOT NULL");
                    table.CheckConstraint("ck_coupon_redemption_order", "(status = 'Consumed' AND order_id IS NOT NULL AND consumed_at_utc IS NOT NULL) OR (status <> 'Consumed' AND order_id IS NULL AND consumed_at_utc IS NULL)");
                    table.CheckConstraint("ck_coupon_redemption_status", "status IN ('Reserved', 'Consumed', 'Released')");
                    table.ForeignKey(
                        name: "fk_coupon_redemption_coupon",
                        column: x => x.coupon_id,
                        principalTable: "coupon",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_coupon_redemption_customer",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cart_coupon_id",
                table: "cart",
                column: "coupon_id");

            migrationBuilder.CreateIndex(
                name: "ix_coupon_active_window",
                table: "coupon",
                columns: new[] { "is_active", "starts_at_utc", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_coupon_normalized_code",
                table: "coupon",
                column: "normalized_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coupon_redemption_coupon_status_expiration",
                table: "coupon_redemption",
                columns: new[] { "coupon_id", "status", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_coupon_redemption_customer_id",
                table: "coupon_redemption",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_coupon_redemption_customer_usage",
                table: "coupon_redemption",
                columns: new[] { "coupon_id", "customer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_coupon_redemption_email_usage",
                table: "coupon_redemption",
                columns: new[] { "coupon_id", "normalized_email", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_coupon_redemption_reservation_key",
                table: "coupon_redemption",
                column: "reservation_key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_cart_coupon",
                table: "cart",
                column: "coupon_id",
                principalTable: "coupon",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cart_coupon",
                table: "cart");

            migrationBuilder.DropTable(
                name: "coupon_redemption");

            migrationBuilder.DropTable(
                name: "coupon");

            migrationBuilder.DropIndex(
                name: "ix_cart_coupon_id",
                table: "cart");

            migrationBuilder.DropColumn(
                name: "coupon_id",
                table: "cart");
        }
    }
}
