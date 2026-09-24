using System.Security.Cryptography;
using System.Text.Json;
using Credentials.Models;

namespace Credentials.Services;

/// <summary>
/// La boveda en uso: el fichero local, la clave en memoria mientras esta desbloqueada, el guardado
/// y la sincronizacion con Google Drive u OneDrive. Es la unica pieza que toca el fichero.
/// </summary>
/// <remarks>
/// <para><b>Fichero local siempre.</b> Aunque la boveda viva en la nube, la copia de trabajo es
/// <c>vault.soccred</c> en los datos de la aplicacion: se lee y se escribe ahi, y la nube se baja al
/// abrir y se sube tras cada cambio. Sin red se sigue trabajando; al volver, se mezcla por entrada
/// (gana la mas nueva) y se sube.</para>
/// <para><b>Clave envuelta.</b> Para desbloquear con Windows Hello o la huella se guarda la clave
/// derivada (no la contraseña) en <c>SecureStorage</c>, que en Windows es DPAPI y en Android el
/// Keystore; solo se lee tras pasar la biometria. Nada de eso sale del dispositivo.</para>
/// <para><b>Misma contraseña en todos los dispositivos.</b> La copia de la nube se descifra con la
/// misma clave; si alguien cambia la contraseña maestra en un dispositivo, los demas la piden al abrir.</para>
/// </remarks>
public sealed class VaultStore
{
    private const string KeySlot = "vault.key";
    private const string TokensSlot = "cloud.tokens";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly ISettingsService _settings;
    private readonly HttpClient _http = new();
    private readonly IOAuthBrowser _browser;
    private byte[]? _key;
    private VaultCrypto.Header? _header;
    private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;
    private Timer? _idleTimer;

    /// <summary>
    /// Cuanto lleva el usuario sin tocar el dispositivo (teclado, raton, pantalla), si la plataforma
    /// lo sabe (Windows: GetLastInputInfo). Con esto la inactividad es la real, no solo la de esta
    /// aplicacion: mientras se usa el PC la boveda sigue abierta.
    /// </summary>
    public static Func<TimeSpan?>? SystemIdle { get; set; }

    public VaultStore(ISettingsService settings, IOAuthBrowser browser)
    {
        _settings = settings;
        _browser = browser;
    }

    public static string FilePath => Path.Combine(DataDirectory, "vault.soccred");

    /// <summary>
    /// Debug con SOC_SANDBOX: instancia de pruebas que convive con la real sin tocar nada suyo (ni
    /// la instancia unica, ni el puente de los navegadores, ni el autocompletar de escritorio).
    /// </summary>
    public static bool Sandbox =>
#if DEBUG
        Environment.GetEnvironmentVariable("SOC_SANDBOX") is { Length: > 0 };
#else
        false;
#endif

