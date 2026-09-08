using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sallvat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShoppingCarts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cart",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guest_token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    concurrency_version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart", x => x.id);
                    table.CheckConstraint("ck_cart_owner", "(guest_token_hash IS NOT NULL AND customer_id IS NULL) OR (guest_token_hash IS NULL AND customer_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_cart_customer",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cart_item",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cart_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_variant_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    reference_unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart_item", x => x.id);
                    table.CheckConstraint("ck_cart_item_currency", "currency = 'BRL'");
                    table.CheckConstraint("ck_cart_item_quantity", "quantity BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_cart_item_reference_price", "reference_unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_cart_item_cart",
                        column: x => x.cart_id,
                        principalTable: "cart",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cart_item_variant",
                        column: x => x.product_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cart_expires_at_utc",
                table: "cart",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ux_cart_customer_id",
                table: "cart",
                column: "customer_id",
                unique: true,
                filter: "customer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_cart_guest_token_hash",
                table: "cart",
                column: "guest_token_hash",
                unique: true,
                filter: "guest_token_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_cart_item_product_variant_id",
                table: "cart_item",
                column: "product_variant_id");

            migrationBuilder.CreateIndex(
                name: "ux_cart_item_cart_variant",
                table: "cart_item",
                columns: new[] { "cart_id", "product_variant_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cart_item");

            migrationBuilder.DropTable(
                name: "cart");
        }
    }
}
