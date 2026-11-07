using Kviz.Business.Abstractions;
using Kviz.Business.Common;

namespace Kviz.Business.Services;

public interface IImageUploadService
{
    Task<ServiceResult<string>> SaveAsync(Stream content, string fileName, long length, CancellationToken ct = default);
}

/// <summary>Şəkil yükləməsini yoxlayır (ölçü, uzantı və faylın real formatı) və saxlayır.</summary>
public class ImageUploadService : IImageUploadService
{
    public const long MaxBytes = 5 * 1024 * 1024;
    private readonly IFileStorage _storage;
    public ImageUploadService(IFileStorage storage) => _storage = storage;

    public async Task<ServiceResult<string>> SaveAsync(Stream content, string fileName, long length, CancellationToken ct = default)
    {
        if (length <= 0) return ServiceResult<string>.Fail("Fayl seçilməyib.");
        if (length > MaxBytes) return ServiceResult<string>.Fail("Şəkil 5 MB-dan böyük ola bilməz.");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".gif" or ".webp"))
            return ServiceResult<string>.Fail("Yalnız JPG, PNG, GIF və WEBP şəkilləri yüklənə bilər.");

        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxBytes) return ServiceResult<string>.Fail("Şəkil 5 MB-dan böyük ola bilməz.");
        var head = buffer.GetBuffer().AsSpan(0, (int)Math.Min(12, buffer.Length));

        var real = Detect(head);
        if (real == null) return ServiceResult<string>.Fail("Fayl şəkil kimi tanınmadı.");

        buffer.Position = 0;
        var url = await _storage.SaveImageAsync(buffer, real, ct);
        return ServiceResult<string>.Ok(url);
    }

    private static string? Detect(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ".jpg";
        if (h.Length >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return ".png";
        if (h.Length >= 6 && h[0] == 'G' && h[1] == 'I' && h[2] == 'F' && h[3] == '8') return ".gif";
        if (h.Length >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P') return ".webp";
        return null;
    }
}
