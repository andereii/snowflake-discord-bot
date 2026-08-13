using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Snowflake.Bot.Migrations
{
    /// <summary>
    /// Añade a GuildConfigs los interruptores y ajustes preparados para el
    /// panel web: rol DJ, plantilla de canales temporales y los toggles de
    /// chat IA y descargas (activados por defecto en los servidores existentes).
    /// </summary>
    public partial class AddGuildFeatureToggles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<ulong>(
                name: "DjRoleId",
                table: "GuildConfigs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GeminiChatEnabled",
                table: "GuildConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "DownloadsEnabled",
                table: "GuildConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TempChannelNameTemplate",
                table: "GuildConfigs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DjRoleId",
                table: "GuildConfigs");

            migrationBuilder.DropColumn(
                name: "GeminiChatEnabled",
                table: "GuildConfigs");

            migrationBuilder.DropColumn(
                name: "DownloadsEnabled",
                table: "GuildConfigs");

            migrationBuilder.DropColumn(
                name: "TempChannelNameTemplate",
                table: "GuildConfigs");
        }
    }
}
