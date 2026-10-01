using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BlockPayoutsOnPaymentReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payout_hold_reason",
                schema: "payments",
                table: "payment_transactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Older code treated a past-due charge as terminal failure even
            // though Asaas can subsequently confirm/receive that same charge.
            migrationBuilder.Sql("""
                UPDATE payments.payment_transactions
                SET status = 'overdue', version = gen_random_uuid()
                WHERE status = 'failed' AND failure_reason = 'Asaas event: PAYMENT_OVERDUE';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payout_hold_reason",
                schema: "payments",
                table: "payment_transactions");
        }
    }
}
