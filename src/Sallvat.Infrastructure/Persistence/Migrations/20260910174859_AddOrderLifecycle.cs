using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sallvat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_coupon_redemption_order_id",
                table: "coupon_redemption");

            migrationBuilder.DropCheckConstraint(
                name: "ck_coupon_redemption_order",
                table: "coupon_redemption");

            migrationBuilder.AddColumn<string>(
                name: "attention_reason",
                table: "order",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "attention_since_utc",
                table: "order",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                table: "inventory_movement",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_attention",
                table: "order",
                sql: "(status = 'RequiresAttention' AND attention_reason IS NOT NULL AND attention_since_utc IS NOT NULL) OR (status <> 'RequiresAttention' AND attention_reason IS NULL AND attention_since_utc IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ux_coupon_redemption_order_id",
                table: "coupon_redemption",
                column: "order_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_coupon_redemption_order",
                table: "coupon_redemption",
                sql: "(status = 'Reserved' AND order_id IS NULL AND consumed_at_utc IS NULL AND released_at_utc IS NULL) OR (status = 'Consumed' AND order_id IS NOT NULL AND consumed_at_utc IS NOT NULL AND released_at_utc IS NULL) OR (status = 'Released' AND released_at_utc IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_order_attention",
                table: "order");

            migrationBuilder.DropIndex(
                name: "ux_coupon_redemption_order_id",
                table: "coupon_redemption");

            migrationBuilder.DropCheckConstraint(
                name: "ck_coupon_redemption_order",
                table: "coupon_redemption");

            migrationBuilder.DropColumn(
                name: "attention_reason",
                table: "order");

            migrationBuilder.DropColumn(
                name: "attention_since_utc",
                table: "order");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_user_id",
                table: "inventory_movement",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_coupon_redemption_order_id",
                table: "coupon_redemption",
                column: "order_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_coupon_redemption_order",
                table: "coupon_redemption",
                sql: "(status = 'Consumed' AND order_id IS NOT NULL AND consumed_at_utc IS NOT NULL) OR (status <> 'Consumed' AND order_id IS NULL AND consumed_at_utc IS NULL)");
        }
    }
}
