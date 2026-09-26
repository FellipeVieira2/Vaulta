using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Collection.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "collection");

            migrationBuilder.CreateTable(
                name: "collection_entries",
                schema: "collection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    printing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_entries", x => x.id);
                    table.UniqueConstraint("ak_collection_entries_id_user", x => new { x.id, x.user_id });
                });

            migrationBuilder.CreateTable(
                name: "collectible_items",
                schema: "collection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    condition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    acquisition_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    acquisition_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    acquisition_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collectible_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_collectible_items_collection_entries_collection_entry_id_us~",
                        columns: x => new { x.collection_entry_id, x.user_id },
                        principalSchema: "collection",
                        principalTable: "collection_entries",
                        principalColumns: new[] { "id", "user_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collectible_item_assets",
                schema: "collection",
                columns: table => new
                {
                    collectible_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collectible_item_assets", x => new { x.collectible_item_id, x.asset_id });
                    table.ForeignKey(
                        name: "FK_collectible_item_assets_collectible_items_collectible_item_~",
                        column: x => x.collectible_item_id,
                        principalSchema: "collection",
                        principalTable: "collectible_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_collection_item_assets_order",
                schema: "collection",
                table: "collectible_item_assets",
                columns: new[] { "collectible_item_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ux_collection_item_asset_asset",
                schema: "collection",
                table: "collectible_item_assets",
                column: "asset_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_collection_item_asset_primary",
                schema: "collection",
                table: "collectible_item_assets",
                columns: new[] { "collectible_item_id", "is_primary" },
                unique: true,
                filter: "is_primary = true");

            migrationBuilder.CreateIndex(
                name: "IX_collectible_items_collection_entry_id_user_id",
                schema: "collection",
                table: "collectible_items",
                columns: new[] { "collection_entry_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_collection_items_entry_status",
                schema: "collection",
                table: "collectible_items",
                columns: new[] { "collection_entry_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_collection_items_user_condition_status",
                schema: "collection",
                table: "collectible_items",
                columns: new[] { "user_id", "condition", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_collection_items_user_status_created",
                schema: "collection",
                table: "collectible_items",
                columns: new[] { "user_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_collection_entries_user_printing_no_variant",
                schema: "collection",
                table: "collection_entries",
                columns: new[] { "user_id", "printing_id" },
                unique: true,
                filter: "variant_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_collection_entries_user_printing_variant",
                schema: "collection",
                table: "collection_entries",
                columns: new[] { "user_id", "printing_id", "variant_id" },
                unique: true,
                filter: "variant_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collectible_item_assets",
                schema: "collection");

            migrationBuilder.DropTable(
                name: "collectible_items",
                schema: "collection");

            migrationBuilder.DropTable(
                name: "collection_entries",
                schema: "collection");
        }
    }
}
