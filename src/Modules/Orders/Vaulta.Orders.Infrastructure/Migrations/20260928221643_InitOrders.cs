using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collectible_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    printing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    condition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    item_price_brl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    platform_fee_brl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_amount_brl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    shipping_street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    shipping_city = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    shipping_state = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    shipping_zip_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tracking_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reservations",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reserved_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_by_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_orders_orders_buyer_status",
                schema: "orders",
                table: "orders",
                columns: new[] { "buyer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_orders_created",
                schema: "orders",
                table: "orders",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_orders_orders_seller_status",
                schema: "orders",
                table: "orders",
                columns: new[] { "seller_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_orders_orders_listing",
                schema: "orders",
                table: "orders",
                column: "listing_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_reservations_buyer_listing_status",
                schema: "orders",
                table: "reservations",
                columns: new[] { "buyer_id", "listing_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_reservations_expires",
                schema: "orders",
                table: "reservations",
                column: "reserved_until",
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_orders_reservations_listing_status",
                schema: "orders",
                table: "reservations",
                columns: new[] { "listing_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orders",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "reservations",
                schema: "orders");
        }
    }
}
