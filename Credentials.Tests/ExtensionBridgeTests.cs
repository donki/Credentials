using System.Text.Json.Nodes;
using Credentials.Models;
using Credentials.Platforms.Windows;

namespace Credentials.Tests;

/// <summary>El lado de la aplicacion para las extensiones de navegador: peticiones, instalacion y «arrancar con Windows».</summary>
public class ExtensionBridgeTests
{
    private const string Master = "Correcta Caballo Pila Grapa";

    private sealed class Harness
    {
        public Harness(Sandbox box, int saveWaitMs = 300)
        {
            Box = box;
            Handler = new ExtensionRequestHandler(box.Store, box.Settings, async work => { MainThreadCalls++; await work(); }, () => Unlocks++)
            {
                SaveUnlockTimeout = TimeSpan.FromMilliseconds(saveWaitMs),
                SaveUnlockPoll = TimeSpan.FromMilliseconds(10),
            };
            Handler.BrowserConnected += b => Connected.Add(b);
        }

        public Sandbox Box { get; }
        public ExtensionRequestHandler Handler { get; }
        public List<string> Connected { get; } = [];
        public int Unlocks, MainThreadCalls;

        public async Task<JsonObject> Ask(string json) => JsonNode.Parse(await Handler.HandleAsync(json))!.AsObject();
    }

    [Fact]
    public async Task Hello_RemembersTheBrowser_AndSaysIfLocked()
    {
        using var box = Sandbox.Create();
        var h = new Harness(box);

        var first = await h.Ask("{\"id\":1,\"type\":\"hello\",\"browser\":\"firefox\"}");
        Assert.Equal(1, (int)first["id"]!);
        Assert.True((bool)first["ok"]!);
        Assert.True((bool)first["locked"]!);
        Assert.Equal(["firefox"], h.Connected);
        Assert.NotNull(box.Settings.ExtensionSeen("firefox"));

        // La segunda vez no se avisa; sin «browser» es Chrome.
        await h.Ask("{\"id\":2,\"type\":\"hello\",\"browser\":\"firefox\"}");
        await h.Ask("{\"id\":\"tres\",\"type\":\"hello\"}");
        Assert.Equal(["firefox", "chrome"], h.Connected);

        await box.Store.CreateAsync(Master);
        Assert.False((bool)(await h.Ask("{\"id\":4,\"type\":\"hello\"}"))["locked"]!);
        Assert.Equal(4, h.MainThreadCalls);
    }

    [Fact]
    public async Task Hello_OpensTheTrustedVault()
    {
        using var box = Sandbox.Create();
        await box.Store.CreateAsync(Master);
        await box.Store.RememberKeyAsync(true);
        box.Store.Lock();
        box.Settings.TrustDevice = true;
        var h = new Harness(box);

        Assert.False((bool)(await h.Ask("{\"id\":1,\"type\":\"hello\"}"))["locked"]!);
        Assert.True(box.Store.IsUnlocked);
    }

