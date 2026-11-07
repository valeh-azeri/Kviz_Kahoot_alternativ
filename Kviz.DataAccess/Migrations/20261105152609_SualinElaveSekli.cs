using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kviz.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class SualinElaveSekli : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ImageCredit",
                table: "Questions",
                newName: "RelatedImageCredit");

            migrationBuilder.RenameColumn(
                name: "ImageCredit",
                table: "GameQuestions",
                newName: "RelatedImageCredit");

            migrationBuilder.AddColumn<string>(
                name: "RelatedImageUrl",
                table: "Questions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedImageUrl",
                table: "GameQuestions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // Əvvəl internetdən gətirilən şəkil (müəllif məlumatı olan) ImageUrl-də saxlanılırdı.
            // İndi o, sualın şəklindən ayrı "əlavə şəkil" sahəsindədir: mövcud məlumatı köçürürük.
            migrationBuilder.Sql("UPDATE [Questions] SET [RelatedImageUrl] = [ImageUrl], [ImageUrl] = NULL WHERE [RelatedImageCredit] IS NOT NULL AND [ImageUrl] IS NOT NULL");
            migrationBuilder.Sql("UPDATE [GameQuestions] SET [RelatedImageUrl] = [ImageUrl], [ImageUrl] = NULL WHERE [RelatedImageCredit] IS NOT NULL AND [ImageUrl] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Geri qayıdanda əlavə şəkli yenidən ImageUrl-ə köçürürük (sualın öz şəkli yoxdursa)
            migrationBuilder.Sql("UPDATE [Questions] SET [ImageUrl] = [RelatedImageUrl] WHERE [ImageUrl] IS NULL AND [RelatedImageUrl] IS NOT NULL");
            migrationBuilder.Sql("UPDATE [GameQuestions] SET [ImageUrl] = [RelatedImageUrl] WHERE [ImageUrl] IS NULL AND [RelatedImageUrl] IS NOT NULL");

            migrationBuilder.DropColumn(
                name: "RelatedImageUrl",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "RelatedImageUrl",
                table: "GameQuestions");

            migrationBuilder.RenameColumn(
                name: "RelatedImageCredit",
                table: "Questions",
                newName: "ImageCredit");

            migrationBuilder.RenameColumn(
                name: "RelatedImageCredit",
                table: "GameQuestions",
                newName: "ImageCredit");
        }
    }
}
