using Microsoft.Extensions.Logging;
using Credentials.Helpers;
using Credentials.Services;
using ZXing.Net.Maui.Controls;

namespace Credentials;

public static partial class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Gestor global de excepciones (constitucion General 6.12): lo primero, antes de nada que pueda fallar.
        CrashReporting.Install();

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
        // Lo que da el sistema, detras de interfaces para poder probar las paginas con dobles.
        builder.Services.AddSingleton<IDialogService, ModernDialogService>();
        builder.Services.AddSingleton<INavigationService, ShellNavigationService>();
        builder.Services.AddSingleton<IQrScanner, Pages.CameraQrScanner>();
        builder.Services.AddSingleton<ITextFilePicker, TextFilePicker>();
        builder.Services.AddSingleton(Clipboard.Default);
        builder.Services.AddSingleton(Share.Default);
        builder.Services.AddSingleton(Browser.Default);
        builder.Services.AddSingleton(Email.Default);
        builder.Services.AddSingleton(AppInfo.Current);
        builder.Services.AddSingleton(DeviceInfo.Current);
        builder.Services.AddSingleton(FileSystem.Current);
        // Sin plataforma (el destino net10.0, el de las pruebas): sin biometria, sin navegador de
        // entrada y sin nada de Windows ni de Android. Cada plataforma pone lo suyo encima
        // (Platforms\*\MauiProgram.*.cs): el ultimo registro es el que vale.
        builder.Services.AddSingleton<IBiometric, NoBiometric>();
        builder.Services.AddSingleton<IOAuthBrowser, NoOAuthBrowser>();
        builder.Services.AddSingleton<IDesktopIntegration, NoDesktopIntegration>();
        builder.Services.AddSingleton<IAndroidIntegration, NoAndroidIntegration>();
        RegisterPlatformServices(builder.Services);

        // Las paginas (carpeta Pages) las instancia el Shell por DataTemplate y resuelven sus
        // servicios via ServiceHelper (constitucion 7: code-behind delgado, sin ViewModels).

        var app = builder.Build();
        ServiceHelper.Initialize(app.Services);
        return app;
    }

    /// <summary>Los servicios de la plataforma (en Windows y en Android; sin plataforma no hay).</summary>
    static partial void RegisterPlatformServices(IServiceCollection services);
}
