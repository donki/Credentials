using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Credentials.Services;

namespace Credentials.Platforms.Windows;

/// <summary>Las dos ramas del registro de Windows que se leen.</summary>
public enum RegistryRoot { CurrentUser, LocalMachine }

/// <summary>
/// El registro de Windows, lo justo: leer un valor de texto y escribir o borrar en HKCU. En la
/// aplicacion es el de verdad (WindowsRegistry); en las pruebas, uno en memoria.
/// </summary>
public interface IRegistryAccess
{
    /// <summary>El valor <paramref name="name"/> (null = el predeterminado) de la clave, o null si no hay.</summary>
    string? GetString(RegistryRoot root, string key, string? name);

    /// <summary>Escribe un valor de texto en HKCU (creando la clave si hace falta).</summary>
    void SetString(string key, string? name, string value);

    /// <summary>Borra un valor de HKCU (si no esta, nada).</summary>
    void DeleteValue(string key, string name);
}

/// <summary>Un navegador con soporte: donde esta y donde registra sus hosts de mensajeria nativa.</summary>
public sealed record Browser(string Key, string Name, string Exe, string HostRegistryKey, bool IsFirefox, string ExtensionsUrl, string? StoreUrl = null)
{
    public string? Path { get; init; }
    public bool Installed => Path is not null;
}

/// <summary>El exe que hay que apuntar para arrancar la aplicacion: el lanzador si la abrio el, si no el propio exe.</summary>
public static class AppExecutable
{
    public static string? Resolve(string? launcher, string? processPath, Func<string, bool> exists) =>
        !string.IsNullOrEmpty(launcher) && exists(launcher) ? launcher : processPath;
}

/// <summary>
/// Instalacion de la extension: deja la carpeta desempaquetada en &lt;raiz&gt;\extension, registra
/// CredentialsHost.exe para cada navegador (HKCU, sin permisos de administrador) y guia al usuario
/// para cargarla, que es lo unico que el navegador no deja automatizar sin publicarla. Las rutas, el
/// registro y abrir el navegador se le pasan: en Windows son los de verdad (ExtensionInstaller).
/// </summary>
public sealed class ExtensionInstallerCore(IRegistryAccess registry, string root, string appDirectory, Func<string?> launcher, Func<string?> processPath, Action<string, string> open)
{
    public const string HostName = "com.socratic.credentials";
    public const string ChromiumId = "hbimfdiggibkbjnmkagdcnddpghhckho";   // sale de la clave «key» del manifiesto (carga a mano)
    public const string EdgeStoreId = "pcilggpjodagihemfbimfbnnmlbfhfbk";  // la publicada en Edge Add-ons (2026-09-24)
    public const string EdgeStoreUrl = "https://microsoftedge.microsoft.com/addons/detail/soc-credentials/pcilggpjodagihemfbimfbnnmlbfhfbk";
    public const string FirefoxId = "credentials@socratic.app";                // el mismo en AMO: el puente ya la autoriza
    public const string FirefoxStoreUrl = "https://addons.mozilla.org/firefox/addon/soc-credentials/";  // publicada en AMO (2026-09-27)
    public const string AppKey = @"Software\sOCratic\Credentials";
    public const string AppPathsKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\";

    public static readonly Browser[] Known =
    [
        new("edge", "Microsoft Edge", "msedge.exe", @"Software\Microsoft\Edge\NativeMessagingHosts", false, "edge://extensions/", EdgeStoreUrl),
        new("chrome", "Google Chrome", "chrome.exe", @"Software\Google\Chrome\NativeMessagingHosts", false, "chrome://extensions/"),
        new("firefox", "Firefox", "firefox.exe", @"Software\Mozilla\NativeMessagingHosts", true, "about:debugging#/runtime/this-firefox", FirefoxStoreUrl),
    ];

    public string Root => root;
    public string ExtensionDir(bool firefox) => Path.Combine(root, "extension", firefox ? "firefox" : "chromium");
    public string HostDir => Path.Combine(root, "host");
    private string SourceDir => Path.Combine(appDirectory, "Extension");
    private string HostExe => Path.Combine(appDirectory, "CredentialsHost.exe");

    /// <summary>La aplicacion trae la extension y el host (en Debug sin publicar el host, no).</summary>
    public bool Available => Directory.Exists(SourceDir) && File.Exists(HostExe);

    /// <summary>Los navegadores que hay en este PC (por sus «App Paths»).</summary>
    public List<Browser> Detected() => Known.Select(b => b with { Path = FindExe(b.Exe) }).Where(b => b.Installed).ToList();

