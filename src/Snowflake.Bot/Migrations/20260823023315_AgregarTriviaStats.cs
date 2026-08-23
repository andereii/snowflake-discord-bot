using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Snowflake.Bot.Migrations
{
    /// <inheritdoc />
    public partial class AgregarTriviaStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TriviaStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    UserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    CorrectAnswers = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalAnswers = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentStreak = table.Column<int>(type: "INTEGER", nullable: false),
                    BestStreak = table.Column<int>(type: "INTEGER", nullable: false),
                    LastPlayedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TriviaStats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TriviaStats_GuildId",
                table: "TriviaStats",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_TriviaStats_GuildId_UserId",
                table: "TriviaStats",
                columns: new[] { "GuildId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TriviaStats");
        }
    }
}
