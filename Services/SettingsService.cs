namespace Credentials.Services;

/// <inheritdoc cref="ISettingsService"/>
public class SettingsService : ISettingsService
{
    public string Language
    {
        get => Preferences.Get("language", LocalizationService.SystemLanguage);
        set => Preferences.Set("language", value ?? LocalizationService.SystemLanguage);
    }

    public StorageMode Storage
    {
        get => Enum.TryParse<StorageMode>(Preferences.Get("storage", "Local"), out var m) ? m : StorageMode.Local;
        set => Preferences.Set("storage", value.ToString());
    }

    public string AccountEmail
    {
        get => Preferences.Get("account_email", string.Empty);
        set => Preferences.Set("account_email", value ?? string.Empty);
    }

    public int AutoLockMinutes
    {
        get => Preferences.Get("autolock_minutes", 5);
        set => Preferences.Set("autolock_minutes", value);
    }

    public bool Biometrics
    {
        get => Preferences.Get("biometrics", false);
        set => Preferences.Set("biometrics", value);
    }

    public int ClipboardSeconds
    {
        get => Preferences.Get("clipboard_seconds", 30);
        set => Preferences.Set("clipboard_seconds", value);
    }

    public string SortMode
    {
        get => Preferences.Get("sort_mode", "title");
        set => Preferences.Set("sort_mode", value ?? "title");
    }
}
