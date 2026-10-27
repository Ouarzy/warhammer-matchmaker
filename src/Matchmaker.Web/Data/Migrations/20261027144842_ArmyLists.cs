using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchmaker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArmyLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "army_lists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_steam_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    roster = table.Column<string>(type: "jsonb", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    game = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_army_lists", x => x.id);
                    table.ForeignKey(
                        name: "FK_army_lists_players_owner_steam_id",
                        column: x => x.owner_steam_id,
                        principalTable: "players",
                        principalColumn: "steam_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_army_lists_owner_steam_id",
                table: "army_lists",
                column: "owner_steam_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "army_lists");
        }
    }
}
