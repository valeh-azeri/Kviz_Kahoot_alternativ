using Kviz.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kviz.DataAccess.Configurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(x => x.DisplayName).IsRequired().HasMaxLength(60);
    }
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.Property(x => x.Name).IsRequired().HasMaxLength(60);
        b.Property(x => x.Icon).IsRequired().HasMaxLength(16);
        b.HasIndex(x => x.SortOrder);

        b.HasData(
            new Category { Id = 1, Name = "Ümumi biliklər", Icon = "🌍", SortOrder = 1 },
            new Category { Id = 2, Name = "Riyaziyyat", Icon = "➗", SortOrder = 2 },
            new Category { Id = 3, Name = "Elm", Icon = "🔬", SortOrder = 3 },
            new Category { Id = 4, Name = "Tarix", Icon = "🏛️", SortOrder = 4 },
            new Category { Id = 5, Name = "Coğrafiya", Icon = "🗺️", SortOrder = 5 },
            new Category { Id = 6, Name = "Dil və ədəbiyyat", Icon = "📚", SortOrder = 6 },
            new Category { Id = 7, Name = "Xarici dillər", Icon = "🗣️", SortOrder = 7 },
            new Category { Id = 8, Name = "İncəsənət və musiqi", Icon = "🎨", SortOrder = 8 },
            new Category { Id = 9, Name = "İdman", Icon = "⚽", SortOrder = 9 },
            new Category { Id = 10, Name = "Texnologiya", Icon = "💻", SortOrder = 10 },
            new Category { Id = 11, Name = "Əyləncə", Icon = "🎉", SortOrder = 11 },
            new Category { Id = 12, Name = "Digər", Icon = "✨", SortOrder = 12 });
    }
}

public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(60);
        b.Property(x => x.Value).IsRequired().HasMaxLength(500);
    }
}

public class QuizConfiguration : IEntityTypeConfiguration<Quiz>
{
    public void Configure(EntityTypeBuilder<Quiz> b)
    {
        b.Property(x => x.Title).IsRequired().HasMaxLength(120);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.CoverImageUrl).HasMaxLength(500);
        b.Property(x => x.Language).IsRequired().HasMaxLength(8);

        b.HasOne(x => x.Owner).WithMany(u => u.Quizzes).HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Category).WithMany(c => c.Quizzes).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.Visibility, x.CreatedAt });
        b.HasIndex(x => new { x.OwnerId, x.UpdatedAt });
        b.HasIndex(x => x.Title);
    }
}

public class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> b)
    {
        b.Property(x => x.Text).IsRequired().HasMaxLength(500);
        b.Property(x => x.ImageUrl).HasMaxLength(500);
        b.Property(x => x.RelatedImageUrl).HasMaxLength(500);
        b.Property(x => x.RelatedImageCredit).HasMaxLength(300);
        b.HasOne(x => x.Quiz).WithMany(q => q.Questions).HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.QuizId, x.Order });
    }
}

public class AnswerOptionConfiguration : IEntityTypeConfiguration<AnswerOption>
{
    public void Configure(EntityTypeBuilder<AnswerOption> b)
    {
        b.Property(x => x.Text).IsRequired().HasMaxLength(200);
        b.HasOne(x => x.Question).WithMany(q => q.Options).HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.QuestionId, x.Order });
    }
}

public class QuizFavoriteConfiguration : IEntityTypeConfiguration<QuizFavorite>
{
    public void Configure(EntityTypeBuilder<QuizFavorite> b)
    {
        b.HasKey(x => new { x.UserId, x.QuizId });
        // Bir neçə cascade yolunun qarşısını almaq üçün istifadəçi tərəfi NoAction-dır.
        b.HasOne(x => x.User).WithMany(u => u.Favorites).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Quiz).WithMany(q => q.Favorites).HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class GameSessionConfiguration : IEntityTypeConfiguration<GameSession>
{
    public void Configure(EntityTypeBuilder<GameSession> b)
    {
        b.Property(x => x.Pin).IsRequired().HasMaxLength(8);
        b.Property(x => x.QuizTitle).IsRequired().HasMaxLength(120);
        b.Property(x => x.LeaderboardEvery).HasDefaultValue(1);

        b.HasOne(x => x.Host).WithMany().HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Quiz).WithMany(q => q.Games).HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.SetNull);

        // Eyni PIN yalnız aktiv (Lobby=0, InProgress=1) oyunlar arasında təkrarlana bilməz.
        b.HasIndex(x => x.Pin).IsUnique().HasFilter("[Status] IN (0, 1)");
        b.HasIndex(x => new { x.HostId, x.CreatedAt });
    }
}

public class GameQuestionConfiguration : IEntityTypeConfiguration<GameQuestion>
{
    public void Configure(EntityTypeBuilder<GameQuestion> b)
    {
        b.Property(x => x.Text).IsRequired().HasMaxLength(500);
        b.Property(x => x.ImageUrl).HasMaxLength(500);
        b.Property(x => x.OptionsJson).IsRequired();
        b.Property(x => x.RelatedImageUrl).HasMaxLength(500);
        b.Property(x => x.RelatedImageCredit).HasMaxLength(300);
        b.HasOne(x => x.GameSession).WithMany(g => g.Questions).HasForeignKey(x => x.GameSessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.GameSessionId, x.Order });
    }
}

public class GameTeamConfiguration : IEntityTypeConfiguration<GameTeam>
{
    public void Configure(EntityTypeBuilder<GameTeam> b)
    {
        b.Property(x => x.Name).IsRequired().HasMaxLength(40);
        b.HasOne(x => x.GameSession).WithMany(g => g.Teams).HasForeignKey(x => x.GameSessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class GamePlayerConfiguration : IEntityTypeConfiguration<GamePlayer>
{
    public void Configure(EntityTypeBuilder<GamePlayer> b)
    {
        b.Property(x => x.Nickname).IsRequired().HasMaxLength(24);
        b.Property(x => x.Avatar).HasMaxLength(80);
        b.HasOne(x => x.GameSession).WithMany(g => g.Players).HasForeignKey(x => x.GameSessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Team).WithMany(t => t.Players).HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.GameSessionId, x.Nickname }).IsUnique();
        b.HasIndex(x => x.Token).IsUnique();
    }
}

public class PlayerAnswerConfiguration : IEntityTypeConfiguration<PlayerAnswer>
{
    public void Configure(EntityTypeBuilder<PlayerAnswer> b)
    {
        b.Property(x => x.AnswerData).IsRequired().HasMaxLength(1000);
        b.HasOne(x => x.Player).WithMany(p => p.Answers).HasForeignKey(x => x.GamePlayerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Question).WithMany(q => q.Answers).HasForeignKey(x => x.GameQuestionId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.GamePlayerId, x.GameQuestionId }).IsUnique();
        b.HasIndex(x => x.GameQuestionId);
    }
}
