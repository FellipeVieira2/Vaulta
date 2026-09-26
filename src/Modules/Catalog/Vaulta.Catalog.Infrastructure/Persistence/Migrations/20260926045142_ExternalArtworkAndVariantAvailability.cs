using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExternalArtworkAndVariantAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "catalog",
                table: "variants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "artwork_provider",
                schema: "catalog",
                table: "printings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_artwork_url",
                schema: "catalog",
                table: "printings",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "catalog",
                table: "variants");

            migrationBuilder.DropColumn(
                name: "artwork_provider",
                schema: "catalog",
                table: "printings");

            migrationBuilder.DropColumn(
                name: "external_artwork_url",
                schema: "catalog",
                table: "printings");
        }
    }
}
