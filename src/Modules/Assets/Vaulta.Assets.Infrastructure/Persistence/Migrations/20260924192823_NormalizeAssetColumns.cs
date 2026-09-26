using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Assets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeAssetColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Visibility",
                schema: "assets",
                table: "assets",
                newName: "visibility");

            migrationBuilder.RenameColumn(
                name: "Status",
                schema: "assets",
                table: "assets",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Sha256",
                schema: "assets",
                table: "assets",
                newName: "sha256");

            migrationBuilder.RenameColumn(
                name: "Purpose",
                schema: "assets",
                table: "assets",
                newName: "purpose");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "assets",
                table: "assets",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "OwnerId",
                schema: "assets",
                table: "assets",
                newName: "owner_id");

            migrationBuilder.RenameColumn(
                name: "ObjectKey",
                schema: "assets",
                table: "assets",
                newName: "object_key");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                schema: "assets",
                table: "assets",
                newName: "deleted_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                schema: "assets",
                table: "assets",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ContentType",
                schema: "assets",
                table: "assets",
                newName: "content_type");

            migrationBuilder.RenameColumn(
                name: "ContentLength",
                schema: "assets",
                table: "assets",
                newName: "content_length");

            migrationBuilder.RenameColumn(
                name: "ConfirmedAt",
                schema: "assets",
                table: "assets",
                newName: "confirmed_at");

            migrationBuilder.RenameIndex(
                name: "IX_assets_Status",
                schema: "assets",
                table: "assets",
                newName: "IX_assets_status");

            migrationBuilder.RenameIndex(
                name: "IX_assets_OwnerId_CreatedAt",
                schema: "assets",
                table: "assets",
                newName: "IX_assets_owner_id_created_at");

            migrationBuilder.RenameIndex(
                name: "IX_assets_ObjectKey",
                schema: "assets",
                table: "assets",
                newName: "IX_assets_object_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "visibility",
                schema: "assets",
                table: "assets",
                newName: "Visibility");

            migrationBuilder.RenameColumn(
                name: "status",
                schema: "assets",
                table: "assets",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "sha256",
                schema: "assets",
                table: "assets",
                newName: "Sha256");

            migrationBuilder.RenameColumn(
                name: "purpose",
                schema: "assets",
                table: "assets",
                newName: "Purpose");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "assets",
                table: "assets",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "owner_id",
                schema: "assets",
                table: "assets",
                newName: "OwnerId");

            migrationBuilder.RenameColumn(
                name: "object_key",
                schema: "assets",
                table: "assets",
                newName: "ObjectKey");

            migrationBuilder.RenameColumn(
                name: "deleted_at",
                schema: "assets",
                table: "assets",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                schema: "assets",
                table: "assets",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "content_type",
                schema: "assets",
                table: "assets",
                newName: "ContentType");

            migrationBuilder.RenameColumn(
                name: "content_length",
                schema: "assets",
                table: "assets",
                newName: "ContentLength");

            migrationBuilder.RenameColumn(
                name: "confirmed_at",
                schema: "assets",
                table: "assets",
                newName: "ConfirmedAt");

            migrationBuilder.RenameIndex(
                name: "IX_assets_status",
                schema: "assets",
                table: "assets",
                newName: "IX_assets_Status");

            migrationBuilder.RenameIndex(
                name: "IX_assets_owner_id_created_at",
                schema: "assets",
                table: "assets",
                newName: "IX_assets_OwnerId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_assets_object_key",
                schema: "assets",
                table: "assets",
                newName: "IX_assets_ObjectKey");
        }
    }
}
