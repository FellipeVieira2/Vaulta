using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DailyCardMarketSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_card_market_snapshots",
                schema: "catalog",
                columns: table => new
                {
                    printing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    market_day = table.Column<DateOnly>(type: "date", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    refresh_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_card_market_snapshots", x => new { x.printing_id, x.market_day });
                    table.ForeignKey(
                        name: "FK_daily_card_market_snapshots_printings_printing_id",
                        column: x => x.printing_id,
                        principalSchema: "catalog",
                        principalTable: "printings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_card_market_snapshots",
                schema: "catalog");
        }
    }
}
