using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaulta.Reviews.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeparateReviewRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reviewed_role",
                schema: "reviews",
                table: "reviews",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "UNKNOWN");

            // The role belongs to the historical order, not to the user's current
            // seller profile. Unmatched legacy rows stay UNKNOWN and are excluded.
            migrationBuilder.Sql("""
                UPDATE reviews.reviews r
                SET reviewed_role = CASE
                    WHEN r.reviewer_id = o.buyer_id AND r.reviewed_user_id = o.seller_id THEN 'SELLER'
                    WHEN r.reviewer_id = o.seller_id AND r.reviewed_user_id = o.buyer_id THEN 'BUYER'
                    ELSE 'UNKNOWN' END
                FROM orders.orders o
                WHERE r.order_id = o.id AND o.status = 'delivered'
                  AND o.buyer_id <> o.seller_id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_reviews_review_user_role",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "reviewed_user_id", "reviewed_role" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reviews_review_user_role",
                schema: "reviews",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "reviewed_role",
                schema: "reviews",
                table: "reviews");
        }
    }
}
