using Kviz.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kviz.DataAccess.Repositories;

public interface IRepository<T> where T : class
{
    IQueryable<T> Query();
    IQueryable<T> QueryNoTracking();
    Task<T?> FindAsync(object key, CancellationToken ct = default);
    void Add(T entity);
    Task AddAsync(T entity, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);
    void Remove(T entity);
    void RemoveRange(IEnumerable<T> entities);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public class Repository<T> : IRepository<T> where T : class
{
    protected readonly KvizDbContext Context;
    public Repository(KvizDbContext context) => Context = context;

    public IQueryable<T> Query() => Context.Set<T>();
    public IQueryable<T> QueryNoTracking() => Context.Set<T>().AsNoTracking();
    public async Task<T?> FindAsync(object key, CancellationToken ct = default) => await Context.Set<T>().FindAsync(new[] { key }, ct);
    public void Add(T entity) => Context.Set<T>().Add(entity);
    public async Task AddAsync(T entity, CancellationToken ct = default) => await Context.Set<T>().AddAsync(entity, ct);
    public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default) => Context.Set<T>().AddRangeAsync(entities, ct);
    public void Remove(T entity) => Context.Set<T>().Remove(entity);
    public void RemoveRange(IEnumerable<T> entities) => Context.Set<T>().RemoveRange(entities);
}

public class UnitOfWork : IUnitOfWork
{
    private readonly KvizDbContext _context;
    public UnitOfWork(KvizDbContext context) => _context = context;
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}

public interface IQuizRepository : IRepository<Quiz>
{
    /// <summary>Kvizi sualları və variantları ilə (izlənən) gətirir.</summary>
    Task<Quiz?> GetFullAsync(Guid id, CancellationToken ct = default);
    Task<Quiz?> GetFullNoTrackingAsync(Guid id, CancellationToken ct = default);
}

public class QuizRepository : Repository<Quiz>, IQuizRepository
{
    public QuizRepository(KvizDbContext context) : base(context) { }

    public Task<Quiz?> GetFullAsync(Guid id, CancellationToken ct = default) =>
        Context.Quizzes.Include(q => q.Questions).ThenInclude(x => x.Options)
            .AsSplitQuery().FirstOrDefaultAsync(q => q.Id == id, ct);

    public Task<Quiz?> GetFullNoTrackingAsync(Guid id, CancellationToken ct = default) =>
        Context.Quizzes.AsNoTracking().Include(q => q.Category).Include(q => q.Owner)
            .Include(q => q.Questions).ThenInclude(x => x.Options)
            .AsSplitQuery().FirstOrDefaultAsync(q => q.Id == id, ct);
}

public interface IGameRepository : IRepository<GameSession>
{
    Task<GameSession?> GetFullAsync(Guid id, CancellationToken ct = default);
    Task<GameSession?> GetActiveByPinAsync(string pin, CancellationToken ct = default);
    Task<bool> PinInUseAsync(string pin, CancellationToken ct = default);
}

public class GameRepository : Repository<GameSession>, IGameRepository
{
    public GameRepository(KvizDbContext context) : base(context) { }

    public Task<GameSession?> GetFullAsync(Guid id, CancellationToken ct = default) =>
        Context.GameSessions.Include(g => g.Questions.OrderBy(q => q.Order)).Include(g => g.Teams.OrderBy(t => t.Order))
            .Include(g => g.Players).AsSplitQuery().FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<GameSession?> GetActiveByPinAsync(string pin, CancellationToken ct = default) =>
        Context.GameSessions.FirstOrDefaultAsync(g => g.Pin == pin &&
            (g.Status == Entities.Enums.GameStatus.Lobby || g.Status == Entities.Enums.GameStatus.InProgress), ct);

    public Task<bool> PinInUseAsync(string pin, CancellationToken ct = default) =>
        Context.GameSessions.AnyAsync(g => g.Pin == pin &&
            (g.Status == Entities.Enums.GameStatus.Lobby || g.Status == Entities.Enums.GameStatus.InProgress), ct);
}
