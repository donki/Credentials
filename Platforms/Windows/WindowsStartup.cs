namespace Credentials.Platforms.Windows;

/// <summary>
/// «Arrancar con Windows»: una entrada en HKCU\…\Run con el lanzador (o el exe, si se corre suelto)
/// y el argumento --tray, para que arranque escondido en la bandeja. Solo el usuario actual y sin
/// tocar nada del sistema; se quita borrando el valor. La logica esta en StartupRegistration.
/// </summary>
internal static class WindowsStartup
{
    private static readonly StartupRegistration Registration = new(new WindowsRegistry(), () => ExecutablePath);

    /// <summary>El exe que hay que arrancar: el lanzador que abrio la aplicacion, o el propio exe.</summary>
    public static string ExecutablePath =>
        AppExecutable.Resolve(Environment.GetEnvironmentVariable("SOC_LAUNCHER"), Environment.ProcessPath, File.Exists) ?? string.Empty;

    public static bool IsEnabled(string valueName) => Registration.IsEnabled(valueName);

    public static void Set(string valueName, bool enabled) => Registration.Set(valueName, enabled);
}
