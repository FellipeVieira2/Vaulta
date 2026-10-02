using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Marketplace.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ListingDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "client_draft_key",
                schema: "marketplace",
                table: "listings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "draft_creation_fingerprint",
                schema: "marketplace",
                table: "listings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "publication_key",
                schema: "marketplace",
                table: "listings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "publication_version",
                schema: "marketplace",
                table: "listings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_marketplace_listing_draft_key",
                schema: "marketplace",
                table: "listings",
                columns: new[] { "seller_user_id", "client_draft_key" },
                unique: true,
                filter: "client_draft_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_marketplace_listing_draft_key",
                schema: "marketplace",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "client_draft_key",
                schema: "marketplace",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "draft_creation_fingerprint",
                schema: "marketplace",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "publication_key",
                schema: "marketplace",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "publication_version",
                schema: "marketplace",
                table: "listings");
        }
    }
}
