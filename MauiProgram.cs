using Microsoft.Extensions.Logging;
using Credentials.Helpers;
using Credentials.Services;
using ZXing.Net.Maui.Controls;

namespace Credentials;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        // Sin fuentes propias: se usa la tipografia del sistema (constitucion A.9).
        builder.UseMauiApp<App>().UseBarcodeReader();

#if DEBUG
        // Trazas de depuracion solo en Debug (constitucion 10).
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#else
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
#endif

        // Servicios (constitucion 5: inyeccion de dependencias para todos los servicios).
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
        builder.Services.AddSingleton<VaultStore>();
#if ANDROID
        builder.Services.AddSingleton<IToastService, Platforms.Android.ToastService>();
        builder.Services.AddSingleton<IBiometric, Platforms.Android.Biometric>();
        builder.Services.AddSingleton<IOAuthBrowser, Platforms.Android.OAuthBrowser>();
#elif WINDOWS
        builder.Services.AddSingleton<IToastService, Platforms.Windows.ToastService>();
        builder.Services.AddSingleton<IBiometric, Platforms.Windows.Biometric>();
        builder.Services.AddSingleton<IOAuthBrowser, Platforms.Windows.OAuthBrowser>();
#endif

        // Las paginas (carpeta Pages) las instancia el Shell por DataTemplate y resuelven sus
        // servicios via ServiceHelper (constitucion 7: code-behind delgado, sin ViewModels).

        var app = builder.Build();
        ServiceHelper.Initialize(app.Services);
        return app;
    }
}
