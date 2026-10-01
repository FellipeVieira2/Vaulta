using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DirectSellerPixPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "received_at",
                schema: "payments",
                table: "payment_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "seller_payouts",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_price_brl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    platform_fee_brl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    amount_brl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    external_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    transfer_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    destination_version = table.Column<Guid>(type: "uuid", nullable: true),
                    pix_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    pix_key_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    destination_holder_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    destination_holder_document = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    destination_verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    destination_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    destination_evidence_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seller_payouts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seller_pix_destinations",
                schema: "payments",
                columns: table => new
                {
                    seller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    holder_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    holder_document = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    identity_evidence_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seller_pix_destinations", x => x.seller_id);
                });

            migrationBuilder.CreateTable(
                name: "transfer_webhook_receipts",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_webhook_receipts", x => x.id);
                    table.ForeignKey(
                        name: "FK_transfer_webhook_receipts_seller_payouts_payout_id",
                        column: x => x.payout_id,
                        principalSchema: "payments",
                        principalTable: "seller_payouts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_seller_payouts_external_reference",
                schema: "payments",
                table: "seller_payouts",
                column: "external_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seller_payouts_order_id",
                schema: "payments",
                table: "seller_payouts",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seller_payouts_payment_id",
                schema: "payments",
                table: "seller_payouts",
                column: "payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seller_payouts_seller_id_created_at",
                schema: "payments",
                table: "seller_payouts",
                columns: new[] { "seller_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_seller_payouts_status_next_check_at",
                schema: "payments",
                table: "seller_payouts",
                columns: new[] { "status", "next_check_at" });

            migrationBuilder.CreateIndex(
                name: "IX_seller_payouts_transfer_id",
                schema: "payments",
                table: "seller_payouts",
                column: "transfer_id",
                unique: true,
                filter: "transfer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_webhook_receipts_payout_id",
                schema: "payments",
                table: "transfer_webhook_receipts",
                column: "payout_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "seller_pix_destinations",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "transfer_webhook_receipts",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "seller_payouts",
                schema: "payments");

            migrationBuilder.DropColumn(
                name: "received_at",
                schema: "payments",
                table: "payment_transactions");
        }
    }
}
