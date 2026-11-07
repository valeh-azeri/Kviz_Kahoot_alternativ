using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kviz.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class OyuncularaAvatar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowPhotoAvatars",
                table: "GameSessions",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Avatar",
                table: "GamePlayers",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowPhotoAvatars",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "Avatar",
                table: "GamePlayers");
        }
    }
}