    private string? FindExe(string exe)
    {
        foreach (var hive in new[] { RegistryRoot.CurrentUser, RegistryRoot.LocalMachine })
        {
            try
            {
                if (registry.GetString(hive, AppPathsKey + exe, null) is { } path && File.Exists(path))
                    return path;
            }
            catch (Exception) { }
        }
        return null;
    }

    public bool IsRegistered(Browser b)
    {
        try { return registry.GetString(RegistryRoot.CurrentUser, b.HostRegistryKey + "\\" + HostName, null) is { } path && File.Exists(path); }
        catch (Exception) { return false; }
    }

    /// <summary>Copia la extension (comun + manifiesto del navegador) y escribe el manifiesto del host y su clave.</summary>
    public void Install(Browser b)
    {
        Extract(b.IsFirefox);
        RegisterHost(b);
        RegisterAppPath();
    }

    /// <summary>
    /// Al arrancar: se deja la extension desempaquetada y el host registrado para **todos** los
    /// navegadores que haya en el PC, esten o no ya registrados.
    /// </summary>
    /// <remarks>
    /// Antes solo se refrescaba lo ya registrado, y registrar era cosa del boton «Instalar». Quien
    /// cargaba la extension a mano (sobre todo en Firefox, que solo admite la carga temporal) se
    /// encontraba con que no podia abrir la aplicacion ni conectarse: no habia manifiesto del host ni
    /// clave en el registro. Esto solo escribe un json en %LOCALAPPDATA% y un valor en HKCU por
    /// navegador, y hay que rehacerlo en cada version porque la carpeta de la aplicacion cambia.
    /// </remarks>
    public void RegisterForInstalledBrowsers()
    {
        try
        {
            RegisterAppPath();
            foreach (var b in Detected())
                Install(b);
        }
        catch (Exception) { }
    }

    private void Extract(bool firefox)
    {
        var target = ExtensionDir(firefox);
        Directory.CreateDirectory(target);
        CopyTree(Path.Combine(SourceDir, "common"), target);
        File.Copy(Path.Combine(SourceDir, firefox ? "firefox" : "chromium", "manifest.json"), Path.Combine(target, "manifest.json"), overwrite: true);
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(from))
            CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    private void RegisterHost(Browser b)
    {
        Directory.CreateDirectory(HostDir);
        var manifestPath = Path.Combine(HostDir, HostName + (b.IsFirefox ? ".firefox" : "") + ".json");
        var manifest = new JsonObject
        {
            ["name"] = HostName,
            ["description"] = "sOC Credentials",
            ["path"] = StableHostExe(),
            ["type"] = "stdio",
        };
        if (b.IsFirefox)
            manifest["allowed_extensions"] = new JsonArray(FirefoxId);
        else
            // Las dos: la de la tienda y la cargada a mano (la de desarrollo, con id fijo por la «key»).
            manifest["allowed_origins"] = new JsonArray($"chrome-extension://{EdgeStoreId}/", $"chrome-extension://{ChromiumId}/");
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        registry.SetString(b.HostRegistryKey + "\\" + HostName, null, manifestPath);
    }

    /// <summary>
    /// El host que se apunta en el manifiesto vive en <c>&lt;raiz&gt;\host</c>, no en la carpeta de la
    /// version.
    /// </summary>
    /// <remarks>
    /// El lanzador desempaqueta cada version en <c>app\&lt;version&gt;</c> y borra la anterior: un
    /// manifiesto que apuntara ahi se quedaba señalando un exe que ya no existe en cuanto se entregaba
    /// otra version, y el navegador decia «desconectado» (Firefox no distingue «no esta» de «no
    /// arranca»). Aqui se copia el exe a un sitio fijo y se apunta a ese. Si esta en uso (el navegador
    /// lo tiene abierto) se deja el que hay: es el mismo programa.
    /// </remarks>
    public string StableHostExe()
    {
        var stable = Path.Combine(HostDir, "CredentialsHost.exe");
        try
        {
            Directory.CreateDirectory(HostDir);
            var origen = new FileInfo(HostExe);
            var destino = new FileInfo(stable);
            if (origen.Exists && (!destino.Exists || destino.Length != origen.Length || destino.LastWriteTimeUtc < origen.LastWriteTimeUtc))
                File.Copy(HostExe, stable, overwrite: true);
        }
        catch (Exception)
        {
            // En uso o sin permiso: si ya hay una copia sirve, y si no, se apunta al de la version.
        }
        return File.Exists(stable) ? stable : HostExe;
    }

    /// <summary>Donde arrancar la aplicacion si el host la encuentra cerrada: el lanzador si lo hay, si no el exe.</summary>
    private void RegisterAppPath()
    {
        var path = AppExecutable.Resolve(launcher(), processPath(), File.Exists);
        if (string.IsNullOrEmpty(path))
            return;
        registry.SetString(AppKey, "AppPath", path);
    }

