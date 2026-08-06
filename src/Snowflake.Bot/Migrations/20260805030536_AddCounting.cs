using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Snowflake.Bot.Migrations
{
    /// <inheritdoc />
    public partial class AddCounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CountingConfigs",
                columns: table => new
                {
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChannelId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    CurrentValue = table.Column<long>(type: "INTEGER", nullable: false),
                    LastUserId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    CurrentRecord = table.Column<long>(type: "INTEGER", nullable: false),
                    RecordAtChainStart = table.Column<long>(type: "INTEGER", nullable: false),
                    RecordCelebratedThisChain = table.Column<bool>(type: "INTEGER", nullable: false),
                    Base = table.Column<string>(type: "TEXT", nullable: false),
                    Goal = table.Column<long>(type: "INTEGER", nullable: true),
                    ExtraChancesPerDay = table.Column<int>(type: "INTEGER", nullable: false),
                    ExtraChancesUsedToday = table.Column<int>(type: "INTEGER", nullable: false),
                    LastExtraChanceResetDate = table.Column<string>(type: "TEXT", nullable: true),
                    EmojiCorrect = table.Column<string>(type: "TEXT", nullable: true),
                    EmojiIncorrect = table.Column<string>(type: "TEXT", nullable: true),
                    EmojiRecord = table.Column<string>(type: "TEXT", nullable: true),
                    LoseMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountingConfigs", x => x.GuildId);
                });

            migrationBuilder.CreateTable(
                name: "CountingStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    UserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    TotalCounts = table.Column<long>(type: "INTEGER", nullable: false),
                    IncorrectCounts = table.Column<long>(type: "INTEGER", nullable: false),
                    BestContribution = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountingStats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CountingStats_GuildId",
                table: "CountingStats",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_CountingStats_GuildId_UserId",
                table: "CountingStats",
                columns: new[] { "GuildId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CountingConfigs");

            migrationBuilder.DropTable(
                name: "CountingStats");
        }
    }
}
