using System.Text.RegularExpressions;

namespace Kviz.Business.Games;

/// <summary>
/// Hazır avatarlar (emoji, internetsiz işləyir) və yüklənmiş şəkil avatarlarının formatı.
/// Avatar sətri: hazır üçün "e:🐱", şəkil üçün "/uploads/avatars/{32 hex}.jpg".
/// </summary>
public static class AvatarCatalog
{
    public const string PhotoFolder = "/uploads/avatars/";

    public static readonly IReadOnlyList<AvatarGroup> Groups = new List<AvatarGroup>
    {
        new("Heyvanlar", new[] { "🐱", "🐶", "🦊", "🐼", "🦁", "🐯", "🐸", "🐵", "🐧", "🐨", "🐰", "🐻", "🦄", "🐙", "🦉", "🐢" }),
        new("Personajlar", new[] { "🤖", "👽", "👻", "🦸", "🧙", "🥷", "🧛", "🧜", "🤠", "😎", "🥳", "🤩" }),
        new("Əyləncə", new[] { "🚀", "⚽", "🎮", "🎸", "🏀", "🎨", "🍕", "🍔", "🍩", "🍉", "🌈", "⚡" })
    };

    private static readonly string[] All = Groups.SelectMany(g => g.Items).ToArray();
    private static readonly HashSet<string> AllSet = new(All, StringComparer.Ordinal);
    private static readonly Regex PhotoPattern = new(@"^/uploads/avatars/[0-9a-f]{32}\.jpg$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsPreset(string? avatar) => avatar is { Length: > 2 and <= 12 } && avatar.StartsWith("e:", StringComparison.Ordinal) && AllSet.Contains(avatar[2..]);

    public static bool IsPhoto(string? avatar) => avatar != null && PhotoPattern.IsMatch(avatar);

    /// <summary>Təsadüfi hazır avatar (oyunçu seçməyibsə).</summary>
    public static string Random() => "e:" + All[System.Random.Shared.Next(All.Length)];
}

public record AvatarGroup(string Title, string[] Items);
