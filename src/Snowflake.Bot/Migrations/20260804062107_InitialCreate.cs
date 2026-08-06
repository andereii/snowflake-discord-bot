using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Snowflake.Bot.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ColorRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    RoleId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ColorHex = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColorRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GuildConfigs",
                columns: table => new
                {
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModLogChannelId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    WelcomeChannelId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    WelcomeMessage = table.Column<string>(type: "TEXT", nullable: true),
                    HubChannelId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    TempChannelNameTemplate = table.Column<string>(type: "TEXT", nullable: true),
                    Volume = table.Column<int>(type: "INTEGER", nullable: true),
                    DjRoleId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    GeminiChatEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    DownloadsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildConfigs", x => x.GuildId);
                });

            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    TargetUserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    TargetTag = table.Column<string>(type: "TEXT", nullable: false),
                    ModeratorId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ModeratorTag = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TempChannels",
                columns: table => new
                {
                    ChannelId = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    OwnerUserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TempChannels", x => x.ChannelId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColorRoles_GuildId",
                table: "ColorRoles",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_ColorRoles_GuildId_RoleId",
                table: "ColorRoles",
                columns: new[] { "GuildId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_GuildId_TargetUserId",
                table: "Incidents",
                columns: new[] { "GuildId", "TargetUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_TempChannels_GuildId",
                table: "TempChannels",
                column: "GuildId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColorRoles");

            migrationBuilder.DropTable(
                name: "GuildConfigs");

            migrationBuilder.DropTable(
                name: "Incidents");

            migrationBuilder.DropTable(
                name: "TempChannels");
        }
    }
}
