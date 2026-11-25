using Credentials.Platforms.Windows;
using Credentials.Services;

namespace Credentials.Ui.Tests;

// Dobles de lo que solo existe en Windows o en Android: el registro, el icono de bandeja, la ventana
// nativa, el autocompletar de escritorio, la tuberia de las extensiones y lo del sistema Android. Con
// ellos las paginas y DesktopIntegration recorren las ramas de cada plataforma sin tocar nada del PC.

/// <summary>El registro de Windows en memoria.</summary>
internal sealed class FakeRegistry : IRegistryAccess
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

    public void DeleteKey(string key)
    {
        if (Broken) throw new UnauthorizedAccessException();
        foreach (var k in Values.Keys.Where(k => k.Item1 == RegistryRoot.CurrentUser && k.Item2 == key).ToList())
            Values.Remove(k);
    }
}

/// <summary>El icono de bandeja: apunta lo que haria con la ventana en lugar de hacerlo.</summary>
internal sealed class FakeTray(Func<string, string> text, Action exit) : TrayController(0xC123, exit)
{
    public Func<string, string> Text { get; } = text;
    public List<string> Calls { get; } = [];

    protected override void AddIcon() => Calls.Add("add");
    protected override void RemoveIcon() => Calls.Add("remove");
    protected override void ShowWindow(int command) => Calls.Add("show:" + command);
    protected override void BringToForeground() => Calls.Add("front");
    protected override void ShowMenu() => Calls.Add("menu");
    protected override void NotifyShown() => Calls.Add("shown");

    public void LockSession() => Handle(WmWtsSessionChange, WtsSessionLock, 0);
    public void UnlockSession() => Handle(WmWtsSessionChange, WtsSessionUnlock, 0);
}

/// <summary>La ventana nativa: su sitio y tamaño, y lo que se le pide.</summary>
internal sealed class FakeFrame : IWindowFrame
{
    public FrameRect Bounds { get; set; } = new(100, 80, 900, 760);
    public int ClientHeight => Bounds.Height - 32;   // bordes de Windows
    public double Scale { get; set; } = 1.5;
    public FrameRect WorkArea { get; set; } = new(0, 0, 1920, 1040);
    public bool IsMaximized { get; set; }
    public bool Broken { get; set; }
    public List<string> Calls { get; } = [];

    public void Restore() { Calls.Add("restore"); IsMaximized = false; }
    public void Maximize() { Calls.Add("maximize"); IsMaximized = true; }

    public void MoveAndResize(FrameRect bounds)
    {
        if (Broken) throw new InvalidOperationException("ventana cerrada");
        Calls.Add($"move:{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");
        Bounds = bounds;
    }

    public void MinimizeWithRestoreBounds(FrameRect restoreBounds, bool restoreMaximized)
    {
        if (Broken) throw new InvalidOperationException("ventana cerrada");
        Calls.Add($"minimize:{restoreBounds.X},{restoreBounds.Y},{restoreBounds.Width},{restoreBounds.Height}:{restoreMaximized}");
    }
}

/// <summary>Lo de Windows de DesktopIntegration, sin Win32: apunta y contesta lo que la prueba diga.</summary>
internal sealed class FakeDesktopHost : IDesktopHost
{
    public FakeDesktopHost(FakeRegistry registry, string root)
    {
        Root = root;
        AppDir = Path.Combine(root, "app");
        Write(Path.Combine(AppDir, "Extension", "common", "background.js"), "bg");
        Write(Path.Combine(AppDir, "Extension", "chromium", "manifest.json"), "chromium");
        Write(Path.Combine(AppDir, "Extension", "firefox", "manifest.json"), "firefox");
        Write(Path.Combine(AppDir, "CredentialsHost.exe"), "host");
        Registry = registry;
        Extensions = new ExtensionInstallerCore(registry, Path.Combine(root, "data"), AppDir, () => null, () => Path.Combine(AppDir, "Credentials.exe"),
            (exe, url) => Opened.Add((exe, url)), () => IsPackaged);
        Startup = new StartupRegistration(registry, () => @"C:\sOC\sOCCredentials.exe");
    }