    [Fact]
    public async Task List_And_Search_OnlyWithTheVaultOpen()
    {
        using var box = Sandbox.Create();
        var h = new Harness(box);

        var locked = await h.Ask("{\"id\":1,\"type\":\"list\",\"host\":\"example.com\"}");
        Assert.True((bool)locked["locked"]!);
        Assert.Null(locked["entries"]);
        Assert.Equal(0, h.Unlocks);   // pasiva: no saca la ventana

        await box.Store.CreateAsync(Master);
        box.Store.Data!.Entries.AddRange(
        [
            new Credential { Title = "Ejemplo", Url = "https://login.example.com", Username = "ana", Password = "p\"1<", Totp = "JBSWY3DPEHPK3PXP" },
            new Credential { Title = "Otra", Url = "https://otra.org", Username = "luis", Password = "p2" },
        ]);

        var list = await h.Ask("{\"id\":2,\"type\":\"list\",\"host\":\"example.com\"}");
        var entry = Assert.Single(list["entries"]!.AsArray())!.AsObject();
        Assert.Equal("Ejemplo", (string)entry["title"]!);
        Assert.Equal("ana", (string)entry["username"]!);
        Assert.Equal("p\"1<", (string)entry["password"]!);
        Assert.Equal("https://login.example.com", (string)entry["url"]!);
        Assert.Matches("^[0-9]{6}$", (string)entry["totp"]!);
        Assert.InRange((int)entry["totpLeft"]!, 1, 30);

        var search = await h.Ask("{\"id\":3,\"type\":\"search\",\"query\":\"luis\"}");
        Assert.Equal("Otra", (string)Assert.Single(search["entries"]!.AsArray())!["title"]!);
        Assert.Null(search["entries"]![0]!["totp"]);
        Assert.Equal(2, (await h.Ask("{\"id\":4,\"type\":\"search\"}"))["entries"]!.AsArray().Count);
        Assert.Empty((await h.Ask("{\"id\":5,\"type\":\"list\"}"))["entries"]!.AsArray());

        // Sin escapar a \uXXXX lo que no hace falta (la extension lo lee tal cual).
        Assert.Contains("p\\\"1<", await h.Handler.HandleAsync("{\"id\":6,\"type\":\"list\",\"host\":\"example.com\"}"));
    }

    [Fact]
    public async Task Show_AsksForTheVault()
    {
        using var box = Sandbox.Create();
        var h = new Harness(box);
        Assert.True((bool)(await h.Ask("{\"id\":1,\"type\":\"show\"}"))["ok"]!);
        Assert.Equal(1, h.Unlocks);
    }

    [Fact]
    public async Task Save_WaitsForTheUnlock_ThenSaves()
    {
        using var box = Sandbox.Create();
        await box.Store.CreateAsync(Master);
        box.Store.Lock();
        var h = new Harness(box);

        // Cerrada y nadie la abre: «locked» al vencer la espera.
        var locked = await h.Ask("{\"id\":1,\"type\":\"save\",\"host\":\"example.com\",\"username\":\"ana\",\"password\":\"x\"}");
        Assert.True((bool)locked["locked"]!);
        Assert.Equal(1, h.Unlocks);

        // El usuario la abre mientras se espera.
        var h2 = new Harness(box, saveWaitMs: 60_000);
        var pending = h2.Ask("{\"id\":2,\"type\":\"save\",\"host\":\"example.com\",\"username\":\"ana\",\"password\":\"nueva\"}");
        await box.Store.UnlockAsync(Master);
        var saved = await pending;
        Assert.True((bool)saved["ok"]!);
        Assert.True((bool)saved["changed"]!);
        Assert.Equal("nueva", box.Store.Data!.Entries.Single().Password);

        // Lo mismo otra vez: nada que cambiar.
        Assert.False((bool)(await h2.Ask("{\"id\":3,\"type\":\"save\",\"host\":\"example.com\",\"username\":\"ana\",\"password\":\"nueva\"}"))["changed"]!);
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("")]
    [InlineData("[1,2]")]
    [InlineData("{\"id\":1,\"type\":5}")]
    public async Task BadRequests_AreBadJson(string line)
    {
        using var box = Sandbox.Create();
        Assert.Equal("{\"error\":\"badjson\"}", await new Harness(box).Handler.HandleAsync(line));
    }

    [Fact]
    public async Task UnknownTypes_And_Failures()
    {
        using var box = Sandbox.Create();
        var h = new Harness(box);
        var unknown = await h.Ask("{\"id\":1,\"type\":\"borrar-todo\"}");
        Assert.Equal("unknown", (string)unknown["error"]!);
        Assert.Equal("unknown", (string)(await h.Ask("null"))["error"]!);

        var broken = new ExtensionRequestHandler(box.Store, box.Settings, _ => throw new InvalidOperationException("sin hilo"), () => { });
        var reply = JsonNode.Parse(await broken.HandleAsync("{\"id\":7,\"type\":\"hello\"}"))!;
        Assert.Equal(7, (int)reply["id"]!);
        Assert.Equal("app", (string)reply["error"]!);
        Assert.Equal("sin hilo", (string)reply["detail"]!);
    }

