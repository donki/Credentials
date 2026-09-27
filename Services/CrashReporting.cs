using System.Text;
using SocShared;

namespace Credentials.Services;

/// <summary>
/// Gestor global de excepciones (constitucion General 6.12) con la pieza comun
/// <c>SocShared.CrashGuard</c>: un error inesperado no cierra la aplicacion, se registra y se avisa
/// al usuario en su idioma, sin ningun detalle del error, y la aplicacion sigue.
///
/// Por que no <c>CrashGuard.Install</c> tal cual: esto es un gestor de contraseñas y el mensaje de una
/// excepcion puede llevar dentro lo que se estaba tratando (p. ej. «The input string 'xxx' was not in a
/// correct format»). Los ganchos son los mismos que pone Install, pero a CrashGuard solo le llega una
/// copia <see cref="RedactedException"/> del error: tipos y pila de llamadas, NUNCA los mensajes ni los
/// datos. Asi ni crash.log ni logcat guardan contraseñas, claves o contenido de la boveda.
/// </summary>
public static class CrashReporting
{
    private static int _installed;

    /// <summary>Primera linea de <c>MauiProgram.CreateMauiApp</c> (en Windows la Application de WinUI ya existe).</summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1)
            return;

        // El aviso con los textos de la aplicacion (idioma elegido en Ajustes, cambio en caliente).
        CrashGuard.Alert = async (page, title, message, ok) =>
        {
            try
            {
                var l = Helpers.ServiceHelper.GetRequiredService<ILocalizationService>();
                (title, message, ok) = (l["CrashTitle"], l["CrashText"], l["Ok"]);
            }
            catch (Exception) { /* sin servicios todavia: el texto por defecto de CrashGuard */ }
            await ModernDialog.AlertAsync(page, title, message, ok);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            // No se puede frenar (el proceso ya se va): solo registrar.
            CrashGuard.Log(e.ExceptionObject is Exception ex ? new RedactedException(ex) : null, "AppDomain.UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            CrashGuard.Report(new RedactedException(e.Exception), "TaskScheduler.UnobservedTaskException");
        };

#if ANDROID
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
        {
            e.Handled = true;
            CrashGuard.Report(new RedactedException(e.Exception), "AndroidEnvironment.UnhandledExceptionRaiser");
        };
#endif

#if WINDOWS
        try
        {
            if (Microsoft.UI.Xaml.Application.Current is { } winApp)
            {
                winApp.UnhandledException += (_, e) =>
                {
                    e.Handled = true;
                    CrashGuard.Report(e.Exception is null ? null : new RedactedException(e.Exception), "Microsoft.UI.Xaml.Application.UnhandledException");
                };
            }
        }
        catch (Exception ex)
        {
            CrashGuard.Log(new RedactedException(ex), "CrashReporting.Install (Windows)");
        }
#endif

        string version;
        try { version = AppInfo.Current.VersionString; } catch (Exception) { version = "?"; }
        CrashGuard.Info($"sOC Credentials {version} arrancada; gestor de excepciones enganchado.");
    }

    /// <summary>
    /// El error sin nada que pueda ser un dato: por cada excepcion de la cadena (internas y las de un
    /// AggregateException), su tipo, su HResult y su pila de llamadas. Los mensajes no se copian.
    /// </summary>
    public sealed class RedactedException(Exception original) : Exception("(mensaje omitido: puede llevar datos de la boveda)")
    {
        public override string ToString()
        {
            var sb = new StringBuilder();
            Append(sb, original, 0);
            return sb.ToString();
        }

        public override string? StackTrace => original.StackTrace;

        private static void Append(StringBuilder sb, Exception ex, int depth)
        {
            if (depth > 8)
                return;
            if (depth > 0)
                sb.AppendLine().Append(new string(' ', depth * 2)).Append("---> ");
            sb.Append(ex.GetType().FullName).Append(" (HResult 0x").Append(ex.HResult.ToString("X8")).Append(')');
            if (ex.StackTrace is { Length: > 0 } stack)
                sb.AppendLine().Append(stack);
            if (ex is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                    Append(sb, inner, depth + 1);
            }
            else if (ex.InnerException is { } inner)
            {
                Append(sb, inner, depth + 1);
            }
        }
    }
}
