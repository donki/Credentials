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
    private static readonly AutofillSetupLogic Offer = new();

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
    public static bool OpenPreferredService() => AutofillSetupLogic.OpenFirst(AutofillSetupLogic.PreferredServiceActions, action => StartSettings(action, global::Android.App.Application.Context.PackageName));

    /// <summary>Abre los ajustes de un navegador (su ficha de aplicacion): desde ahi se llega a los suyos.</summary>
    public static bool OpenBrowserSettings(string package) => AutofillSetupLogic.OpenFirst([Settings.ActionApplicationDetailsSettings], action => StartSettings(action, package));

    /// <summary>Los navegadores Chromium instalados (los que tienen su propio gestor de contraseñas).</summary>
    public static IReadOnlyList<(string Package, string Name)> InstalledBrowsers() => AutofillSetupLogic.Installed(package =>
    {
        try { return global::Android.App.Application.Context.PackageManager?.GetPackageInfo(package, 0) is not null; }
        catch (Exception) { return false; }   // no esta instalado
    });

    /// <summary>Abre el dialogo del sistema que pregunta si usar sOC Credentials para autocompletar.</summary>
    public static void Request() => StartSettings(Settings.ActionRequestSetAutofillService, global::Android.App.Application.Context.PackageName);

    /// <summary>Tras desbloquear: si otro gestor (o ninguno) rellena las contraseñas, se propone cambiar a este.</summary>
    public static Task OfferAfterUnlockAsync(Page page, ISettingsService settings, ILocalizationService l) =>
        Offer.OfferAfterUnlockAsync((title, cancel, options) => SocShared.ModernDialog.ActionSheetAsync(page, title, cancel, options), settings, l, () => Supported, () => IsOurs, Request);

    private static void StartSettings(string action, string? package)
    {
        var intent = new Intent(action, global::Android.Net.Uri.Parse("package:" + package));
        intent.AddFlags(ActivityFlags.NewTask);
        global::Android.App.Application.Context.StartActivity(intent);
    }

    private static AutofillManager? Manager()
    {
        var context = global::Android.App.Application.Context;
        return context.GetSystemService(Java.Lang.Class.FromType(typeof(AutofillManager))) as AutofillManager;
    }
}
