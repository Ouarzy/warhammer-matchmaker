using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchmaker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RatingBefore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "first_rating_before",
                table: "matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "second_rating_before",
                table: "matches",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "first_rating_before",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "second_rating_before",
                table: "matches");
        }
    }
}
