namespace Kviz.Business.Common;

public class ValidationError
{
    public string Path { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ValidationError() { }
    public ValidationError(string path, string message) { Path = path; Message = message; }
}

public class ServiceResult
{
    public List<ValidationError> Errors { get; set; } = new();
    public bool Success => Errors.Count == 0;
    public string FirstError => Errors.FirstOrDefault()?.Message ?? string.Empty;

    public static ServiceResult Ok() => new();
    public static ServiceResult Fail(string message, string path = "") =>
        new() { Errors = { new ValidationError(path, message) } };
}

public class ServiceResult<T> : ServiceResult
{
    public T? Value { get; set; }
    public static ServiceResult<T> Ok(T value) => new() { Value = value };
    public new static ServiceResult<T> Fail(string message, string path = "") =>
        new() { Errors = { new ValidationError(path, message) } };
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Qoşulma ünvanı məlumatı (LAN və ya internet).</summary>
public class JoinInfoDto
{
    public string BaseUrl { get; set; } = string.Empty;
    public string JoinUrl { get; set; } = string.Empty;
    /// <summary>Qısa, insanın yazacağı ünvan (http:// olmadan).</summary>
    public string ShortUrl { get; set; } = string.Empty;
    public List<string> Alternatives { get; set; } = new();
    public bool IsLocalNetwork { get; set; }
}
