using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedPokemonGame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "catalog",
                table: "games",
                columns: new[] { "id", "code", "name" },
                values: new object[] { new Guid("f18fd4d1-2514-4b19-9eaa-f33c04564c7b"), "pokemon", "Pokémon" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "catalog",
                table: "games",
                keyColumn: "id",
                keyValue: new Guid("f18fd4d1-2514-4b19-9eaa-f33c04564c7b"));

        }
    }
}
