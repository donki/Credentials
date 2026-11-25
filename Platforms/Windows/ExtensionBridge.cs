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
    private readonly string _pipeName;

    /// <summary>Un navegador acaba de saludar por primera vez (para avisar en pantalla).</summary>
    public event Action<string>? BrowserConnected
    {
        add => _handler.BrowserConnected += value;
        remove => _handler.BrowserConnected -= value;
    }

    /// <param name="requestUnlock">El usuario necesita la boveda: traer la ventana y, si esta cerrada, la puerta (DesktopIntegration.RequestUnlock).</param>
    /// <param name="pipeName">La tuberia (las pruebas usan otra, para no cruzarse con la aplicacion abierta).</param>
    /// <param name="onMainThread">Donde se hace lo que toca la boveda (el hilo de la interfaz; las pruebas, el suyo).</param>
    public ExtensionServer(VaultStore store, ISettingsService settings, Action requestUnlock, string pipeName = PipeName, Func<Func<Task>, Task>? onMainThread = null)
    {
        _pipeName = pipeName;
        _handler = new ExtensionRequestHandler(store, settings, onMainThread ?? MainThread.InvokeOnMainThreadAsync, requestUnlock);
    }

    /// <summary>Atiende la tuberia en segundo plano; solo la puede abrir el mismo usuario de Windows.</summary>
    public void Start()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        var loop = new PipeServerLoop(() => NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security), _handler);
        _ = Task.Run(() => loop.RunAsync(_cts.Token));
    }

    public void Stop() => _cts.Cancel();
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

    public void DeleteKey(string key) => Registry.CurrentUser.DeleteSubKey(key, throwOnMissingSubKey: false);
}

/// <summary>
/// Instalacion de la extension en este PC (%LOCALAPPDATA%\sOCCredentials, registro de HKCU y el
/// navegador de verdad). La logica esta en ExtensionInstallerCore y la parte con pantalla en
/// DesktopIntegration.
/// </summary>
public static class ExtensionInstaller
{
    internal static readonly ExtensionInstallerCore Core = new(new WindowsRegistry(),
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCCredentials"),
        AppContext.BaseDirectory, () => Environment.GetEnvironmentVariable("SOC_LAUNCHER"), () => Environment.ProcessPath,
        (exe, url) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, url) { UseShellExecute = true }),
        PackageIdentity.IsPackaged);
}
