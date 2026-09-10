using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sallvat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrdersAndReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "order_number_sequence",
                startValue: 1000L);

            migrationBuilder.CreateTable(
                name: "order",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    checkout_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_cart_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    buyer_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    buyer_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    buyer_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    items_subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    shipping_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    grand_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    coupon_id = table.Column<long>(type: "bigint", nullable: true),
                    coupon_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    shipping_provider = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    shipping_carrier = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    shipping_service = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    shipping_quote_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    shipping_minimum_business_days = table.Column<int>(type: "integer", nullable: false),
                    shipping_maximum_business_days = table.Column<int>(type: "integer", nullable: false),
                    shipping_quoted_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    concurrency_version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order", x => x.id);
                    table.CheckConstraint("ck_order_amounts", "items_subtotal >= 0 AND discount_total >= 0 AND discount_total <= items_subtotal AND shipping_total > 0 AND grand_total = items_subtotal - discount_total + shipping_total");
                    table.CheckConstraint("ck_order_coupon", "(discount_total = 0 AND coupon_id IS NULL AND coupon_code IS NULL) OR (discount_total > 0 AND coupon_id IS NOT NULL AND coupon_code IS NOT NULL)");
                    table.CheckConstraint("ck_order_currency", "currency = 'BRL'");
                    table.CheckConstraint("ck_order_expiration", "expires_at_utc > created_at_utc AND shipping_quoted_at_utc <= created_at_utc");
                    table.CheckConstraint("ck_order_shipping_days", "shipping_minimum_business_days > 0 AND shipping_maximum_business_days >= shipping_minimum_business_days");
                    table.CheckConstraint("ck_order_status", "status IN ('PendingPayment', 'Paid', 'Preparing', 'Shipped', 'Delivered', 'Cancelled', 'Refunded', 'RequiresAttention')");
                    table.ForeignKey(
                        name: "fk_order_coupon",
                        column: x => x.coupon_id,
                        principalTable: "coupon",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_order_customer",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_address",
                columns: table => new
                {
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    recipient_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    postal_code = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    street = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    complement = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    district = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    city = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    state_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_address", x => x.order_id);
                    table.CheckConstraint("ck_order_address_country", "country_code = 'BR'");
                    table.ForeignKey(
                        name: "fk_order_address_order",
                        column: x => x.order_id,
                        principalTable: "order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_item",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    product_variant_id = table.Column<long>(type: "bigint", nullable: false),
                    product_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    variant_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_item", x => x.id);
                    table.CheckConstraint("ck_order_item_amounts", "unit_price >= 0 AND discount_amount >= 0 AND discount_amount <= unit_price * quantity AND subtotal = unit_price * quantity - discount_amount");
                    table.CheckConstraint("ck_order_item_currency", "currency = 'BRL'");
                    table.CheckConstraint("ck_order_item_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_order_item_order",
                        column: x => x.order_id,
                        principalTable: "order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_order_item_variant",
                        column: x => x.product_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_reservation",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<long>(type: "bigint", nullable: false),
                    product_variant_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reserved_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    released_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    concurrency_version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_reservation", x => x.id);
                    table.CheckConstraint("ck_stock_reservation_completion", "(status = 'Reserved' AND consumed_at_utc IS NULL AND released_at_utc IS NULL) OR (status = 'Consumed' AND consumed_at_utc IS NOT NULL AND released_at_utc IS NULL) OR (status = 'Released' AND consumed_at_utc IS NULL AND released_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_stock_reservation_expiration", "expires_at_utc > reserved_at_utc");
                    table.CheckConstraint("ck_stock_reservation_quantity", "quantity > 0");
                    table.CheckConstraint("ck_stock_reservation_status", "status IN ('Reserved', 'Consumed', 'Released')");
                    table.ForeignKey(
                        name: "fk_stock_reservation_order",
                        column: x => x.order_id,
                        principalTable: "order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_reservation_variant",
                        column: x => x.product_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_coupon_redemption_order_id",
                table: "coupon_redemption",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_coupon_id",
                table: "order",
                column: "coupon_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_customer_id",
                table: "order",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_source_cart",
                table: "order",
                column: "source_cart_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_status_created",
                table: "order",
                columns: new[] { "status", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_order_checkout_attempt",
                table: "order",
                column: "checkout_attempt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_order_number",
                table: "order",
                column: "order_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_item_order_id",
                table: "order_item",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_item_variant_id",
                table: "order_item",
                column: "product_variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservation_status_expiration",
                table: "stock_reservation",
                columns: new[] { "status", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservation_variant_id",
                table: "stock_reservation",
                column: "product_variant_id");

            migrationBuilder.CreateIndex(
                name: "ux_stock_reservation_order_variant",
                table: "stock_reservation",
                columns: new[] { "order_id", "product_variant_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_coupon_redemption_order",
                table: "coupon_redemption",
                column: "order_id",
                principalTable: "order",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_coupon_redemption_order",
                table: "coupon_redemption");

            migrationBuilder.DropTable(
                name: "order_address");

            migrationBuilder.DropTable(
                name: "order_item");

            migrationBuilder.DropTable(
                name: "stock_reservation");

            migrationBuilder.DropTable(
                name: "order");

            migrationBuilder.DropIndex(
                name: "ix_coupon_redemption_order_id",
                table: "coupon_redemption");

            migrationBuilder.DropSequence(
                name: "order_number_sequence");
        }
    }
}
