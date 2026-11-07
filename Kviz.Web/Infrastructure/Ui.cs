using Kviz.Entities.Enums;

namespace Kviz.Web.Infrastructure;

/// <summary>View-larda istifadə olunan kiçik köməkçilər.</summary>
public static class Ui
{
    public static int Hue(Guid id) => (id.ToByteArray()[0] * 7 + id.ToByteArray()[3]) % 360;

    public static string TypeName(QuestionType t) => t switch
    {
        QuestionType.MultipleChoice => "Test",
        QuestionType.TrueFalse => "Doğru / Yanlış",
        QuestionType.TypeAnswer => "Cavabı yaz",
        QuestionType.Slider => "Sürüşdürücü",
        QuestionType.Puzzle => "Puzzle",
        QuestionType.Poll => "Sorğu",
        QuestionType.WordCloud => "Söz buludu",
        _ => "Açıq sual"
    };

    public static string TypeEmoji(QuestionType t) => t switch
    {
        QuestionType.MultipleChoice => "✅",
        QuestionType.TrueFalse => "⚖️",
        QuestionType.TypeAnswer => "⌨️",
        QuestionType.Slider => "🎚️",
        QuestionType.Puzzle => "🧩",
        QuestionType.Poll => "📊",
        QuestionType.WordCloud => "☁️",
        _ => "💬"
    };

    public static string ModeName(GameMode m) => m switch
    {
        GameMode.LiveClassic => "Canlı oyun",
        GameMode.LiveTeam => "Komanda oyunu",
        _ => "Tapşırıq"
    };

    public static string ModeEmoji(GameMode m) => m switch { GameMode.LiveClassic => "🎮", GameMode.LiveTeam => "👥", _ => "📝" };

    public static string StatusName(GameStatus s) => s switch
    {
        GameStatus.Lobby => "Gözləmə otağı",
        GameStatus.InProgress => "Davam edir",
        GameStatus.Finished => "Bitib",
        _ => "Ləğv edilib"
    };

    public static string Duration(int seconds)
    {
        if (seconds < 60) return $"{seconds} san";
        var m = seconds / 60;
        var s = seconds % 60;
        return s == 0 ? $"{m} dəq" : $"{m} dəq {s} san";
    }

    public static string Local(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    public static string LanguageName(string code) => code switch
    {
        "az" => "Azərbaycan", "en" => "English", "ru" => "Русский", "tr" => "Türkçe", _ => code.ToUpperInvariant()
    };

    public static readonly (string Code, string Name)[] Languages =
        { ("az", "Azərbaycan dili"), ("en", "English"), ("ru", "Русский"), ("tr", "Türkçe"), ("other", "Digər") };
}
