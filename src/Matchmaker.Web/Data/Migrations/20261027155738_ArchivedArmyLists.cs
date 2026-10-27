using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchmaker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArchivedArmyLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "archived_army_lists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_steam_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    roster = table.Column<string>(type: "jsonb", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    game = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archived_army_lists", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_archived_army_lists_match_id",
                table: "archived_army_lists",
                column: "match_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archived_army_lists");
        }
    }
}
