using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Collection.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionEntryUserCreatedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_collection_entries_user_created",
                schema: "collection",
                table: "collection_entries",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_collection_entries_user_created",
                schema: "collection",
                table: "collection_entries");
        }
    }
}
