using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Credentials.Services;
using Microsoft.Win32;

namespace Credentials.Platforms.Windows;

/// <summary>
/// El lado de la aplicacion para las extensiones de navegador. CredentialsHost.exe (mensajeria
/// nativa) le pasa por la tuberia sOCCredentials una linea JSON por peticion y espera otra linea de
/// respuesta con el mismo «id». La tuberia solo la puede abrir el mismo usuario de Windows.
/// </summary>
public sealed class ExtensionServer
{
    public const string PipeName = "sOCCredentials";

    private readonly VaultStore _store;
    private readonly ISettingsService _settings;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Un navegador acaba de saludar por primera vez (para avisar en pantalla).</summary>
    public event Action<string>? BrowserConnected;

    public ExtensionServer(VaultStore store, ISettingsService settings)
    {
        _store = store;
        _settings = settings;
    }

    public void Start() => _ = Task.Run(AcceptLoopAsync);

    public void Stop() => _cts.Cancel();

    private async Task AcceptLoopAsync()
    {
        var security = new PipeSecurity();
        var me = WindowsIdentity.GetCurrent().User!;
        security.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception)
            {
                // Otra instancia tiene la tuberia (o no se pudo crear): se espera y se reintenta.
                try { await Task.Delay(2000, _cts.Token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                continue;
            }
            _ = Task.Run(() => ServeAsync(server));
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        {
            var reader = new StreamReader(pipe, new UTF8Encoding(false));
            var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            try
            {
                while (pipe.IsConnected && await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false) is { } line)
                {
                    var response = await HandleAsync(line).ConfigureAwait(false);
                    await writer.WriteLineAsync(response).ConfigureAwait(false);
                }
            }
            catch (Exception) { /* el host se fue: se cierra esta conexion y ya */ }
        }
    }

