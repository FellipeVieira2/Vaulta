using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteShippingAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "shipping_city",
                schema: "identity",
                table: "user_profiles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_complement",
                schema: "identity",
                table: "user_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_neighborhood",
                schema: "identity",
                table: "user_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_number",
                schema: "identity",
                table: "user_profiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_recipient",
                schema: "identity",
                table: "user_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "shipping_complement",
                schema: "identity",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "shipping_neighborhood",
                schema: "identity",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "shipping_number",
                schema: "identity",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "shipping_recipient",
                schema: "identity",
                table: "user_profiles");

            migrationBuilder.AlterColumn<string>(
                name: "shipping_city",
                schema: "identity",
                table: "user_profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
