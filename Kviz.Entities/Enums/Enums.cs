namespace Kviz.Entities.Enums;

/// <summary>Sual növü.</summary>
public enum QuestionType
{
    MultipleChoice = 0,
    TrueFalse = 1,
    TypeAnswer = 2,
    Slider = 3,
    Puzzle = 4,
    Poll = 5,
    WordCloud = 6,
    OpenEnded = 7
}

/// <summary>Sualın xal rejimi.</summary>
public enum PointsMode
{
    Standard = 0,
    Double = 1,
    NoPoints = 2
}

public enum QuizVisibility
{
    Private = 0,
    Public = 1
}

public enum GameMode
{
    LiveClassic = 0,
    LiveTeam = 1,
    Challenge = 2
}

public enum GameStatus
{
    Lobby = 0,
    InProgress = 1,
    Finished = 2,
    Cancelled = 3
}

/// <summary>Oyunçunun istifadə etdiyi güclər (bit bayraqları).</summary>
[Flags]
public enum PowerUpFlags
{
    None = 0,
    DoublePoints = 1,
    FiftyFifty = 2,
    StreakShield = 4
}
