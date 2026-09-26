using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "external_ids",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_ids", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "games",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_games", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_runs",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    records_read = table.Column<int>(type: "integer", nullable: false),
                    records_created = table.Column<int>(type: "integer", nullable: false),
                    records_updated = table.Column<int>(type: "integer", nullable: false),
                    records_unresolved = table.Column<int>(type: "integer", nullable: false),
                    error_category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cards",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    supertype = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    subtypes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    rules_text = table.Column<string>(type: "text", nullable: true),
                    image_asset_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cards", x => x.id);
                    table.ForeignKey(
                        name: "FK_cards_games_game_id",
                        column: x => x.game_id,
                        principalSchema: "catalog",
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "series",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series", x => x.id);
                    table.ForeignKey(
                        name: "FK_series_games_game_id",
                        column: x => x.game_id,
                        principalSchema: "catalog",
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sets",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    release_date = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    symbol_asset_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    logo_asset_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sets", x => x.id);
                    table.ForeignKey(
                        name: "FK_sets_games_game_id",
                        column: x => x.game_id,
                        principalSchema: "catalog",
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sets_series_series_id",
                        column: x => x.series_id,
                        principalSchema: "catalog",
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "printings",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collector_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normalized_collector_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    language = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    rarity = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    raw_rarity = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_printings", x => x.id);
                    table.ForeignKey(
                        name: "FK_printings_cards_card_id",
                        column: x => x.card_id,
                        principalSchema: "catalog",
                        principalTable: "cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_printings_sets_set_id",
                        column: x => x.set_id,
                        principalSchema: "catalog",
                        principalTable: "sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "variants",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    printing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    raw_value = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_variants", x => x.id);
                    table.ForeignKey(
                        name: "FK_variants_printings_printing_id",
                        column: x => x.printing_id,
                        principalSchema: "catalog",
                        principalTable: "printings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_cards_game_name",
                schema: "catalog",
                table: "cards",
                columns: new[] { "game_id", "normalized_name" });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_external_ids_entity",
                schema: "catalog",
                table: "external_ids",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ux_catalog_external_ids_source",
                schema: "catalog",
                table: "external_ids",
                columns: new[] { "provider", "entity_type", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_catalog_games_code",
                schema: "catalog",
                table: "games",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalog_printings_card",
                schema: "catalog",
                table: "printings",
                column: "card_id");

            migrationBuilder.CreateIndex(
                name: "ux_catalog_printings_set_number_language",
                schema: "catalog",
                table: "printings",
                columns: new[] { "set_id", "normalized_collector_number", "language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_catalog_series_game_name",
                schema: "catalog",
                table: "series",
                columns: new[] { "game_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalog_sets_game_name",
                schema: "catalog",
                table: "sets",
                columns: new[] { "game_id", "normalized_name" });

            migrationBuilder.CreateIndex(
                name: "IX_sets_series_id",
                schema: "catalog",
                table: "sets",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "ux_catalog_sets_game_code",
                schema: "catalog",
                table: "sets",
                columns: new[] { "game_id", "code" },
                unique: true,
                filter: "code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_sync_runs_provider_started",
                schema: "catalog",
                table: "sync_runs",
                columns: new[] { "provider", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ux_catalog_variants_printing_code",
                schema: "catalog",
                table: "variants",
                columns: new[] { "printing_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_ids",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "sync_runs",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "variants",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "printings",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "cards",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "sets",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "series",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "games",
                schema: "catalog");
        }
    }
}
