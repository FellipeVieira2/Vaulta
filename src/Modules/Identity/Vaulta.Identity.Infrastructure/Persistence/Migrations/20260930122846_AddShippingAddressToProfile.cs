using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingAddressToProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "shipping_city",
                schema: "identity",
                table: "user_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_state",
                schema: "identity",
                table: "user_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_street",
                schema: "identity",
                table: "user_profiles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_zip_code",
                schema: "identity",
                table: "user_profiles",
                type: "character varying(9)",
                maxLength: 9,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "shipping_city",
                schema: "identity",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "shipping_state",
                schema: "identity",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "shipping_street",
                schema: "identity",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "shipping_zip_code",
                schema: "identity",
                table: "user_profiles");
        }
    }
}