    /// <summary>Publicada en su tienda: la ficha para instalarla con «Obtener»; si no, la pagina de extensiones para cargarla a mano.</summary>
    public void OpenExtensionsPage(Browser b)
    {
        if (b.Path is null)
            return;
        try { open(b.Path, b.StoreUrl ?? b.ExtensionsUrl); }
        catch (Exception) { }
    }
}

/// <summary>«Arrancar con Windows»: una entrada en HKCU\…\Run con el exe y --tray (arranca escondida en la bandeja).</summary>
public sealed class StartupRegistration(IRegistryAccess registry, Func<string> executablePath)
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled(string valueName)
    {
        try { return registry.GetString(RegistryRoot.CurrentUser, RunKey, valueName) is { Length: > 0 }; }
        catch (Exception) { return false; }
    }

    public void Set(string valueName, bool enabled)
    {
        try
        {
            var exe = executablePath();
            if (enabled && exe.Length > 0)
                registry.SetString(RunKey, valueName, "\"" + exe + "\" --tray");
            else
                registry.DeleteValue(RunKey, valueName);
        }
        catch (Exception)
        {
            // Sin permiso sobre HKCU no hay mas que hacer; el interruptor se queda como estaba.
        }
    }
}

/// <summary>Los dialogos que necesita la instalacion guiada (en la aplicacion, ModernDialog sobre la pagina).</summary>
public interface IExtensionPrompts
{
    Task<string?> ActionSheetAsync(string title, string cancel, params string[] options);

    Task<bool> AlertAsync(string title, string message, string accept, string? cancel = null);

    Task CopyToClipboardAsync(string text);
}

/// <summary>La parte con pantalla: la oferta tras desbloquear y la guia de instalacion por navegador.</summary>
public sealed class ExtensionSetupFlow(ExtensionInstallerCore installer)
{
    private bool _offeredThisSession;

    /// <summary>Tras desbloquear: si hay navegadores sin la extension, se ofrece instalarla (una vez por sesion).</summary>
    public async Task OfferAfterUnlockAsync(IExtensionPrompts prompts, ISettingsService settings, ILocalizationService l)
    {
        if (_offeredThisSession || !settings.AskExtensions || !installer.Available)
            return;
        _offeredThisSession = true;
        var missing = installer.Detected().Where(b => settings.ExtensionSeen(b.Key) is null).ToList();
        if (missing.Count == 0)
            return;
        var names = string.Join(", ", missing.Select(b => b.Name));
        var choice = await prompts.ActionSheetAsync(string.Format(l.CurrentCulture, l["ExtOfferTitle"], names), l["NotNow"], l["ExtInstallNow"], l["ExtDontAsk"]);
        if (choice == l["ExtDontAsk"])
        {
            settings.AskExtensions = false;
            return;
        }
        if (choice != l["ExtInstallNow"])
            return;
        foreach (var b in missing)
            await InstallAsync(prompts, b, l);
    }

    /// <summary>Instala lo automatizable y guia el paso manual (cargar la carpeta en el navegador).</summary>
    public async Task InstallAsync(IExtensionPrompts prompts, Browser b, ILocalizationService l)
    {
        try
        {
            installer.Install(b);
        }
        catch (Exception ex)
        {
            await prompts.AlertAsync(l["Error"], ex.Message, l["Ok"]);
            return;
        }
        if (b.StoreUrl is not null)
        {
            // Publicada en la tienda del navegador: nada de modo de desarrollador, se instala desde su ficha.
            if (await prompts.AlertAsync(string.Format(l.CurrentCulture, l["ExtInstallIn"], b.Name), string.Format(l.CurrentCulture, l["ExtStepsStore"], b.Name), l["ExtOpenStore"], l["Cancel"]))
                installer.OpenExtensionsPage(b);
            return;
        }
        // Sin tienda solo queda Chrome (Edge y Firefox instalan desde la suya): carpeta desempaquetada.
        var dir = installer.ExtensionDir(b.IsFirefox);
        try { await prompts.CopyToClipboardAsync(dir); } catch (Exception) { }
        var steps = string.Format(l.CurrentCulture, l["ExtStepsChromium"], b.Name, l["ExtLoadUnpacked_" + b.Key], dir);
        if (await prompts.AlertAsync(string.Format(l.CurrentCulture, l["ExtInstallIn"], b.Name), steps, string.Format(l.CurrentCulture, l["ExtOpenBrowser"], b.Name), l["Cancel"]))
            installer.OpenExtensionsPage(b);
    }
}
