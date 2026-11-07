using Kviz.DataAccess.Configurations;
using Kviz.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kviz.DataAccess;

public class KvizDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public KvizDbContext(DbContextOptions<KvizDbContext> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerOption> AnswerOptions => Set<AnswerOption>();
    public DbSet<QuizFavorite> QuizFavorites => Set<QuizFavorite>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<GameQuestion> GameQuestions => Set<GameQuestion>();
    public DbSet<GameTeam> GameTeams => Set<GameTeam>();
    public DbSet<GamePlayer> GamePlayers => Set<GamePlayer>();
    public DbSet<PlayerAnswer> PlayerAnswers => Set<PlayerAnswer>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(KvizDbContext).Assembly);
    }
}
