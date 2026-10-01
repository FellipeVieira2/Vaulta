using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompleteOrderShippingAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Refuse legacy values that exceed the new domain limit; never truncate addresses.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM orders.orders WHERE length(shipping_state) > 100) THEN
                        RAISE EXCEPTION 'Order shipping_state exceeds 100 characters. Review legacy addresses before applying this migration.';
                    END IF;
                END $$;
                """);
            migrationBuilder.AlterColumn<string>(
                name: "shipping_state",
                schema: "orders",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_complement",
                schema: "orders",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_neighborhood",
                schema: "orders",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_number",
                schema: "orders",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_recipient",
                schema: "orders",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "shipping_complement",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipping_neighborhood",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipping_number",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipping_recipient",
                schema: "orders",
                table: "orders");

            migrationBuilder.AlterColumn<string>(
                name: "shipping_state",
                schema: "orders",
                table: "orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}
