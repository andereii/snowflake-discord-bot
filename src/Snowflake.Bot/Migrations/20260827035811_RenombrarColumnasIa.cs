using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Snowflake.Bot.Migrations
{
    /// <inheritdoc />
    public partial class RenombrarColumnasIa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GeminiSpontaneousEnabled",
                table: "GuildConfigs",
                newName: "AiSpontaneousEnabled");

            migrationBuilder.RenameColumn(
                name: "GeminiMentionsEnabled",
                table: "GuildConfigs",
                newName: "AiMentionsEnabled");

            migrationBuilder.RenameColumn(
                name: "GeminiChatEnabled",
                table: "GuildConfigs",
                newName: "AiChatEnabled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AiSpontaneousEnabled",
                table: "GuildConfigs",
                newName: "GeminiSpontaneousEnabled");

            migrationBuilder.RenameColumn(
                name: "AiMentionsEnabled",
                table: "GuildConfigs",
                newName: "GeminiMentionsEnabled");

            migrationBuilder.RenameColumn(
                name: "AiChatEnabled",
                table: "GuildConfigs",
                newName: "GeminiChatEnabled");
        }
    }
}
