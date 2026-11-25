using Credentials.Services;

namespace Credentials;

public static partial class MauiProgram
{
    /// <summary>Windows: aviso en la pagina, navegador del sistema para entrar en la nube y el escritorio.</summary>
    static partial void RegisterPlatformServices(IServiceCollection services)
    {
        services.AddSingleton<IToastService, Platforms.Windows.ToastService>();
        services.AddSingleton<IOAuthBrowser, Platforms.Windows.OAuthBrowser>();
        services.AddSingleton<IDesktopHost, Platforms.Windows.DesktopHost>();
        services.AddSingleton<IDesktopIntegration, DesktopIntegration>();
    }
}
