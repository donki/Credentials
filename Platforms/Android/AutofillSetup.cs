using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views.Autofill;
using Credentials.Services;

namespace Credentials.Platforms.Android;

/// <summary>
/// Que sOC Credentials sea el servicio de autocompletar del dispositivo. Android solo admite uno:
/// al elegir este se desconecta el otro gestor (Google, Samsung Pass, Bitwarden…). Se ofrece al
/// desbloquear (una vez por sesion, y el usuario puede pedir que no se le pregunte mas) y desde
/// Ajustes › Autocompletar.
/// </summary>
public static class AutofillSetup
{
    private static bool _offeredThisSession;

    public static bool Supported
    {
        get
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
                return false;
            try { return Manager()?.IsAutofillSupported == true; }
            catch (Exception) { return false; }
        }
    }

    /// <summary>True si el servicio de autocompletar activo es esta aplicacion.</summary>
    public static bool IsOurs
    {
        get
        {
            try { return Manager()?.HasEnabledAutofillServices == true; }
            catch (Exception) { return false; }
        }
    }

    /// <summary>
    /// Android 14 en adelante tiene, ademas del servicio de autocompletar, un «servicio preferido de
    /// contraseñas y claves de acceso» (Credential Manager). Los navegadores Chromium (Chrome, Edge)
    /// hacen caso a ese, no al de autocompletar: mientras ahi este Google, en el navegador saldra
    /// Google aunque aqui el autocompletar sea este.
    /// </summary>
    public static bool HasPreferredService => Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake;

    /// <summary>Abre la pantalla del sistema donde se elige el gestor de contraseñas preferido.</summary>
    public static bool OpenPreferredService()
    {
        var context = global::Android.App.Application.Context;
        // La pantalla se llama distinto segun la version y la capa del fabricante: se prueban por orden.
        foreach (var action in new[] { "android.settings.CREDENTIAL_PROVIDER", Settings.ActionRequestSetAutofillService })
        {
            try
            {
                var intent = new Intent(action);
                intent.SetData(global::Android.Net.Uri.Parse("package:" + context.PackageName));
                intent.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(intent);
                return true;
            }
            catch (Exception) { }
        }
        return false;
    }

    /// <summary>Abre los ajustes de un navegador (su ficha de aplicacion): desde ahi se llega a los suyos.</summary>
    public static bool OpenBrowserSettings(string package)
    {
        var context = global::Android.App.Application.Context;
        try
        {
            var intent = new Intent(Settings.ActionApplicationDetailsSettings,
                global::Android.Net.Uri.Parse("package:" + package));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Los navegadores Chromium instalados (los que tienen su propio gestor de contraseñas).</summary>
    public static IReadOnlyList<(string Package, string Name)> InstalledBrowsers()
    {
        var known = new (string Package, string Name)[]
        {
            ("com.microsoft.emmx", "Microsoft Edge"),
            ("com.android.chrome", "Google Chrome"),
            ("com.brave.browser", "Brave"),
            ("org.mozilla.firefox", "Firefox"),
        };
        var context = global::Android.App.Application.Context;
        var list = new List<(string, string)>();
        foreach (var b in known)
        {
            try
            {
                context.PackageManager?.GetPackageInfo(b.Package, 0);
                list.Add(b);
            }
            catch (Exception) { /* no esta instalado */ }
        }
        return list;
    }

    /// <summary>Abre el dialogo del sistema que pregunta si usar sOC Credentials para autocompletar.</summary>
    public static void Request()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(Settings.ActionRequestSetAutofillService);
        intent.SetData(global::Android.Net.Uri.Parse("package:" + context.PackageName));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    /// <summary>Tras desbloquear: si otro gestor (o ninguno) rellena las contraseñas, se propone cambiar a este.</summary>
    public static async Task OfferAfterUnlockAsync(Page page, ISettingsService settings, ILocalizationService l)
    {
        if (_offeredThisSession || !settings.AskAutofill || !Supported || IsOurs)
            return;
        _offeredThisSession = true;
        var choice = await SocShared.ModernDialog.ActionSheetAsync(page, l["AutofillOfferTitle"], l["NotNow"], l["AutofillUseThis"], l["ExtDontAsk"]);
        if (choice == l["ExtDontAsk"])
        {
            settings.AskAutofill = false;
            return;
        }
        if (choice == l["AutofillUseThis"])
            Request();
    }

    private static AutofillManager? Manager()
    {
        var context = global::Android.App.Application.Context;
        return context.GetSystemService(Java.Lang.Class.FromType(typeof(AutofillManager))) as AutofillManager;
    }
}
