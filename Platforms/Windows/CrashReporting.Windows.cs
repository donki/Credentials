using SocShared;

namespace Credentials.Services;

public static partial class CrashReporting
{
    /// <summary>En Windows la Application de WinUI ya existe al arrancar MAUI: sus errores sin atender tambien.</summary>
    static partial void HookPlatform() => HookWinUI(() => Microsoft.UI.Xaml.Application.Current);

    /// <summary>Engancha el error sin atender de la Application de WinUI que se le da (si la hay).</summary>
    internal static void HookWinUI(Func<Microsoft.UI.Xaml.Application?> current)
    {
        try
        {
            if (current() is { } winApp)
                winApp.UnhandledException += (_, e) => OnWinUIError(e);
        }
        catch (Exception ex)
        {
            CrashGuard.Log(new RedactedException(ex), "CrashReporting.Install (Windows)");
        }
    }

    internal static void OnWinUIError(Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        CrashGuard.Report(e.Exception is null ? null : new RedactedException(e.Exception), "Microsoft.UI.Xaml.Application.UnhandledException");
    }
}
