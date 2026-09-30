using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Marketplace.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitMarketplace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "marketplace");

            migrationBuilder.CreateTable(
                name: "listings",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collectible_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    printing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    condition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    price_brl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sold_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sold_to_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_listings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seller_profiles",
                schema: "marketplace",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    bio = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    state = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    zip_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    average_rating = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    total_sales = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seller_profiles", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "listing_photos",
                schema: "marketplace",
                columns: table => new
                {
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_listing_photos", x => new { x.listing_id, x.asset_id });
                    table.ForeignKey(
                        name: "FK_listing_photos_listings_listing_id",
                        column: x => x.listing_id,
                        principalSchema: "marketplace",
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_marketplace_listing_photos_order",
                schema: "marketplace",
                table: "listing_photos",
                columns: new[] { "listing_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ux_marketplace_listing_photo_asset",
                schema: "marketplace",
                table: "listing_photos",
                column: "asset_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_marketplace_listing_photo_primary",
                schema: "marketplace",
                table: "listing_photos",
                columns: new[] { "listing_id", "is_primary" },
                unique: true,
                filter: "is_primary = true");

            migrationBuilder.CreateIndex(
                name: "ix_marketplace_listings_printing_status",
                schema: "marketplace",
                table: "listings",
                columns: new[] { "printing_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_marketplace_listings_seller_status",
                schema: "marketplace",
                table: "listings",
                columns: new[] { "seller_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_marketplace_listings_status_created",
                schema: "marketplace",
                table: "listings",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_marketplace_listings_active_item",
                schema: "marketplace",
                table: "listings",
                column: "collectible_item_id",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_marketplace_seller_status",
                schema: "marketplace",
                table: "seller_profiles",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "listing_photos",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "seller_profiles",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "listings",
                schema: "marketplace");
        }
    }
}
