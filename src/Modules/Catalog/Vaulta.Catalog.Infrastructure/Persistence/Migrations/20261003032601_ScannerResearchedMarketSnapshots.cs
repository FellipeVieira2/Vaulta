using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScannerResearchedMarketSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scanner_research_snapshots",
                schema: "catalog",
                columns: table => new
                {
                    cache_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    market_day = table.Column<DateOnly>(type: "date", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    refresh_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scanner_research_snapshots", x => x.cache_key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_scanner_research_snapshots_refresh_after",
                schema: "catalog",
                table: "scanner_research_snapshots",
                column: "refresh_after");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scanner_research_snapshots",
                schema: "catalog");
        }
    }
}
