using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kviz.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class SualSekliMuellifMelumati : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageCredit",
                table: "Questions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageCredit",
                table: "GameQuestions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageCredit",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "ImageCredit",
                table: "GameQuestions");
        }
    }
}
