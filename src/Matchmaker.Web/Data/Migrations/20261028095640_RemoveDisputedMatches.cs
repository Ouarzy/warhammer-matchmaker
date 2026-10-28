using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchmaker.Web.Data.Migrations
{
    /// <summary>
    /// Le statut « contesté » n'existe plus (contester propose désormais un autre score) : les anciennes
    /// parties contestées, sans effet sur l'Elo, sont supprimées avec ce qui s'y rattache.
    /// </summary>
    public partial class RemoveDisputedMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "archived_army_lists", "match_report_codes", "scheduled_searches" })
                migrationBuilder.Sql($"DELETE FROM {table} WHERE match_id IN (SELECT id FROM matches WHERE status = 'Disputed');");
            migrationBuilder.Sql("DELETE FROM matches WHERE status = 'Disputed';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Les parties supprimées ne peuvent pas être restaurées.
        }
    }
}