    /// <summary>La carpeta de datos; en Debug, SOC_SANDBOX la cambia para probar sin tocar la boveda real.</summary>
    public static string DataDirectory
    {
        get
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("SOC_SANDBOX") is { Length: > 0 } sandbox)
            {
                Directory.CreateDirectory(sandbox);
                return sandbox;
            }
#endif
            return FileSystem.AppDataDirectory;
        }
    }

    public VaultData? Data { get; private set; }
    public bool Exists => File.Exists(FilePath);
    public bool IsUnlocked => _key is not null && Data is not null;

    /// <summary>Algo ha cambiado en la boveda (guardado, mezcla, bloqueo): las paginas releen.</summary>
    public event Action? Changed;
    /// <summary>Se ha bloqueado (a mano o por inactividad).</summary>
    public event Action? Locked;

    /// <summary>Se acaba de abrir (contraseña, biometria o al crearla).</summary>
    public event Action? Unlocked;
    /// <summary>Estado de la nube en una linea (para la barra de estado o un aviso).</summary>
    public event Action<string>? Status;

    // ------------------------------------------------------------------ crear, abrir, cerrar

    public async Task CreateAsync(string masterPassword)
    {
        var data = new VaultData();
        var header = VaultCrypto.NewHeader(data.VaultId);
        var key = await Task.Run(() => VaultCrypto.DeriveKey(masterPassword, header));
        _key = key;
        _header = header;
        Data = data;
        await SaveAsync(upload: false);
        Touch();
        Unlocked?.Invoke();
    }

    /// <summary>Abre con la contraseña maestra. Lanza <see cref="CryptographicException"/> si no es.</summary>
    public async Task UnlockAsync(string masterPassword)
    {
        var stored = await File.ReadAllTextAsync(FilePath);
        var header = VaultCrypto.ReadHeader(stored);
        var key = await Task.Run(() => VaultCrypto.DeriveKey(masterPassword, header));
        var json = VaultCrypto.Decrypt(stored, key);
        Data = JsonSerializer.Deserialize<VaultData>(json, Json) ?? new VaultData();
        _key = key;
        _header = header;
        Touch();
        Changed?.Invoke();
        Unlocked?.Invoke();
    }

    /// <summary>Abre con la clave guardada en la boveda del sistema (tras la biometria). False si no hay clave o no vale.</summary>
    public async Task<bool> UnlockWithStoredKeyAsync()
    {
        try
        {
            var stored = await SecureStorage.Default.GetAsync(KeySlot);
            if (string.IsNullOrEmpty(stored) || !Exists)
                return false;
            var key = Convert.FromBase64String(stored);
            var text = await File.ReadAllTextAsync(FilePath);
            var header = VaultCrypto.ReadHeader(text);
            var json = VaultCrypto.Decrypt(text, key);
            Data = JsonSerializer.Deserialize<VaultData>(json, Json) ?? new VaultData();
            _key = key;
            _header = header;
            Touch();
            Changed?.Invoke();
            Unlocked?.Invoke();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Con «Confiar en este dispositivo», abre la boveda con la clave guardada en el sistema, sin
    /// preguntar nada. False si no esta activado, no hay clave o no vale.
    /// </summary>
    public Task<bool> TryTrustedUnlockAsync()
    {
        if (IsUnlocked)
            return Task.FromResult(true);
        // Sin mirar HasStoredKey: si no hay clave, UnlockWithStoredKeyAsync ya devuelve false.
        if (!_settings.TrustDevice || !Exists)
            return Task.FromResult(false);
        // Al arrancar lo piden a la vez la aplicacion y la puerta: un solo intento para los dos.
        return _trustedUnlock ??= Run();

        async Task<bool> Run()
        {
            try { return await UnlockWithStoredKeyAsync(); }
            finally { _trustedUnlock = null; }
        }
    }

    private Task<bool>? _trustedUnlock;

    public bool HasStoredKey
    {
        get
        {
            // Fuera del hilo de la interfaz: esperarlo ahi mismo la dejaba bloqueada para siempre
            // (la lectura necesita ese hilo para terminar).
            try { return Task.Run(() => SecureStorage.Default.GetAsync(KeySlot)).GetAwaiter().GetResult() is { Length: > 0 }; }
            catch (Exception) { return false; }
        }
    }

    /// <summary>Guarda (o quita) la clave derivada en la boveda del sistema para el desbloqueo biometrico.</summary>
    public async Task RememberKeyAsync(bool remember)
    {
        if (remember && _key is not null)
            await SecureStorage.Default.SetAsync(KeySlot, Convert.ToBase64String(_key));
        else
            SecureStorage.Default.Remove(KeySlot);
    }

    public void Lock()
    {
        if (_key is null && Data is null)
            return;
        if (_key is not null)
            CryptographicOperations.ZeroMemory(_key);
        _key = null;
        Data = null;
        StopIdleTimer();
        Locked?.Invoke();
    }

    /// <summary>Cualquier uso reinicia la cuenta atras del bloqueo automatico.</summary>
    public void Touch()
    {
        _lastActivity = DateTimeOffset.UtcNow;
        if (IsUnlocked && _idleTimer is null)
            StartIdleTimer();
    }

    /// <summary>Cuanto lleva el usuario sin hacer nada: lo que sepa el sistema o, si no, lo que lleva sin usar esta aplicacion.</summary>
    public TimeSpan IdleTime
    {
        get
        {
            var app = DateTimeOffset.UtcNow - _lastActivity;
            try
            {
                if (SystemIdle?.Invoke() is { } system)
                    return system < app ? system : app;
            }
            catch (Exception) { }
            return app;
        }
    }

    /// <summary>Si ha pasado el tiempo de inactividad configurado, bloquea.</summary>
    public bool LockIfIdle()
    {
        var minutes = _settings.AutoLockMinutes;
        if (_settings.TrustDevice || !IsUnlocked || minutes <= 0 || IdleTime < TimeSpan.FromMinutes(minutes))
            return false;
        Lock();
        return true;
    }

    // La cuenta atras corre mientras la boveda esta abierta, este la pagina que este a la vista (o la
    // ventana en la bandeja): cada 15 s se mira si toca bloquear.
    private void StartIdleTimer()
    {
        StopIdleTimer();
        _idleTimer = new Timer(_ => MainThread.BeginInvokeOnMainThread(() => { try { LockIfIdle(); } catch (Exception) { } }), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    private void StopIdleTimer()
    {
        _idleTimer?.Dispose();
        _idleTimer = null;
    }

    /// <summary>
    /// Borra una entrada: borrado logico (queda una lapida sin secretos) para que la baja llegue a
    /// los demas dispositivos al mezclar; VaultData.Purge la quita del todo pasado un tiempo.
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        EnsureUnlocked();
        var tomb = Data!.Entries.FirstOrDefault(x => x.Id == id);
        if (tomb is null)
            return;
        tomb.Deleted = true;
        tomb.Password = string.Empty;
        tomb.Totp = string.Empty;
        tomb.Notes = string.Empty;
        tomb.Fields.Clear();
        tomb.History.Clear();
        tomb.ModifiedAt = DateTimeOffset.UtcNow;
        await SaveAsync();
    }

    public async Task ChangeMasterPasswordAsync(string newPassword)
    {
        EnsureUnlocked();
        var header = VaultCrypto.NewHeader(Data!.VaultId);
        var key = await Task.Run(() => VaultCrypto.DeriveKey(newPassword, header));
        var remembered = HasStoredKey;
        _key = key;
        _header = header;
        if (remembered)
            await RememberKeyAsync(true);
        await SaveAsync(upload: true);
    }

    // ------------------------------------------------------------------ guardar

    /// <summary>Cifra y escribe el fichero local (atomico); si la boveda esta en la nube, la sube detras.</summary>
    public async Task SaveAsync(bool upload = true)
    {
        EnsureUnlocked();
        Data!.ModifiedAt = DateTimeOffset.UtcNow;
        Data.Purge();
        var text = VaultCrypto.Encrypt(JsonSerializer.Serialize(Data, Json), _key!, _header!);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        await File.WriteAllTextAsync(temp, text);
        if (File.Exists(FilePath))
            File.Copy(FilePath, FilePath + ".bak", overwrite: true);
        File.Move(temp, FilePath, overwrite: true);
        Touch();
        Changed?.Invoke();
        if (upload && _settings.Storage != StorageMode.Local)
            _ = UploadSafelyAsync(text);
    }

    private async Task UploadSafelyAsync(string text)
    {
        try
        {
            var drive = await DriveAsync();
            await drive.UploadAsync(text);
            Status?.Invoke($"cloud:ok:{drive.Mode}");
        }
        catch (Exception ex)
        {
            Status?.Invoke("cloud:error:" + ex.Message);
        }
    }

    // ------------------------------------------------------------------ nube

    public OAuthProvider Provider(StorageMode mode) => mode switch
    {
        StorageMode.GoogleDrive => new OAuthProvider("Google", OAuthSecrets.GoogleClientId, OAuthSecrets.GoogleClientSecret,
            "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", GoogleDrive.Scopes, "&access_type=offline&prompt=consent"),
        StorageMode.OneDrive => new OAuthProvider("Microsoft", OAuthSecrets.MicrosoftClientId, string.Empty,
            "https://login.microsoftonline.com/common/oauth2/v2.0/authorize", "https://login.microsoftonline.com/common/oauth2/v2.0/token", OneDrive.Scopes, "&prompt=select_account"),
        _ => throw new InvalidOperationException("Sin proveedor."),
    };

    public bool IsConfigured(StorageMode mode) => mode != StorageMode.Local && Provider(mode).ClientId.Length > 0;

    /// <summary>Entra con la cuenta y deja los tokens en la boveda del sistema. Devuelve el correo.</summary>
    public async Task<string> SignInAsync(StorageMode mode, CancellationToken cancellationToken = default)
    {
        var client = new OAuthClient(_http, _browser, Provider(mode));
        var tokens = await client.SignInAsync(cancellationToken);
        var needed = mode == StorageMode.GoogleDrive ? "https://www.googleapis.com/auth/drive.appdata" : "Files.ReadWrite.AppFolder";
        if (tokens.Scope.Length > 0 && !tokens.Has(needed))
            throw new InvalidOperationException("scope");
        await SecureStorage.Default.SetAsync(TokensSlot, JsonSerializer.Serialize(tokens, Json));
        _settings.Storage = mode;
        _settings.AccountEmail = OAuthClient.EmailOf(tokens);
        return _settings.AccountEmail;
    }

    public void SignOut()
    {
        SecureStorage.Default.Remove(TokensSlot);
        _settings.Storage = StorageMode.Local;
        _settings.AccountEmail = string.Empty;
    }

    private async Task<ICloudDrive> DriveAsync()
    {
        var mode = _settings.Storage;
        var stored = await SecureStorage.Default.GetAsync(TokensSlot) ?? throw new InvalidOperationException("Sin sesion en la nube.");
        var tokens = JsonSerializer.Deserialize<OAuthTokens>(stored, Json) ?? throw new InvalidOperationException("Sin sesion en la nube.");
        var client = new OAuthClient(_http, _browser, Provider(mode));
        Func<CancellationToken, Task<string>> token = async ct =>
        {
            var fresh = await client.RefreshIfNeededAsync(tokens, ct);
            if (!ReferenceEquals(fresh, tokens))
            {
                tokens = fresh;
                await SecureStorage.Default.SetAsync(TokensSlot, JsonSerializer.Serialize(tokens, Json));
            }
            return tokens.AccessToken;
        };
        return mode == StorageMode.GoogleDrive ? new GoogleDrive(_http, token) : new OneDrive(_http, token);
    }

    /// <summary>
    /// Baja la copia de la nube, la mezcla por entrada con la local y sube el resultado si hacia
    /// falta. Con la boveda desbloqueada. Devuelve cuantas entradas han cambiado aqui.
    /// </summary>
    public async Task<int> SyncAsync(CancellationToken cancellationToken = default)
    {
        EnsureUnlocked();
        if (_settings.Storage == StorageMode.Local)
            return 0;
        var drive = await DriveAsync();
        var remote = await drive.DownloadAsync(cancellationToken);
        if (remote is null)
        {
            await UploadSafelyAsync(VaultCrypto.Encrypt(JsonSerializer.Serialize(Data, Json), _key!, _header!));
            return 0;
        }
        VaultData theirs;
        try
        {
            var header = VaultCrypto.ReadHeader(remote.Value.Content);
            // La nube puede llevar otra sal (otra instalacion la creo): con la misma contraseña no
            // hay problema, pero la clave hay que derivarla con SU cabecera. Sin la contraseña a
            // mano se intenta con la clave actual; si no vale, se pide.
            var json = header.Salt == _header!.Salt ? VaultCrypto.Decrypt(remote.Value.Content, _key!) : throw new VaultPasswordNeededException(remote.Value.Content);
            theirs = JsonSerializer.Deserialize<VaultData>(json, Json) ?? new VaultData();
        }
        catch (CryptographicException)
        {
            throw new VaultPasswordNeededException(remote.Value.Content);
        }
        var changed = Data!.Merge(theirs);
        // Lo que la nube no tenia (nuestro y mas nuevo) tambien hay que subirlo.
        var localNewer = Data.Entries.Any(e => theirs.Entries.FirstOrDefault(t => t.Id == e.Id) is not { } t || t.ModifiedAt < e.ModifiedAt);
        await SaveAsync(upload: localNewer);
        Status?.Invoke($"cloud:ok:{drive.Mode}");
        return changed;
    }

    /// <summary>Segunda parte de <see cref="SyncAsync"/> cuando la nube esta cifrada con otra sal: con la contraseña se rehace la clave.</summary>
    public async Task<int> MergeRemoteWithPasswordAsync(string remoteContent, string masterPassword)
    {
        EnsureUnlocked();
        var header = VaultCrypto.ReadHeader(remoteContent);
        var key = await Task.Run(() => VaultCrypto.DeriveKey(masterPassword, header));
        var json = VaultCrypto.Decrypt(remoteContent, key);
        var theirs = JsonSerializer.Deserialize<VaultData>(json, Json) ?? new VaultData();
        var changed = Data!.Merge(theirs);
        await SaveAsync(upload: true);
        return changed;
    }

    /// <summary>Copia cifrada tal cual (para «exportar boveda» o guardarla donde sea).</summary>
    public string ExportEncrypted() => File.ReadAllText(FilePath);

    /// <summary>JSON en claro de todo (para «exportar a JSON»); el que lo pide sabe lo que hace.</summary>
    public string ExportPlainJson()
    {
        EnsureUnlocked();
        return JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });
    }

    private void EnsureUnlocked()
    {
        if (!IsUnlocked)
            throw new InvalidOperationException("La boveda esta bloqueada.");
    }
}

/// <summary>La copia de la nube va con otra sal: hace falta la contraseña maestra para leerla.</summary>
public sealed class VaultPasswordNeededException(string remoteContent) : Exception("La copia de la nube pide la contraseña maestra.")
{
    public string RemoteContent { get; } = remoteContent;
}