    private async Task<string> HandleAsync(string line)
    {
        JsonNode? request;
        try { request = JsonNode.Parse(line); }
        catch (Exception) { return "{\"error\":\"badjson\"}"; }
        var id = request?["id"]?.DeepClone();
        var type = request?["type"]?.GetValue<string>() ?? string.Empty;
        var reply = new JsonObject { ["id"] = id };
        try
        {
            // Todo lo que toca la boveda o la interfaz va al hilo principal.
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                switch (type)
                {
                    case "hello":
                    {
                        var browser = request?["browser"]?.GetValue<string>() ?? "chrome";
                        var first = _settings.ExtensionSeen(browser) is null;
                        _settings.SetExtensionSeen(browser);
                        if (first) BrowserConnected?.Invoke(browser);
                        reply["ok"] = true;
                        reply["locked"] = !_store.IsUnlocked;
                        break;
                    }
                    case "show":
                        // El usuario ha pedido la aplicacion (popup, menu, desplegable): si esta
                        // bloqueada, aqui si se pide la contraseña.
                        RequestUnlock();
                        reply["ok"] = true;
                        break;
                    case "list":
                    case "search":
                    {
                        // Peticiones pasivas (la insignia al cargar cada pestaña, el desplegable al
                        // enfocar un campo): con la boveda cerrada se contesta «locked» y punto, sin
                        // sacar la ventana. La contraseña solo se pide cuando el usuario actua.
                        if (!_store.IsUnlocked)
                        {
                            reply["locked"] = true;
                            break;
                        }
                        _store.Touch();
                        var entries = type == "list"
                            ? AutofillLogic.Match(_store.Data!.Entries, request?["host"]?.GetValue<string>(), null)
                            : AutofillLogic.Search(_store.Data!.Entries, request?["query"]?.GetValue<string>() ?? string.Empty);
                        var array = new JsonArray();
                        foreach (var e in entries.Take(type == "list" ? 20 : 50))
                        {
                            var item = new JsonObject
                            {
                                ["id"] = e.Id.ToString(),
                                ["title"] = e.Title,
                                ["username"] = e.Username,
                                ["password"] = e.Password,
                                ["url"] = e.Url,
                            };
                            if (e.HasTotp && Totp.Parse(e.Totp) is { } totp)
                            {
                                var (code, left) = totp.Now();
                                item["totp"] = code;
                                item["totpLeft"] = left;
                            }
                            array.Add(item);
                        }
                        reply["entries"] = array;
                        break;
                    }
                    case "save":
                    {
                        if (!_store.IsUnlocked)
                        {
                            RequestUnlock();
                            // Se espera a que el usuario desbloquee (hasta dos minutos) para guardar.
                            var deadline = DateTime.UtcNow.AddMinutes(2);
                            while (!_store.IsUnlocked && DateTime.UtcNow < deadline)
                                await Task.Delay(500);
                            if (!_store.IsUnlocked)
                            {
                                reply["locked"] = true;
                                break;
                            }
                        }
                        var host = request?["host"]?.GetValue<string>();
                        var username = request?["username"]?.GetValue<string>() ?? string.Empty;
                        var password = request?["password"]?.GetValue<string>() ?? string.Empty;
                        var saved = await AutofillLogic.UpsertAsync(_store, host, null, null, username, password);
                        reply["ok"] = true;
                        reply["changed"] = saved;
                        break;
                    }
                    default:
                        reply["error"] = "unknown";
                        break;
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            reply["error"] = "app";
            reply["detail"] = ex.Message;
        }
        return reply.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>El usuario necesita la boveda: se trae la ventana y, si esta cerrada, la pagina de desbloqueo.</summary>
    private void RequestUnlock()
    {
        WindowHelper.BringToFront();
        if (!_store.IsUnlocked && Application.Current?.Windows.FirstOrDefault()?.Page is { } page)
            _ = Pages.Gate.EnsureUnlockedAsync(page);
    }
}

/// <summary>Traer la ventana principal al frente (desde la bandeja, minimizada o detras de otras).</summary>
public static class WindowHelper
{
    public static void BringToFront()
    {
        try
        {
            if (App.Tray is { } tray)
            {
                tray.Show();
                return;
            }
            if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
                ShowWindow(hwnd, 9 /* SW_RESTORE */);
                SetForegroundWindow(hwnd);
            }
        }
        catch (Exception) { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
}

/// <summary>Un navegador con soporte: donde esta y donde registra sus hosts de mensajeria nativa.</summary>
public sealed record Browser(string Key, string Name, string Exe, string HostRegistryKey, bool IsFirefox, string ExtensionsUrl)
{
    public string? Path { get; init; }
    public bool Installed => Path is not null;
}

/// <summary>
/// Instalacion de la extension: deja la carpeta desempaquetada en %LOCALAPPDATA%\sOCCredentials\extension,
/// registra CredentialsHost.exe para cada navegador (HKCU, sin permisos de administrador) y guia al
/// usuario para cargarla, que es lo unico que el navegador no deja automatizar sin publicarla.
/// </summary>
public static class ExtensionInstaller
{
    public const string HostName = "com.socratic.credentials";
    private const string ChromiumId = "hbimfdiggibkbjnmkagdcnddpghhckho";   // sale de la clave «key» del manifiesto
    private const string FirefoxId = "credentials@socratic.app";
    private const string AppKey = @"Software\sOCratic\Credentials";

    public static string Root => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCCredentials");
    public static string ExtensionDir(bool firefox) => System.IO.Path.Combine(Root, "extension", firefox ? "firefox" : "chromium");
    private static string HostDir => System.IO.Path.Combine(Root, "host");
    private static string SourceDir => System.IO.Path.Combine(AppContext.BaseDirectory, "Extension");
    private static string HostExe => System.IO.Path.Combine(AppContext.BaseDirectory, "CredentialsHost.exe");

    public static bool Available => Directory.Exists(SourceDir) && File.Exists(HostExe);

    public static readonly Browser[] Known =
    [
        new("edge", "Microsoft Edge", "msedge.exe", @"Software\Microsoft\Edge\NativeMessagingHosts", false, "edge://extensions/"),
        new("chrome", "Google Chrome", "chrome.exe", @"Software\Google\Chrome\NativeMessagingHosts", false, "chrome://extensions/"),
        new("firefox", "Mozilla Firefox", "firefox.exe", @"Software\Mozilla\NativeMessagingHosts", true, "about:debugging#/runtime/this-firefox"),
    ];

    /// <summary>Los navegadores que hay en este PC (por sus «App Paths»).</summary>
    public static List<Browser> Detected() => Known.Select(b => b with { Path = FindExe(b.Exe) }).Where(b => b.Installed).ToList();

    private static string? FindExe(string exe)
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exe);
                if (key?.GetValue(null) is string path && File.Exists(path))
                    return path;
            }
            catch (Exception) { }
        }
        return null;
    }

    public static bool IsRegistered(Browser b)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(b.HostRegistryKey + "\\" + HostName);
            return key?.GetValue(null) is string path && File.Exists(path);
        }
        catch (Exception) { return false; }
    }

    /// <summary>Copia la extension (comun + manifiesto del navegador) y escribe el manifiesto del host y su clave.</summary>
    public static void Install(Browser b)
    {
        Extract(b.IsFirefox);
        RegisterHost(b);
        RegisterAppPath();
    }

    /// <summary>
    /// Al arrancar: si algun navegador ya esta registrado, se refrescan la extension y el manifiesto
    /// del host, porque la carpeta de la aplicacion cambia con cada version.
    /// </summary>
    public static void RefreshIfRegistered()
    {
        try
        {
            RegisterAppPath();
            foreach (var b in Known)
                if (IsRegistered(b))
                    Install(b);
        }
        catch (Exception) { }
    }

    private static void Extract(bool firefox)
    {
        var target = ExtensionDir(firefox);
        Directory.CreateDirectory(target);
        CopyTree(System.IO.Path.Combine(SourceDir, "common"), target);
        File.Copy(System.IO.Path.Combine(SourceDir, firefox ? "firefox" : "chromium", "manifest.json"), System.IO.Path.Combine(target, "manifest.json"), overwrite: true);
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(from))
            CopyTree(dir, System.IO.Path.Combine(to, System.IO.Path.GetFileName(dir)));
    }

    private static void RegisterHost(Browser b)
    {
        Directory.CreateDirectory(HostDir);
        var manifestPath = System.IO.Path.Combine(HostDir, HostName + (b.IsFirefox ? ".firefox" : "") + ".json");
        var manifest = new JsonObject
        {
            ["name"] = HostName,
            ["description"] = "sOC Credentials",
            ["path"] = HostExe,
            ["type"] = "stdio",
        };
        if (b.IsFirefox)
            manifest["allowed_extensions"] = new JsonArray(FirefoxId);
        else
            manifest["allowed_origins"] = new JsonArray($"chrome-extension://{ChromiumId}/");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        using var key = Registry.CurrentUser.CreateSubKey(b.HostRegistryKey + "\\" + HostName, writable: true);
        key?.SetValue(null, manifestPath);
    }

    /// <summary>Donde arrancar la aplicacion si el host la encuentra cerrada: el lanzador si lo hay, si no el exe.</summary>
    private static void RegisterAppPath()
    {
        var launcher = Environment.GetEnvironmentVariable("SOC_LAUNCHER");
        var path = !string.IsNullOrEmpty(launcher) && File.Exists(launcher) ? launcher : Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
            return;
        using var key = Registry.CurrentUser.CreateSubKey(AppKey, writable: true);
        key?.SetValue("AppPath", path);
    }

    public static void OpenExtensionsPage(Browser b)
    {
        if (b.Path is null)
            return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(b.Path, b.ExtensionsUrl) { UseShellExecute = true }); }
        catch (Exception) { }
    }
}

