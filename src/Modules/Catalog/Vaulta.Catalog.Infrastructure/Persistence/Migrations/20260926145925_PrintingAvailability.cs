using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrintingAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "catalog",
                table: "printings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalog_printings_set_active",
                schema: "catalog",
                table: "printings",
                columns: new[] { "set_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_catalog_printings_set_active",
                schema: "catalog",
                table: "printings");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "catalog",
                table: "printings");
        }
    }
}
