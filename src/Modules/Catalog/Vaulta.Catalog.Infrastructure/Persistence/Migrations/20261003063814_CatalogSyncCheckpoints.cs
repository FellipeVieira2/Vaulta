using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogSyncCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "progress_json",
                schema: "catalog",
                table: "sync_runs",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "progress_json",
                schema: "catalog",
                table: "sync_runs");
        }
    }
}
