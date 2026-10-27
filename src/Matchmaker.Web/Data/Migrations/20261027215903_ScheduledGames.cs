using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchmaker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "scheduled_for",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "scheduled_searches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    planned_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    player_army_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    player_steam_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    player_ranked = table.Column<bool>(type: "boolean", nullable: false),
                    player_rating = table.Column<int>(type: "integer", nullable: false),
                    player_game = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    player_format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduled_searches", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matches_scheduled_for",
                table: "matches",
                column: "scheduled_for");

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_searches_match_id",
                table: "scheduled_searches",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_searches_status_planned_for",
                table: "scheduled_searches",
                columns: new[] { "status", "planned_for" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduled_searches");

            migrationBuilder.DropIndex(
                name: "IX_matches_scheduled_for",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "scheduled_for",
                table: "matches");
        }
    }
}