    [Fact]
    public async Task Serve_AnswersLineByLine_UntilTheHostLeaves()
    {
        using var box = Sandbox.Create();
        var h = new Harness(box);
        var pipe = new DuplexStream("{\"id\":1,\"type\":\"show\"}\n{\"id\":2,\"type\":\"nada\"}\n");
        await h.Handler.ServeAsync(pipe, () => true, CancellationToken.None);
        Assert.Equal(["{\"id\":1,\"ok\":true}", "{\"id\":2,\"error\":\"unknown\"}"], pipe.Written.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));

        // Desconectado: no lee nada. Cancelado: sale sin lanzar.
        var idle = new DuplexStream("{\"id\":1,\"type\":\"show\"}\n");
        await h.Handler.ServeAsync(idle, () => false, CancellationToken.None);
        Assert.Equal(string.Empty, idle.Written);
        await h.Handler.ServeAsync(new DuplexStream("{}\n"), () => true, new CancellationToken(true));
    }

    // ------------------------------------------------------------------ instalacion

    private sealed class FakeRegistry : IRegistryAccess
    {
        public Dictionary<(RegistryRoot, string, string?), string> Values { get; } = [];
        public bool Broken { get; set; }

        public string? GetString(RegistryRoot root, string key, string? name) =>
            Broken ? throw new UnauthorizedAccessException() : Values.GetValueOrDefault((root, key, name));

        public void SetString(string key, string? name, string value)
        {
            if (Broken) throw new UnauthorizedAccessException();
            Values[(RegistryRoot.CurrentUser, key, name)] = value;
        }

        public void DeleteValue(string key, string name)
        {
            if (Broken) throw new UnauthorizedAccessException();
            Values.Remove((RegistryRoot.CurrentUser, key, name));
        }
    }

    /// <summary>Una carpeta de aplicacion falsa (con la extension y el host) y una raiz de datos, en temporal.</summary>
    private sealed class InstallBox : IDisposable
    {
        public InstallBox(bool withExtension = true)
        {
            Dir = Path.Combine(Path.GetTempPath(), "soccred-ext-" + Guid.NewGuid().ToString("N"));
            App = Path.Combine(Dir, "app");
            Directory.CreateDirectory(App);
            if (withExtension)
            {
                Write(Path.Combine(App, "Extension", "common", "background.js"), "bg");
                Write(Path.Combine(App, "Extension", "common", "icons", "16.png"), "png");
                Write(Path.Combine(App, "Extension", "chromium", "manifest.json"), "chromium");
                Write(Path.Combine(App, "Extension", "firefox", "manifest.json"), "firefox");
                Write(Path.Combine(App, "CredentialsHost.exe"), "host v1");
            }
            Launcher = Path.Combine(Dir, "sOCCredentials.exe");
            Core = new ExtensionInstallerCore(Registry, Path.Combine(Dir, "data"), App, () => LauncherEnv, () => Path.Combine(App, "Credentials.exe"), (exe, url) => Opened.Add((exe, url)));
        }

        public string Dir { get; }
        public string App { get; }
        public string Launcher { get; }
        public string? LauncherEnv { get; set; }
        public FakeRegistry Registry { get; } = new();
        public List<(string, string)> Opened { get; } = [];
        public ExtensionInstallerCore Core { get; }

        public string AddBrowser(string exe, RegistryRoot root = RegistryRoot.LocalMachine)
        {
            var path = Path.Combine(Dir, "browsers", exe);
            Write(path, "exe");
            Registry.Values[(root, ExtensionInstallerCore.AppPathsKey + exe, null)] = path;
            return path;
        }

        public static void Write(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose() => Directory.Delete(Dir, true);
    }

    [Fact]
    public void Detected_ByAppPaths()
    {
        using var ib = new InstallBox();
        Assert.Empty(ib.Core.Detected());
        var edge = ib.AddBrowser("msedge.exe", RegistryRoot.CurrentUser);
        ib.AddBrowser("firefox.exe");
        // Una clave que apunta a un exe que ya no esta no cuenta.
        ib.Registry.Values[(RegistryRoot.LocalMachine, ExtensionInstallerCore.AppPathsKey + "chrome.exe", null)] = Path.Combine(ib.Dir, "no-esta.exe");

        var found = ib.Core.Detected();
        Assert.Equal(["edge", "firefox"], found.Select(b => b.Key));
        Assert.Equal(edge, found[0].Path);
        Assert.True(found[0].Installed);
        Assert.False(ExtensionInstallerCore.Known[0].Installed);

        ib.Registry.Broken = true;
        Assert.Empty(ib.Core.Detected());
    }

    [Fact]
    public void Install_CopiesTheExtension_WritesTheHostManifest_AndRegisters()
    {
        using var ib = new InstallBox();
        File.WriteAllText(ib.Launcher, "lanzador");
        ib.LauncherEnv = ib.Launcher;
        Assert.True(ib.Core.Available);
        var edge = ExtensionInstallerCore.Known.Single(b => b.Key == "edge");
        var firefox = ExtensionInstallerCore.Known.Single(b => b.Key == "firefox");
        Assert.False(ib.Core.IsRegistered(edge));

        ib.Core.Install(edge);
        ib.Core.Install(firefox);

        var chromiumDir = ib.Core.ExtensionDir(false);
        Assert.Equal(Path.Combine(ib.Dir, "data", "extension", "chromium"), chromiumDir);
        Assert.Equal("bg", File.ReadAllText(Path.Combine(chromiumDir, "background.js")));
        Assert.Equal("png", File.ReadAllText(Path.Combine(chromiumDir, "icons", "16.png")));
        Assert.Equal("chromium", File.ReadAllText(Path.Combine(chromiumDir, "manifest.json")));
        Assert.Equal("firefox", File.ReadAllText(Path.Combine(ib.Core.ExtensionDir(true), "manifest.json")));

        var stableHost = Path.Combine(ib.Core.HostDir, "CredentialsHost.exe");
        Assert.Equal("host v1", File.ReadAllText(stableHost));
        var edgeManifest = ib.Registry.Values[(RegistryRoot.CurrentUser, edge.HostRegistryKey + "\\" + ExtensionInstallerCore.HostName, null)];
        Assert.Equal(Path.Combine(ib.Core.HostDir, "com.socratic.credentials.json"), edgeManifest);
        var manifest = JsonNode.Parse(File.ReadAllText(edgeManifest))!;
        Assert.Equal(ExtensionInstallerCore.HostName, (string)manifest["name"]!);
        Assert.Equal(stableHost, (string)manifest["path"]!);
        Assert.Equal("stdio", (string)manifest["type"]!);
        Assert.Equal([$"chrome-extension://{ExtensionInstallerCore.EdgeStoreId}/", $"chrome-extension://{ExtensionInstallerCore.ChromiumId}/"],
            manifest["allowed_origins"]!.AsArray().Select(n => (string)n!));

        var ffManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(ib.Core.HostDir, "com.socratic.credentials.firefox.json")))!;
        Assert.Equal(ExtensionInstallerCore.FirefoxId, (string)ffManifest["allowed_extensions"]![0]!);
        Assert.Null(ffManifest["allowed_origins"]);

        Assert.Equal(ib.Launcher, ib.Registry.Values[(RegistryRoot.CurrentUser, ExtensionInstallerCore.AppKey, "AppPath")]);
        Assert.True(ib.Core.IsRegistered(edge));
        Assert.False(ib.Core.IsRegistered(ExtensionInstallerCore.Known.Single(b => b.Key == "chrome")));
        ib.Registry.Broken = true;
        Assert.False(ib.Core.IsRegistered(edge));
    }

    [Fact]
    public void StableHost_CopiesNewVersions_AndKeepsTheOneInUse()
    {
        using var ib = new InstallBox();
        var stable = ib.Core.StableHostExe();
        Assert.Equal("host v1", File.ReadAllText(stable));

        // Una version nueva (otro tamaño): se copia encima.
        File.WriteAllText(Path.Combine(ib.App, "CredentialsHost.exe"), "host v2 mas largo");
        Assert.Equal("host v2 mas largo", File.ReadAllText(ib.Core.StableHostExe()));

        // En uso por el navegador: se deja la que hay y se apunta a ella.
        File.WriteAllText(Path.Combine(ib.App, "CredentialsHost.exe"), "host v3");
        using (new FileStream(stable, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Equal(stable, ib.Core.StableHostExe());
        Assert.Equal("host v2 mas largo", File.ReadAllText(stable));
    }

    [Fact]
    public void WithoutTheBundledHost_NotAvailable_AndPointsToTheAppFolder()
    {
        using var ib = new InstallBox(withExtension: false);
        Assert.False(ib.Core.Available);
        Assert.Equal(Path.Combine(ib.App, "CredentialsHost.exe"), ib.Core.StableHostExe());
    }

    [Fact]
    public void RegisterForInstalledBrowsers_RegistersEveryDetectedBrowser()
    {
        using var ib = new InstallBox();
        ib.AddBrowser("chrome.exe");
        ib.AddBrowser("firefox.exe");
        ib.LauncherEnv = Path.Combine(ib.Dir, "no-esta.exe");   // lanzador que no existe: el exe de la app

        ib.Core.RegisterForInstalledBrowsers();
        Assert.True(ib.Core.IsRegistered(ib.Core.Detected()[0]));
        Assert.True(ib.Core.IsRegistered(ib.Core.Detected()[1]));
        Assert.Equal(Path.Combine(ib.App, "Credentials.exe"), ib.Registry.Values[(RegistryRoot.CurrentUser, ExtensionInstallerCore.AppKey, "AppPath")]);

        // Si el registro no deja, no pasa nada.
        ib.Registry.Broken = true;
        ib.Core.RegisterForInstalledBrowsers();
    }

    [Fact]
    public void AppPath_NotWritten_WithoutAnyExe()
    {
        var registry = new FakeRegistry();
        var dir = Path.Combine(Path.GetTempPath(), "soccred-ext-" + Guid.NewGuid().ToString("N"));
        var core = new ExtensionInstallerCore(registry, dir, dir, () => null, () => null, (_, _) => { });
        core.RegisterForInstalledBrowsers();
        Assert.Empty(registry.Values);
        Assert.Equal(dir, core.Root);
        Assert.Equal("x", AppExecutable.Resolve("x", "y", _ => true));
        Assert.Equal("y", AppExecutable.Resolve("x", "y", _ => false));
        Assert.Equal("y", AppExecutable.Resolve("", "y", _ => true));
    }

    [Fact]
    public void OpenExtensionsPage_StoreFirst_AndNothingWithoutBrowser()
    {
        using var ib = new InstallBox();
        var edge = ExtensionInstallerCore.Known[0] with { Path = @"C:\edge.exe" };
        var chrome = ExtensionInstallerCore.Known[1] with { Path = @"C:\chrome.exe" };
        ib.Core.OpenExtensionsPage(edge);
        ib.Core.OpenExtensionsPage(chrome);
        ib.Core.OpenExtensionsPage(ExtensionInstallerCore.Known[2]);
        Assert.Equal([(@"C:\edge.exe", ExtensionInstallerCore.EdgeStoreUrl), (@"C:\chrome.exe", "chrome://extensions/")], ib.Opened);

        var failing = new ExtensionInstallerCore(ib.Registry, ib.Dir, ib.App, () => null, () => null, (_, _) => throw new InvalidOperationException());
        failing.OpenExtensionsPage(edge);
    }

    [Fact]
    public void Startup_RunKey()
    {
        var registry = new FakeRegistry();
        var exe = @"C:\Program Files\sOC\sOCCredentials.exe";
        var startup = new StartupRegistration(registry, () => exe);
        Assert.False(startup.IsEnabled("sOCCredentials"));

        startup.Set("sOCCredentials", true);
        Assert.Equal("\"C:\\Program Files\\sOC\\sOCCredentials.exe\" --tray", registry.Values[(RegistryRoot.CurrentUser, StartupRegistration.RunKey, "sOCCredentials")]);
        Assert.True(startup.IsEnabled("sOCCredentials"));

        startup.Set("sOCCredentials", false);
        Assert.False(startup.IsEnabled("sOCCredentials"));
        startup.Set("sOCCredentials", false);   // borrar lo que no esta tampoco falla

        // Sin exe conocido no se escribe nada; sin permiso, nada cambia y no lanza.
        exe = string.Empty;
        startup.Set("sOCCredentials", true);
        Assert.False(startup.IsEnabled("sOCCredentials"));
        registry.Broken = true;
        startup.Set("sOCCredentials", true);
        Assert.False(startup.IsEnabled("sOCCredentials"));
    }

    // ------------------------------------------------------------------ la guia

    private sealed class FakePrompts : IExtensionPrompts
    {
        public Queue<string?> Sheets { get; } = new();
        public Queue<bool> Alerts { get; } = new();
        public List<string> Log { get; } = [];
        public bool ClipboardBroken { get; set; }

        public Task<string?> ActionSheetAsync(string title, string cancel, params string[] options)
        {
            Log.Add($"sheet:{title}|{cancel}|{string.Join(",", options)}");
            return Task.FromResult(Sheets.Dequeue());
        }

        public Task<bool> AlertAsync(string title, string message, string accept, string? cancel = null)
        {
            Log.Add($"alert:{title}|{message}|{accept}|{cancel}");
            return Task.FromResult(Alerts.Count > 0 && Alerts.Dequeue());
        }

        public Task CopyToClipboardAsync(string text)
        {
            if (ClipboardBroken) throw new InvalidOperationException();
            Log.Add("copy:" + text);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Offer_OncePerSession_OnlyForMissingBrowsers()
    {
        using var box = Sandbox.Create();
        var l = box.Texts("es");
        using var ib = new InstallBox();
        ib.AddBrowser("msedge.exe");
        ib.AddBrowser("chrome.exe");
        box.Settings.SetExtensionSeen("edge");
        var prompts = new FakePrompts();
        prompts.Sheets.Enqueue(l["NotNow"]);
        var flow = new ExtensionSetupFlow(ib.Core);

        await flow.OfferAfterUnlockAsync(prompts, box.Settings, l);
        Assert.Equal($"sheet:{string.Format(l["ExtOfferTitle"], "Google Chrome")}|{l["NotNow"]}|{l["ExtInstallNow"]},{l["ExtDontAsk"]}", Assert.Single(prompts.Log));
        Assert.False(ib.Core.IsRegistered(ib.Core.Detected()[0]));

        // Una vez por sesion.
        await flow.OfferAfterUnlockAsync(prompts, box.Settings, l);
        Assert.Single(prompts.Log);

        // «No volver a preguntar».
        var again = new ExtensionSetupFlow(ib.Core);
        prompts.Sheets.Enqueue(l["ExtDontAsk"]);
        await again.OfferAfterUnlockAsync(prompts, box.Settings, l);
        Assert.False(box.Settings.AskExtensions);
        await new ExtensionSetupFlow(ib.Core).OfferAfterUnlockAsync(prompts, box.Settings, l);
        Assert.Equal(2, prompts.Log.Count);
    }

    [Fact]
    public async Task Offer_NothingWhenAllConnected_OrWithoutTheExtension()
    {
        using var box = Sandbox.Create();
        var l = box.Texts("es");
        using var ib = new InstallBox();
        ib.AddBrowser("msedge.exe");
        box.Settings.SetExtensionSeen("edge");
        var prompts = new FakePrompts();
        await new ExtensionSetupFlow(ib.Core).OfferAfterUnlockAsync(prompts, box.Settings, l);
        using var empty = new InstallBox(withExtension: false);
        await new ExtensionSetupFlow(empty.Core).OfferAfterUnlockAsync(prompts, box.Settings, l);
        Assert.Empty(prompts.Log);
    }

    [Fact]
    public async Task InstallNow_StoreBrowsers_And_UnpackedChrome()
    {
        using var box = Sandbox.Create();
        var l = box.Texts("es");
        using var ib = new InstallBox();
        ib.AddBrowser("msedge.exe");
        ib.AddBrowser("chrome.exe");
        var prompts = new FakePrompts();
        prompts.Sheets.Enqueue(l["ExtInstallNow"]);
        prompts.Alerts.Enqueue(true);    // Edge: abrir la tienda
        prompts.Alerts.Enqueue(true);    // Chrome: abrir el navegador

        await new ExtensionSetupFlow(ib.Core).OfferAfterUnlockAsync(prompts, box.Settings, l);

        var detected = ib.Core.Detected();
        Assert.All(detected, b => Assert.True(ib.Core.IsRegistered(b)));
        var dir = ib.Core.ExtensionDir(false);
        Assert.Equal(
        [
            $"sheet:{string.Format(l["ExtOfferTitle"], "Microsoft Edge, Google Chrome")}|{l["NotNow"]}|{l["ExtInstallNow"]},{l["ExtDontAsk"]}",
            $"alert:{string.Format(l["ExtInstallIn"], "Microsoft Edge")}|{string.Format(l["ExtStepsStore"], "Microsoft Edge")}|{l["ExtOpenStore"]}|{l["Cancel"]}",
            "copy:" + dir,
            $"alert:{string.Format(l["ExtInstallIn"], "Google Chrome")}|{string.Format(l["ExtStepsChromium"], "Google Chrome", l["ExtLoadUnpacked_chrome"], dir)}|{string.Format(l["ExtOpenBrowser"], "Google Chrome")}|{l["Cancel"]}",
        ], prompts.Log);
        Assert.Equal([(detected[0].Path!, ExtensionInstallerCore.EdgeStoreUrl), (detected[1].Path!, "chrome://extensions/")], ib.Opened);
    }

    [Fact]
    public async Task Install_Cancelled_Or_Failing()
    {
        using var box = Sandbox.Create();
        var l = box.Texts("es");
        using var ib = new InstallBox();
        var flow = new ExtensionSetupFlow(ib.Core);
        var prompts = new FakePrompts { ClipboardBroken = true };
        var chrome = ExtensionInstallerCore.Known[1] with { Path = @"C:\chrome.exe" };
        var edge = ExtensionInstallerCore.Known[0] with { Path = @"C:\edge.exe" };

        // Cancelar en los dos casos: no se abre nada (y sin portapapeles sigue igual).
        await flow.InstallAsync(prompts, chrome, l);
        await flow.InstallAsync(prompts, edge, l);
        Assert.Empty(ib.Opened);
        Assert.Equal(2, prompts.Log.Count);

        // Si instalar falla (la extension no esta), un aviso con el error y nada mas.
        using var empty = new InstallBox(withExtension: false);
        prompts.Log.Clear();
        await new ExtensionSetupFlow(empty.Core).InstallAsync(prompts, chrome, l);
        Assert.StartsWith($"alert:{l["Error"]}|", Assert.Single(prompts.Log));
    }
}
