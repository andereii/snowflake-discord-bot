using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Snowflake.Bot.Migrations
{
    /// <inheritdoc />
    public partial class AddYouTubeSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YouTubeSubscriptions",
                columns: table => new
                {
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    YTChannelId = table.Column<string>(type: "TEXT", nullable: false),
                    YTChannelName = table.Column<string>(type: "TEXT", nullable: false),
                    NotifyChannelId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    NotifyRoleId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    LastVideoId = table.Column<string>(type: "TEXT", nullable: true),
                    CustomMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YouTubeSubscriptions", x => x.GuildId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_YouTubeSubscriptions_YTChannelId",
                table: "YouTubeSubscriptions",
                column: "YTChannelId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YouTubeSubscriptions");
        }
    }
}
