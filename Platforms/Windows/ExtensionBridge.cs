using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Credentials.Services;
using Microsoft.Win32;

namespace Credentials.Platforms.Windows;

/// <summary>
/// El lado de la aplicacion para las extensiones de navegador. CredentialsHost.exe (mensajeria
/// nativa) le pasa por la tuberia sOCCredentials una linea JSON por peticion y espera otra linea de
/// respuesta con el mismo «id». La tuberia solo la puede abrir el mismo usuario de Windows. Lo que
/// se contesta esta en ExtensionRequestHandler (PlatformLogic, con pruebas); aqui, la tuberia.
/// </summary>
public sealed class ExtensionServer
{
    public const string PipeName = "sOCCredentials";

    private readonly ExtensionRequestHandler _handler;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Un navegador acaba de saludar por primera vez (para avisar en pantalla).</summary>
    public event Action<string>? BrowserConnected
    {
        add => _handler.BrowserConnected += value;
        remove => _handler.BrowserConnected -= value;
    }

    public ExtensionServer(VaultStore store, ISettingsService settings) =>
        _handler = new ExtensionRequestHandler(store, settings, MainThread.InvokeOnMainThreadAsync, () => RequestUnlock(store));

    /// <summary>Atiende la tuberia en segundo plano; solo la puede abrir el mismo usuario de Windows.</summary>
    public void Start()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        var loop = new PipeServerLoop(() => NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security), _handler);
        _ = Task.Run(() => loop.RunAsync(_cts.Token));
    }

    public void Stop() => _cts.Cancel();

    /// <summary>El usuario necesita la boveda: se trae la ventana y, si esta cerrada, la pagina de desbloqueo.</summary>
    private static void RequestUnlock(VaultStore store)
    {
        WindowHelper.BringToFront();
        if (!store.IsUnlocked && Application.Current?.Windows.FirstOrDefault()?.Page is { } page)
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

/// <summary>El registro de Windows de verdad (HKCU para escribir).</summary>
internal sealed class WindowsRegistry : IRegistryAccess
{
    public string? GetString(RegistryRoot root, string key, string? name)
    {
        using var k = (root == RegistryRoot.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine).OpenSubKey(key);
        return k?.GetValue(name) as string;
    }

    public void SetString(string key, string? name, string value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key, writable: true);
        k?.SetValue(name, value);
    }

    public void DeleteValue(string key, string name)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>
/// Instalacion de la extension en este PC (%LOCALAPPDATA%\sOCCredentials, registro de HKCU y el
/// navegador de verdad). La logica esta en ExtensionInstallerCore.
/// </summary>
public static class ExtensionInstaller
{
    public const string HostName = ExtensionInstallerCore.HostName;
    public const string EdgeStoreUrl = ExtensionInstallerCore.EdgeStoreUrl;
    public const string FirefoxStoreUrl = ExtensionInstallerCore.FirefoxStoreUrl;

    internal static readonly ExtensionInstallerCore Core = new(new WindowsRegistry(),
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCCredentials"),
        AppContext.BaseDirectory, () => Environment.GetEnvironmentVariable("SOC_LAUNCHER"), () => Environment.ProcessPath,
        (exe, url) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, url) { UseShellExecute = true }));

    public static Browser[] Known => ExtensionInstallerCore.Known;
    public static string Root => Core.Root;
    public static string ExtensionDir(bool firefox) => Core.ExtensionDir(firefox);
    public static bool Available => Core.Available;
    public static List<Browser> Detected() => Core.Detected();
    public static bool IsRegistered(Browser b) => Core.IsRegistered(b);
    public static void Install(Browser b) => Core.Install(b);
    public static void RegisterForInstalledBrowsers() => Core.RegisterForInstalledBrowsers();
    public static void OpenExtensionsPage(Browser b) => Core.OpenExtensionsPage(b);
}

/// <summary>La parte con pantalla (ExtensionSetupFlow) sobre los dialogos de la pagina.</summary>
public static class ExtensionSetup
{
    private static readonly ExtensionSetupFlow Flow = new(ExtensionInstaller.Core);

    public static Task OfferAfterUnlockAsync(Page page, ISettingsService settings, ILocalizationService l) => Flow.OfferAfterUnlockAsync(new PagePrompts(page), settings, l);

    public static Task InstallAsync(Page page, Browser b, ILocalizationService l) => Flow.InstallAsync(new PagePrompts(page), b, l);

    private sealed class PagePrompts(Page page) : IExtensionPrompts
    {
        public Task<string?> ActionSheetAsync(string title, string cancel, params string[] options) => SocShared.ModernDialog.ActionSheetAsync(page, title, cancel, options);

        public Task<bool> AlertAsync(string title, string message, string accept, string? cancel = null) => SocShared.ModernDialog.AlertAsync(page, title, message, accept, cancel);

        public Task CopyToClipboardAsync(string text) => Clipboard.Default.SetTextAsync(text);
    }
}
