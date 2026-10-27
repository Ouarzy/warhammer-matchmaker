using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchmaker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Matches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "players",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    declarer_steam_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    first_accepted = table.Column<bool>(type: "boolean", nullable: false),
                    first_rating_change = table.Column<int>(type: "integer", nullable: true),
                    first_victory_points = table.Column<int>(type: "integer", nullable: true),
                    second_accepted = table.Column<bool>(type: "boolean", nullable: false),
                    second_rating_change = table.Column<int>(type: "integer", nullable: true),
                    second_victory_points = table.Column<int>(type: "integer", nullable: true),
                    first_army_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    first_steam_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    first_ranked = table.Column<bool>(type: "boolean", nullable: false),
                    first_rating = table.Column<int>(type: "integer", nullable: false),
                    first_game = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    first_format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    second_army_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    second_enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    second_steam_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    second_ranked = table.Column<bool>(type: "boolean", nullable: false),
                    second_rating = table.Column<int>(type: "integer", nullable: false),
                    second_game = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    second_format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matches", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_players_rating",
                table: "players",
                column: "rating");

            migrationBuilder.CreateIndex(
                name: "IX_matches_status",
                table: "matches",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "matches");

            migrationBuilder.DropIndex(
                name: "IX_players_rating",
                table: "players");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "players");
        }
    }
}
