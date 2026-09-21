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
