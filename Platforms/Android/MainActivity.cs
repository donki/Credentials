using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;
using AndroidView = Android.Views.View;

namespace Credentials;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private ScreenOffReceiver? _screenOff;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ApplySystemBarInsets();
        // Al apagarse la pantalla (bloqueo del movil) la boveda se cierra: es el «bloqueo de sesion»
        // del telefono. Mientras la pantalla siga encendida solo cuenta la inactividad.
        _screenOff = new ScreenOffReceiver();
        RegisterReceiver(_screenOff, new IntentFilter(Intent.ActionScreenOff));
#if DEBUG
        // Solo en Debug: extras del intent para probar y capturar sin teclear (Helpers.DemoData).
        if (Intent?.GetStringExtra("master") is { } master)
        {
            var demo = Intent.GetBooleanExtra("demo", false);
            var page = Intent.GetStringExtra("page");
            var lang = Intent.GetStringExtra("lang");
            Microsoft.Maui.Controls.Application.Current!.Dispatcher.Dispatch(async () =>
            {
                await Task.Delay(1500);
                await Helpers.DemoData.ApplyAsync(master, demo, page, lang);
            });
        }
#endif
    }

    protected override void OnDestroy()
    {
        if (_screenOff is not null)
        {
            try { UnregisterReceiver(_screenOff); } catch (Exception) { }
            _screenOff = null;
        }
        base.OnDestroy();
    }

    private sealed class ScreenOffReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != Intent.ActionScreenOff)
                return;
            try
            {
                if (!Helpers.ServiceHelper.GetRequiredService<Services.ISettingsService>().TrustDevice)
                    Helpers.ServiceHelper.GetRequiredService<Services.VaultStore>().Lock();
            }
            catch (Exception) { }
        }
    }

    /// <summary>Al volver a primer plano se comprueba el bloqueo por inactividad: la boveda no espera al siguiente toque.</summary>
    protected override void OnResume()
    {
        base.OnResume();
        try { Helpers.ServiceHelper.GetRequiredService<Services.VaultStore>().LockIfIdle(); } catch (Exception) { }
    }

    /// <summary>Sin capturas de pantalla ni miniatura en «recientes»: lo que hay en pantalla son contraseñas.</summary>
    protected override void OnStart()
    {
        base.OnStart();
#if !DEBUG
        // En Debug se deja capturar (pantallas para las tiendas, con datos inventados).
        Window?.SetFlags(global::Android.Views.WindowManagerFlags.Secure, global::Android.Views.WindowManagerFlags.Secure);
#endif
    }

    /// <summary>
    /// Desde Android 15 el sistema dibuja la aplicacion de borde a borde y las barras de
    /// estado y navegacion quedan por encima del contenido. Se separa el contenido con el
    /// tamano real de esas barras y se pinta el hueco con el teal de la marca.
    /// </summary>
    private void ApplySystemBarInsets()
    {
        var content = FindViewById(global::Android.Resource.Id.Content);
        if (content is null)
            return;

        content.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#2A1CB8"));

        ViewCompat.SetOnApplyWindowInsetsListener(content, new SystemBarInsetsListener());

        // Iconos claros sobre el teal de la marca.
        var controller = Window is not null
            ? WindowCompat.GetInsetsController(Window, Window.DecorView)
            : null;

        if (controller is not null)
        {
            controller.AppearanceLightStatusBars = false;
            controller.AppearanceLightNavigationBars = false;
        }
    }

    private class SystemBarInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(AndroidView? view, WindowInsetsCompat? insets)
        {
            // Los insets se consumen siempre: ninguna vista hija debe volver a aplicarlos.
            var consumed = WindowInsetsCompat.Consumed!;

            if (view is null || insets is null)
                return consumed;

            var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout());
            if (bars is not null)
                view.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);

            return consumed;
        }
    }
}
