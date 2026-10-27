using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchmaker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AbandonAndAutomaticConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "abandoned_by_steam_id",
                table: "matches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "confirmed_automatically",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "declared_at",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "abandoned_by_steam_id",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "confirmed_automatically",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "declared_at",
                table: "matches");
        }
    }
}
