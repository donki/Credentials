namespace Credentials.Services;

/// <inheritdoc cref="ISettingsService"/>
public class SettingsService : ISettingsService
{
    // Solo en Debug: con SOC_SANDBOX definido, los ajustes van a otro contenedor y no se mezclan con
    // los del usuario de este PC (para probar sin tocar su boveda ni sus preferencias).
    private static readonly string? Shared =
#if DEBUG
        Environment.GetEnvironmentVariable("SOC_SANDBOX") is { Length: > 0 } ? "soc-sandbox" : null;
#else
        null;
#endif

    public string Language
    {
        get => Preferences.Get("language", LocalizationService.SystemLanguage, Shared);
        set => Preferences.Set("language", value ?? LocalizationService.SystemLanguage, Shared);
    }

    public StorageMode Storage
    {
        get => Enum.TryParse<StorageMode>(Preferences.Get("storage", "Local", Shared), out var m) ? m : StorageMode.Local;
        set => Preferences.Set("storage", value.ToString(), Shared);
    }

    public string AccountEmail
    {
        get => Preferences.Get("account_email", string.Empty, Shared);
        set => Preferences.Set("account_email", value ?? string.Empty, Shared);
    }

    public int AutoLockMinutes
    {
        get => Preferences.Get("autolock_minutes", 5, Shared);
        set => Preferences.Set("autolock_minutes", value, Shared);
    }

    public bool Biometrics
    {
        get => Preferences.Get("biometrics", false, Shared);
        set => Preferences.Set("biometrics", value, Shared);
    }

    public int ClipboardSeconds
    {
        get => Preferences.Get("clipboard_seconds", 30, Shared);
        set => Preferences.Set("clipboard_seconds", value, Shared);
    }

    public string SortMode
    {
        get => Preferences.Get("sort_mode", "title", Shared);
        set => Preferences.Set("sort_mode", value ?? "title", Shared);
    }

    public bool TrayOnMinimize
    {
        get => Preferences.Get("tray_on_minimize", true, Shared);
        set => Preferences.Set("tray_on_minimize", value, Shared);
    }

    public bool AskExtensions
    {
        get => Preferences.Get("ask_extensions", true, Shared);
        set => Preferences.Set("ask_extensions", value, Shared);
    }

    public DateTimeOffset? ExtensionSeen(string browser)
    {
        var s = Preferences.Get("extension_seen_" + browser, string.Empty, Shared);
        return DateTimeOffset.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
    }

    public void SetExtensionSeen(string browser) => Preferences.Set("extension_seen_" + browser, DateTimeOffset.UtcNow.ToString("o"), Shared);
}