    public string Root { get; }
    public string AppDir { get; }
    public FakeRegistry Registry { get; }
    public List<(string Exe, string Url)> Opened { get; } = [];
    public string[] CommandLine { get; set; } = ["Credentials.exe"];
    public bool IsPackaged { get; set; }
    public ExtensionInstallerCore Extensions { get; }
    public StartupRegistration Startup { get; }
    public TimeSpan? Idle { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Sin ventana nativa todavia (el primer HandlerChanged de MAUI).</summary>
    public bool NoNativeWindow { get; set; }
    public FakeTray? Tray { get; private set; }
    public int TraysCreated, Posted, AutofillStarted, TaskbarApplied, BroughtToFront;
    public bool AutofillFails { get; set; }
    public bool ExtensionServerFails { get; set; }
    public bool BringToFrontFails { get; set; }
    public Action? RequestUnlock { get; private set; }
    public Action<string>? BrowserConnected { get; private set; }
    public FakeFrame? Frame { get; set; }
    public bool ExitCalled { get; private set; }

    public string AddBrowser(string exe)
    {
        var path = Path.Combine(Root, "browsers", exe);
        Write(path, "exe");
        Registry.Values[(RegistryRoot.LocalMachine, ExtensionInstallerCore.AppPathsKey + exe, null)] = path;
        return path;
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public TimeSpan? SystemIdle() => Idle;

    public TrayController? CreateTray(Window window, Func<string, string> text)
    {
        if (NoNativeWindow)
            return null;
        TraysCreated++;
        return Tray = new FakeTray(text, () => ExitCalled = true);
    }

    public void Post(Window window, Action action)
    {
        Posted++;
        action();
    }

    public void StartDesktopAutofill(VaultStore store, ISettingsService settings, ILocalizationService l)
    {
        if (AutofillFails)
            throw new InvalidOperationException("sin accesibilidad");
        AutofillStarted++;
    }

    public void ApplyTaskbarIdentity(Window window) => TaskbarApplied++;

    public void StartExtensionServer(VaultStore store, ISettingsService settings, Action requestUnlock, Action<string> browserConnected)
    {
        if (ExtensionServerFails)
            throw new IOException("tuberia ocupada");
        RequestUnlock = requestUnlock;
        BrowserConnected = browserConnected;
    }

    public void BringToFront(Window? window)
    {
        if (BringToFrontFails)
            throw new InvalidOperationException("sin ventana");
        BroughtToFront++;
    }

    public IWindowFrame? FrameOf(Window? window) => window is null ? null : Frame;
}

/// <summary>Lo de Android: contesta lo que la prueba diga y apunta lo que se le pide.</summary>
internal sealed class FakeAndroid : IAndroidIntegration
{
    public bool IsAvailable => true;
    public bool AutofillSupported { get; set; } = true;
    public bool AutofillIsOurs { get; set; }
    public bool HasPreferredService { get; set; }
    public List<(string Package, string Name)> Browsers { get; } = [];
    public bool CanOpen { get; set; } = true;
    public Exception? RequestFails { get; set; }
    public GateBackAction Back { get; set; } = GateBackAction.MovedToBack;
    public List<string> Calls { get; } = [];

    public IReadOnlyList<(string Package, string Name)> InstalledBrowsers() => Browsers;

    public bool OpenPreferredService()
    {
        Calls.Add("preferred");
        return CanOpen;
    }

    public bool OpenBrowserSettings(string package)
    {
        Calls.Add("browser:" + package);
        return CanOpen;
    }

    public void RequestAutofill()
    {
        Calls.Add("request");
        if (RequestFails is not null)
            throw RequestFails;
    }

    public void ApplyScreenshotPolicy() => Calls.Add("screenshots");

    public Task OfferAutofillAfterUnlockAsync(Page page)
    {
        Calls.Add("offer");
        return Task.CompletedTask;
    }

    public void MoveToBack() => Calls.Add("back");

    public GateBackAction GateBack()
    {
        Calls.Add("gateback");
        return Back;
    }
}
