using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sallvat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRecoveryExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_recovery_execution",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    concurrency_version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_recovery_execution", x => x.id);
                    table.CheckConstraint("ck_recovery_execution_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_recovery_execution_state", "(state = 'Running' AND finished_at_utc IS NULL) OR (state = 'Completed' AND finished_at_utc IS NOT NULL AND finished_at_utc >= started_at_utc AND finished_at_utc < expires_at_utc) OR (state = 'Interrupted' AND finished_at_utc IS NOT NULL AND finished_at_utc >= expires_at_utc)");
                    table.CheckConstraint("ck_recovery_execution_window", "expires_at_utc = started_at_utc + interval '2 minutes'");
                    table.ForeignKey(
                        name: "FK_payment_recovery_execution_payment_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_recovery_execution_payment_id_started_at_utc",
                table: "payment_recovery_execution",
                columns: new[] { "payment_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_recovery_execution_running",
                table: "payment_recovery_execution",
                column: "payment_id",
                unique: true,
                filter: "state = 'Running'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM payment_recovery_execution) THEN
                        RAISE EXCEPTION 'Recovery execution history must be preserved; downgrade refused';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "payment_recovery_execution");
        }
    }
}
