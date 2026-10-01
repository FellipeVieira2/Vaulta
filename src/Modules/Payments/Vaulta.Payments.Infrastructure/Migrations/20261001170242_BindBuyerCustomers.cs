using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BindBuyerCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "buyer_customers",
                schema: "payments",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    external_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    asaas_customer_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_buyer_customers", x => x.user_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_buyer_customers_asaas_customer_id",
                schema: "payments",
                table: "buyer_customers",
                column: "asaas_customer_id",
                unique: true,
                filter: "asaas_customer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_buyer_customers_external_reference",
                schema: "payments",
                table: "buyer_customers",
                column: "external_reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "buyer_customers",
                schema: "payments");
        }
    }
}
