using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogVisionMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "raw_value",
                schema: "catalog",
                table: "variants",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "metadata_json",
                schema: "catalog",
                table: "printings",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_pricing_json",
                schema: "catalog",
                table: "printings",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "metadata_json",
                schema: "catalog",
                table: "printings");

            migrationBuilder.DropColumn(
                name: "source_pricing_json",
                schema: "catalog",
                table: "printings");

            migrationBuilder.AlterColumn<string>(
                name: "raw_value",
                schema: "catalog",
                table: "variants",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
