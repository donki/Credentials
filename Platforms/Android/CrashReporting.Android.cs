using SocShared;

namespace Credentials.Services;

public static partial class CrashReporting
{
    /// <summary>Android: los errores de Java que llegan al runtime de .NET.</summary>
    static partial void HookPlatform()
    {
        global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
        {
            e.Handled = true;
            CrashGuard.Report(new RedactedException(e.Exception), "AndroidEnvironment.UnhandledExceptionRaiser");
        };
    }
}
