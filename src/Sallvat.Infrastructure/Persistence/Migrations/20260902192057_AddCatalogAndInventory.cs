using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sallvat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogAndInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    changes_json = table.Column<string>(type: "jsonb", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_log_actor",
                        column: x => x.actor_user_id,
                        principalTable: "application_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    slug = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    short_description = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    olfactory_family = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    top_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    heart_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    base_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    concentration = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    projection = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    longevity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    occasions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    season = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    period = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_featured = table.Column<bool>(type: "boolean", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    concurrency_version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product", x => x.id);
                    table.CheckConstraint("ck_product_status", "status IN ('Draft', 'Published', 'Archived')");
                });

            migrationBuilder.CreateTable(
                name: "product_image",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    alt_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    is_cover = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_image", x => x.id);
                    table.CheckConstraint("ck_product_image_dimensions", "width > 0 AND height > 0");
                    table.CheckConstraint("ck_product_image_position", "position >= 0");
                    table.ForeignKey(
                        name: "fk_product_image_product",
                        column: x => x.product_id,
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_slug_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    slug = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_slug_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_slug_history_product",
                        column: x => x.product_id,
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_variant",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normalized_sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    volume_ml = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    weight_kg = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    height_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    width_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    length_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    on_hand = table.Column<int>(type: "integer", nullable: false),
                    reserved = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    concurrency_version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_variant", x => x.id);
                    table.CheckConstraint("ck_product_variant_currency", "currency = 'BRL'");
                    table.CheckConstraint("ck_product_variant_physical", "weight_kg > 0 AND height_cm > 0 AND width_cm > 0 AND length_cm > 0");
                    table.CheckConstraint("ck_product_variant_price", "price >= 0");
                    table.CheckConstraint("ck_product_variant_stock", "on_hand >= 0 AND reserved >= 0 AND reserved <= on_hand");
                    table.CheckConstraint("ck_product_variant_volume", "volume_ml > 0");
                    table.ForeignKey(
                        name: "fk_product_variant_product",
                        column: x => x.product_id,
                        principalTable: "product",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_movement",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_variant_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    resulting_on_hand = table.Column<int>(type: "integer", nullable: false),
                    resulting_reserved = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_movement", x => x.id);
                    table.CheckConstraint("ck_inventory_movement_balance", "resulting_on_hand >= 0 AND resulting_reserved >= 0 AND resulting_reserved <= resulting_on_hand");
                    table.CheckConstraint("ck_inventory_movement_quantity", "quantity <> 0");
                    table.ForeignKey(
                        name: "fk_inventory_movement_actor",
                        column: x => x.actor_user_id,
                        principalTable: "application_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_movement_variant",
                        column: x => x.product_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_actor_user_id",
                table: "audit_log",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entity_created",
                table: "audit_log",
                columns: new[] { "entity_type", "entity_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movement_actor_user_id",
                table: "inventory_movement",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movement_variant_created",
                table: "inventory_movement",
                columns: new[] { "product_variant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_product_publication",
                table: "product",
                columns: new[] { "status", "is_featured", "published_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_product_slug",
                table: "product",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_image_product_position",
                table: "product_image",
                columns: new[] { "product_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ux_product_image_cover",
                table: "product_image",
                column: "product_id",
                unique: true,
                filter: "is_cover");

            migrationBuilder.CreateIndex(
                name: "ux_product_image_storage_key",
                table: "product_image",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_slug_history_product_id",
                table: "product_slug_history",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ux_product_slug_history_slug",
                table: "product_slug_history",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_variant_product_active",
                table: "product_variant",
                columns: new[] { "product_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ux_product_variant_normalized_sku",
                table: "product_variant",
                column: "normalized_sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "inventory_movement");

            migrationBuilder.DropTable(
                name: "product_image");

            migrationBuilder.DropTable(
                name: "product_slug_history");

            migrationBuilder.DropTable(
                name: "product_variant");

            migrationBuilder.DropTable(
                name: "product");
        }
    }
}
