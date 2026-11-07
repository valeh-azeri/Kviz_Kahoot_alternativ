using Kviz.Business.Abstractions;
using Kviz.Business.Common;

namespace Kviz.Business.Services;

public interface IAvatarService
{
    /// <summary>Brauzerdə kiçildilmiş JPEG avatarı yoxlayıb saxlayır, ünvanını qaytarır.</summary>
    Task<ServiceResult<string>> SavePhotoAsync(Stream content, long length, CancellationToken ct = default);
    /// <summary>Verilən avatarlardan şəkil olanların fayllarını silir (hazır emoji avatarlara toxunmur).</summary>
    void DeletePhotos(IEnumerable<string?> avatars);
}

/// <summary>
/// Avatar şəkli kiçik olmalıdır: brauzer onu 160×160-a kiçildib JPEG edir (adətən 5-20 KB). Server bunu təkrar yoxlayır:
/// real JPEG faylı, ən çox 80 KB və ölçüsü 512 pikseldən böyük deyil. Beləliklə böyük fayl göndərib yer tutmaq olmur.
/// </summary>
public class AvatarService : IAvatarService
{
    public const int MaxBytes = 80 * 1024;
    public const int MaxSide = 512;
    private readonly IFileStorage _storage;
    public AvatarService(IFileStorage storage) => _storage = storage;

    public async Task<ServiceResult<string>> SavePhotoAsync(Stream content, long length, CancellationToken ct = default)
    {
        if (length <= 0) return ServiceResult<string>.Fail("Şəkil seçilməyib.");
        if (length > MaxBytes) return ServiceResult<string>.Fail("Avatar şəkli çox böyükdür.");

        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length is 0 or > MaxBytes) return ServiceResult<string>.Fail("Avatar şəkli çox böyükdür.");

        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        if (!JpegSize.TryRead(bytes, out var w, out var h) || w < 16 || h < 16 || w > MaxSide || h > MaxSide)
            return ServiceResult<string>.Fail("Avatar üçün düzgün şəkil göndərilmədi.");

        buffer.Position = 0;
        var url = await _storage.SaveAvatarAsync(buffer, ct);
        return ServiceResult<string>.Ok(url);
    }

    public void DeletePhotos(IEnumerable<string?> avatars)
    {
        foreach (var a in avatars.Where(Games.AvatarCatalog.IsPhoto).Distinct())
        {
            try { _storage.DeleteAvatar(a!); }
            catch { /* fayl artıq silinib və ya bağlıdır: vacib deyil */ }
        }
    }
}

/// <summary>JPEG faylının eni və hündürlüyünü başlıqdan oxuyur (şəkli tam açmadan).</summary>
internal static class JpegSize
{
    public static bool TryRead(ReadOnlySpan<byte> d, out int width, out int height)
    {
        width = height = 0;
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8 || d[2] != 0xFF) return false;
        var i = 2;
        while (i + 3 < d.Length)
        {
            if (d[i] != 0xFF) { i++; continue; }
            var marker = d[i + 1];
            if (marker == 0xFF) { i++; continue; }                          // doldurucu bayt
            if (marker is 0x01 or (>= 0xD0 and <= 0xD9)) { i += 2; continue; } // uzunluqsuz markerlər
            var len = (d[i + 2] << 8) | d[i + 3];
            if (len < 2) return false;
            var isSof = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isSof)
            {
                if (i + 8 >= d.Length) return false;
                height = (d[i + 5] << 8) | d[i + 6];
                width = (d[i + 7] << 8) | d[i + 8];
                return width > 0 && height > 0;
            }
            i += 2 + len;
        }
        return false;
    }
}
