using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowRepurchaseAfterCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_orders_orders_listing",
                schema: "orders",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "ux_orders_orders_listing",
                schema: "orders",
                table: "orders",
                column: "listing_id",
                unique: true,
                filter: "status NOT IN ('cancelled', 'refunded')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_orders_orders_listing",
                schema: "orders",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "ux_orders_orders_listing",
                schema: "orders",
                table: "orders",
                column: "listing_id",
                unique: true);
        }
    }
}
