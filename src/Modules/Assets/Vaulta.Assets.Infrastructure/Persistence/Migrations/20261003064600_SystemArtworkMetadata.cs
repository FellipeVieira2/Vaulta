using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Assets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SystemArtworkMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "height",
                schema: "assets",
                table: "assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_url",
                schema: "assets",
                table: "assets",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "width",
                schema: "assets",
                table: "assets",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "height",
                schema: "assets",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "source_url",
                schema: "assets",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "width",
                schema: "assets",
                table: "assets");
        }
    }
}
