using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OwnedCatalogArtwork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "artwork_asset_id",
                schema: "catalog",
                table: "printings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "artwork_checked_at",
                schema: "catalog",
                table: "printings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "artwork_e_tag",
                schema: "catalog",
                table: "printings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "artwork_import_error",
                schema: "catalog",
                table: "printings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "artwork_import_status",
                schema: "catalog",
                table: "printings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "artwork_last_modified",
                schema: "catalog",
                table: "printings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "artwork_sha256",
                schema: "catalog",
                table: "printings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "thumbnail_asset_id",
                schema: "catalog",
                table: "printings",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "artwork_asset_id",
                schema: "catalog",
                table: "printings");

            migrationBuilder.DropColumn(
                name: "artwork_checked_at",
                schema: "catalog",
                table: "printings");

            migrationBuilder.DropColumn(
                name: "artwork_e_tag",
                schema: "catalog",
                table: "printings");

            migrationBuilder.DropColumn(
                name: "artwork_import_error",
                schema: "catalog",
                table: "printings");

            migrationBuilder.DropColumn(
                name: "artwork_import_status",
                schema: "catalog",
                table: "printings");

            migrationBuilder.DropColumn(
                name: "artwork_last_modified",
                schema: "catalog",
                table: "printings");

            migrationBuilder.DropColumn(
                name: "artwork_sha256",
                schema: "catalog",
                table: "printings");

            migrationBuilder.DropColumn(
                name: "thumbnail_asset_id",
                schema: "catalog",
                table: "printings");
        }
    }
}
