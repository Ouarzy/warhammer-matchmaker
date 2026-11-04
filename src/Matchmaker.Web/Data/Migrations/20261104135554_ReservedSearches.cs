using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchmaker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReservedSearches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "player_reserved_for_steam_id",
                table: "scheduled_searches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "first_reserved_for_steam_id",
                table: "matches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "second_reserved_for_steam_id",
                table: "matches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "player_reserved_for_steam_id",
                table: "scheduled_searches");

            migrationBuilder.DropColumn(
                name: "first_reserved_for_steam_id",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "second_reserved_for_steam_id",
                table: "matches");
        }
    }
}
