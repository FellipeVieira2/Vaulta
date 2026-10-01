using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrackPixExpiration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pix_expiration_date",
                schema: "payments",
                table: "payment_transactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pix_expiration_date",
                schema: "payments",
                table: "payment_transactions");
        }
    }
}
