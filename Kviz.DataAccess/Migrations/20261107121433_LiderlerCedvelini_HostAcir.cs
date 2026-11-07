using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kviz.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class LiderlerCedvelini_HostAcir : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HostRevealsBoard",
                table: "GameSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HostRevealsBoard",
                table: "GameSessions");
        }
    }
}
