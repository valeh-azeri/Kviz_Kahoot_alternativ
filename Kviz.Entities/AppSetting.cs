namespace Kviz.Entities;

/// <summary>Administratorun idarə etdiyi sayt ayarları (açar/dəyər).</summary>
public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public static class AppRoles
{
    public const string Admin = "Admin";
}

public static class AppSettingKeys
{
    public const string RegistrationOpen = "RegistrationOpen";
    /// <summary>Hamıya açıq kvizlər kitabxanasına qeydiyyatsız (daxil olmadan) baxmaq olar.</summary>
    public const string GuestLibrary = "GuestLibrary";
}
