using Credentials.Services;

namespace Credentials;

public static partial class MauiProgram
{
    /// <summary>Android: toast del sistema, huella, navegador de entrada y lo propio del dispositivo.</summary>
    static partial void RegisterPlatformServices(IServiceCollection services)
    {
        services.AddSingleton<IToastService, Platforms.Android.ToastService>();
        services.AddSingleton<IBiometric, Platforms.Android.Biometric>();
        services.AddSingleton<IOAuthBrowser, Platforms.Android.OAuthBrowser>();
        services.AddSingleton<IAndroidIntegration, Platforms.Android.AndroidIntegration>();
    }
}
