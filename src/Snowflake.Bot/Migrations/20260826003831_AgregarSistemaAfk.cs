using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Snowflake.Bot.Migrations
{
    /// <inheritdoc />
    public partial class AgregarSistemaAfk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AfkIgnoredChannels",
                columns: table => new
                {
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ChannelId = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AfkIgnoredChannels", x => new { x.GuildId, x.ChannelId });
                });

            migrationBuilder.CreateTable(
                name: "AfkUsers",
                columns: table => new
                {
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    UserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    SetAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    OriginalNickname = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AfkUsers", x => new { x.GuildId, x.UserId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AfkIgnoredChannels_GuildId",
                table: "AfkIgnoredChannels",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_AfkUsers_GuildId",
                table: "AfkUsers",
                column: "GuildId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AfkIgnoredChannels");

            migrationBuilder.DropTable(
                name: "AfkUsers");
        }
    }
}
