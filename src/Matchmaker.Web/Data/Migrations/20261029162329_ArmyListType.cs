using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchmaker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArmyListType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.AddColumn<string>(
                    name: "type",
                    table: table,
                    type: "character varying(16)",
                    maxLength: 16,
                    nullable: false,
                    defaultValue: "Hybrid");

                // Les listes cochées « Liste d'équipe » le restent ; les autres deviennent hybrides.
                migrationBuilder.Sql($"UPDATE {table} SET type = 'Team' WHERE is_team_list");

                migrationBuilder.DropColumn(name: "is_team_list", table: table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.AddColumn<bool>(
                    name: "is_team_list",
                    table: table,
                    type: "boolean",
                    nullable: false,
                    defaultValue: false);

                migrationBuilder.Sql($"UPDATE {table} SET is_team_list = true WHERE type = 'Team'");

                migrationBuilder.DropColumn(name: "type", table: table);
            }
        }

        private static readonly string[] Tables = ["army_lists", "archived_army_lists"];
    }
}
