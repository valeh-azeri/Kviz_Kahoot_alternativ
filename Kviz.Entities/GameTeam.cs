namespace Kviz.Entities;

public class GameTeam
{
    public Guid Id { get; set; }
    public Guid GameSessionId { get; set; }
    public GameSession? GameSession { get; set; }
    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ColorIndex { get; set; }

    public ICollection<GamePlayer> Players { get; set; } = new List<GamePlayer>();
}
