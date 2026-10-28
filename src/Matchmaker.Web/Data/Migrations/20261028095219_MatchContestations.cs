using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchmaker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MatchContestations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "contestations",
                table: "matches",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "contestations",
                table: "matches");
        }
    }
}
