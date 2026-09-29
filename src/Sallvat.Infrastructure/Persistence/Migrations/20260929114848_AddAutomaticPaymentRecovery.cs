using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sallvat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomaticPaymentRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "outcome",
                table: "payment_recovery_execution",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "payment_recovery_execution",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Manual");

            // Existing executions were manual; do not invent a financial outcome for legacy rows.
            migrationBuilder.Sql("ALTER TABLE payment_recovery_execution ALTER COLUMN source DROP DEFAULT");

            migrationBuilder.CreateIndex(
                name: "IX_payment_recovery_execution_payment_id_source_started_at_utc",
                table: "payment_recovery_execution",
                columns: new[] { "payment_id", "source", "started_at_utc" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_execution_outcome",
                table: "payment_recovery_execution",
                sql: "(state = 'Running' AND outcome IS NULL) OR (state = 'Completed' AND ((source = 'Manual' AND outcome IS NULL) OR (outcome IS NOT NULL AND outcome IN ('Observed', 'Confirmed', 'RequiresAttention', 'Conflict', 'Unavailable', 'Forbidden')))) OR (state = 'Interrupted' AND ((source = 'Manual' AND outcome IS NULL) OR (outcome IS NOT NULL AND outcome = 'Interrupted')))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recovery_execution_source",
                table: "payment_recovery_execution",
                sql: "source IN ('Manual', 'Automatic')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM payment_recovery_execution WHERE source = 'Automatic' OR outcome IS NOT NULL) THEN
                        RAISE EXCEPTION 'Recovery source and outcome evidence must be preserved; downgrade refused';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_payment_recovery_execution_payment_id_source_started_at_utc",
                table: "payment_recovery_execution");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_execution_outcome",
                table: "payment_recovery_execution");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recovery_execution_source",
                table: "payment_recovery_execution");

            migrationBuilder.DropColumn(
                name: "outcome",
                table: "payment_recovery_execution");

            migrationBuilder.DropColumn(
                name: "source",
                table: "payment_recovery_execution");
        }
    }
}
