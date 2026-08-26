using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Snowflake.Bot.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftbanAndHardmute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HardmuteBackups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    UserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    RoleIds = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HardmuteBackups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HardmuteBackups_GuildId_UserId",
                table: "HardmuteBackups",
                columns: new[] { "GuildId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HardmuteBackups");
        }
    }
}
