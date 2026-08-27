using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Snowflake.Bot.Migrations
{
    /// <inheritdoc />
    public partial class AgregarPollCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PollCount",
                table: "GuildConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PollCount",
                table: "GuildConfigs");
        }
    }
}
