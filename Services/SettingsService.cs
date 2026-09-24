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

    // En Windows la boveda va con la sesion de escritorio: se cierra al bloquear la sesion (Win+L,
    // o el bloqueo automatico del sistema) y la contraseña se pide una vez al volver; por eso el
    // bloqueo por inactividad propio viene apagado (el usuario puede encenderlo en Ajustes).
    // En Android se cierra a los 15 minutos (y al apagar la pantalla).
    public int AutoLockMinutes
    {
        get => Preferences.Get("autolock_minutes", OperatingSystem.IsWindows() ? 0 : 15, Shared);
        set => Preferences.Set("autolock_minutes", value, Shared);
    }

    public bool TrustDevice
    {
        get => Preferences.Get("trust_device", false, Shared);
        set => Preferences.Set("trust_device", value, Shared);
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

    public bool AskAutofill
    {
        get => Preferences.Get("ask_autofill", true, Shared);
        set => Preferences.Set("ask_autofill", value, Shared);
    }

    public bool DesktopAutofill
    {
        get => Preferences.Get("desktop_autofill", true, Shared);
        set => Preferences.Set("desktop_autofill", value, Shared);
    }

    public bool TutorialDone
    {
        get => Preferences.Get("tutorial_done", false, Shared);
        set => Preferences.Set("tutorial_done", value, Shared);
    }

    public DateTimeOffset? ExtensionSeen(string browser)
    {
        var s = Preferences.Get("extension_seen_" + browser, string.Empty, Shared);
        return DateTimeOffset.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
    }

    public void SetExtensionSeen(string browser) => Preferences.Set("extension_seen_" + browser, DateTimeOffset.UtcNow.ToString("o"), Shared);
}
