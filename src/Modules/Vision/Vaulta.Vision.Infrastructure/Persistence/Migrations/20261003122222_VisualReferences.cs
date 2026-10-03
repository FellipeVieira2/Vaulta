using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Vision.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VisualReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vision");

            migrationBuilder.CreateTable(
                name: "visual_references",
                schema: "vision",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    printing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    model_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    model_manifest_json = table.Column<string>(type: "jsonb", nullable: false),
                    origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    image_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    original_source_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    vector_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    dimension = table.Column<int>(type: "integer", nullable: false),
                    vector = table.Column<float[]>(type: "real[]", nullable: true),
                    error_category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visual_references", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_visual_references_model_version_status",
                schema: "vision",
                table: "visual_references",
                columns: new[] { "model_version", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_visual_references_printing_id_model_version_source_asset_id~",
                schema: "vision",
                table: "visual_references",
                columns: new[] { "printing_id", "model_version", "source_asset_id", "origin" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "visual_references",
                schema: "vision");
        }
    }
}
