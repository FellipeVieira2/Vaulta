using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SellerFeesAndRefundRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "payment_fee_brl",
                schema: "payments",
                table: "seller_payouts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "seller_net_brl",
                schema: "payments",
                table: "seller_payouts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "transfer_fee_brl",
                schema: "payments",
                table: "seller_payouts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refund_next_check_at",
                schema: "payments",
                table: "payment_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_reason",
                schema: "payments",
                table: "payment_transactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_request_url",
                schema: "payments",
                table: "payment_transactions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refund_requested_at",
                schema: "payments",
                table: "payment_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "refund_requested_by",
                schema: "payments",
                table: "payment_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_status",
                schema: "payments",
                table: "payment_transactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refund_submitted_at",
                schema: "payments",
                table: "payment_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_refund_status_refund_next_check_at",
                schema: "payments",
                table: "payment_transactions",
                columns: new[] { "refund_status", "refund_next_check_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_transactions_refund_status_refund_next_check_at",
                schema: "payments",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "payment_fee_brl",
                schema: "payments",
                table: "seller_payouts");

            migrationBuilder.DropColumn(
                name: "seller_net_brl",
                schema: "payments",
                table: "seller_payouts");

            migrationBuilder.DropColumn(
                name: "transfer_fee_brl",
                schema: "payments",
                table: "seller_payouts");

            migrationBuilder.DropColumn(
                name: "refund_next_check_at",
                schema: "payments",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "refund_reason",
                schema: "payments",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "refund_request_url",
                schema: "payments",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "refund_requested_at",
                schema: "payments",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "refund_requested_by",
                schema: "payments",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "refund_status",
                schema: "payments",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "refund_submitted_at",
                schema: "payments",
                table: "payment_transactions");
        }
    }
}
