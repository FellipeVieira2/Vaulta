using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Collection.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrackListingOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "listed_by_id",
                schema: "collection",
                table: "collectible_items",
                type: "uuid",
                nullable: true);

            // Collection runs before Marketplace on a fresh database. On upgrades,
            // reserve existing active/sold units before accepting collection edits.
            migrationBuilder.Sql("""
                DO $migration$
                BEGIN
                    IF to_regclass('marketplace.listings') IS NOT NULL THEN
                        IF EXISTS (
                            SELECT 1 FROM marketplace.listings l
                            JOIN collection.collectible_items i ON i.id = l.collectible_item_id
                            WHERE l.status IN ('active', 'sold', 'publishing') AND i.status = 'ACTIVE'
                            GROUP BY i.id HAVING count(*) > 1
                        ) THEN
                            RAISE EXCEPTION 'Multiple outstanding listings for a collection unit. Resolve ownership before migration.';
                        END IF;
                        UPDATE collection.collectible_items i
                        SET listed_by_id = l.id, version = gen_random_uuid(), updated_at = now()
                        FROM marketplace.listings l
                        WHERE i.id = l.collectible_item_id AND i.user_id = l.seller_user_id
                            AND i.status = 'ACTIVE' AND l.status IN ('active', 'sold', 'publishing');
                    END IF;
                END $migration$;
                """);

            migrationBuilder.CreateTable(
                name: "item_ownership_transfers",
                schema: "collection",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buyer_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_brl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_ownership_transfers", x => x.order_id);
                    table.ForeignKey(
                        name: "FK_item_ownership_transfers_collectible_items_buyer_item_id",
                        column: x => x.buyer_item_id,
                        principalSchema: "collection",
                        principalTable: "collectible_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_ownership_transfers_collectible_items_seller_item_id",
                        column: x => x.seller_item_id,
                        principalSchema: "collection",
                        principalTable: "collectible_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_collection_transfer_buyer_item",
                schema: "collection",
                table: "item_ownership_transfers",
                column: "buyer_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_collection_transfer_seller_item",
                schema: "collection",
                table: "item_ownership_transfers",
                column: "seller_item_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_ownership_transfers",
                schema: "collection");

            migrationBuilder.DropColumn(
                name: "listed_by_id",
                schema: "collection",
                table: "collectible_items");
        }
    }
}
