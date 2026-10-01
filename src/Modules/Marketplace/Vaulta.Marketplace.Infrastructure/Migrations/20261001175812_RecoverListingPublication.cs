using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Marketplace.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecoverListingPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_marketplace_listings_active_item",
                schema: "marketplace",
                table: "listings");

            migrationBuilder.CreateIndex(
                name: "ux_marketplace_listings_active_item",
                schema: "marketplace",
                table: "listings",
                column: "collectible_item_id",
                unique: true,
                filter: "status IN ('active', 'publishing')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_marketplace_listings_active_item",
                schema: "marketplace",
                table: "listings");

            migrationBuilder.CreateIndex(
                name: "ux_marketplace_listings_active_item",
                schema: "marketplace",
                table: "listings",
                column: "collectible_item_id",
                unique: true,
                filter: "status = 'active'");
        }
    }
}