/// <summary>La parte con pantalla: la oferta tras desbloquear y la guia de instalacion por navegador.</summary>
public static class ExtensionSetup
{
    private static bool _offeredThisSession;

    /// <summary>Tras desbloquear: si hay navegadores sin la extension, se ofrece instalarla (una vez por sesion).</summary>
    public static async Task OfferAfterUnlockAsync(Page page, ISettingsService settings, ILocalizationService l)
    {
        if (_offeredThisSession || !settings.AskExtensions || !ExtensionInstaller.Available)
            return;
        _offeredThisSession = true;
        var missing = ExtensionInstaller.Detected().Where(b => settings.ExtensionSeen(b.Key) is null).ToList();
        if (missing.Count == 0)
            return;
        var names = string.Join(", ", missing.Select(b => b.Name));
        var choice = await SocShared.ModernDialog.ActionSheetAsync(page, string.Format(l.CurrentCulture, l["ExtOfferTitle"], names), l["NotNow"], l["ExtInstallNow"], l["ExtDontAsk"]);
        if (choice == l["ExtDontAsk"])
        {
            settings.AskExtensions = false;
            return;
        }
        if (choice != l["ExtInstallNow"])
            return;
        foreach (var b in missing)
            await InstallAsync(page, b, l);
    }

    /// <summary>Instala lo automatizable y guia el paso manual (cargar la carpeta en el navegador).</summary>
    public static async Task InstallAsync(Page page, Browser b, ILocalizationService l)
    {
        try
        {
            ExtensionInstaller.Install(b);
        }
        catch (Exception ex)
        {
            await SocShared.ModernDialog.AlertAsync(page, l["Error"], ex.Message, l["Ok"]);
            return;
        }
        var dir = ExtensionInstaller.ExtensionDir(b.IsFirefox);
        try { await Clipboard.Default.SetTextAsync(b.IsFirefox ? System.IO.Path.Combine(dir, "manifest.json") : dir); } catch (Exception) { }
        var steps = b.IsFirefox
            ? string.Format(l.CurrentCulture, l["ExtStepsFirefox"], b.Name, System.IO.Path.Combine(dir, "manifest.json"))
            : string.Format(l.CurrentCulture, l["ExtStepsChromium"], b.Name, l["ExtLoadUnpacked_" + b.Key], dir);
        var open = await SocShared.ModernDialog.AlertAsync(page, string.Format(l.CurrentCulture, l["ExtInstallIn"], b.Name), steps, string.Format(l.CurrentCulture, l["ExtOpenBrowser"], b.Name), l["Cancel"]);
        if (open)
            ExtensionInstaller.OpenExtensionsPage(b);
    }
}
