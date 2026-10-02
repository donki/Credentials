using Android.App;
using Android.App.Assist;
using Android.Content;
using Android.OS;
using Android.Service.Autofill;
using Android.Views;
using Android.Views.Autofill;
using Android.Widget;
using Credentials.Models;
using Credentials.Services;

namespace Credentials.Platforms.Android;

/// <summary>
/// Autocompletar del sistema (Android 8+): cuando una app o el navegador enseña un formulario de
/// usuario y contraseña, Android pregunta a este servicio y se ofrecen las entradas de la boveda
/// que casan con el dominio web o con el paquete de la app. Si la boveda esta bloqueada, la
/// sugerencia unica es «Desbloquear sOC Credentials»: abre la puerta y, al volver, rellena.
/// </summary>
/// <remarks>
/// El usuario lo activa en Ajustes › Sistema › Idiomas e introduccion de texto › Servicio de
/// autocompletar. Los campos se reconocen por las pistas de autocompletar (username, password,
/// emailAddress), por el tipo de entrada (contraseña, correo) y, en las webs, por los atributos
/// HTML (type, name, id, autocomplete).
///
/// Guardar: cada respuesta lleva un SaveInfo con los campos de usuario y contraseña, asi que al
/// enviar un formulario (nuevo registro, o una contraseña que no esta en la boveda) Android
/// pregunta «¿Guardar la contraseña en Credentials?». Si el usuario acepta llega OnSaveRequest:
/// con la boveda abierta se guarda al momento; bloqueada, se abre AutofillSaveActivity, que pide
/// desbloquear y guarda.
///
/// Las decisiones estan en AndroidAutofillLogic (PlatformLogic, con pruebas); aqui solo se traduce
/// entre ellas y las clases del sistema.
/// </remarks>
[Service(Name = "com.socratic.credentials.AutofillService", Permission = "android.permission.BIND_AUTOFILL_SERVICE", Exported = true, Label = "Credentials")]
[IntentFilter(["android.service.autofill.AutofillService"])]
[MetaData("android.autofill", Resource = "@xml/autofill_service")]
public class CredentialsAutofillService : global::Android.Service.Autofill.AutofillService
{
    public const string ExtraUserField = "userField";
    public const string ExtraPassField = "passField";

    public override async void OnFillRequest(FillRequest request, CancellationSignal cancellationSignal, FillCallback callback)
    {
        try
        {
            var fields = FindFields(request.FillContexts[^1].Structure);
            var (kind, entries) = await AndroidAutofillLogic.DecideFillAsync(Helpers.ServiceHelper.GetRequiredService<VaultStore>(), fields);
            callback.OnSuccess(kind switch
            {
                FillKind.None => null,
                FillKind.Locked => LockedResponse(fields),
                _ => OfferResponse(this, entries, Id(fields.User), Id(fields.Pass)).SetSaveInfo(SaveInfoFor(fields)).Build(),
            });
        }
        catch (Exception ex)
        {
            callback.OnFailure(ex.Message);
        }
    }

    public override void OnSaveRequest(SaveRequest request, SaveCallback callback)
    {
        try
        {
            var structure = request.FillContexts[^1].Structure;
            var typed = AndroidAutofillLogic.ReadTyped(Roots(structure), structure.ActivityComponent?.PackageName);
            var store = Helpers.ServiceHelper.GetRequiredService<VaultStore>();
            switch (AndroidAutofillLogic.DecideSave(typed, store))
            {
                case SaveKind.Nothing:
                    callback.OnSuccess();
                    return;
                case SaveKind.SaveNow:
                    _ = SaveTypedAsync(this, store, typed);
                    callback.OnSuccess();
                    return;
            }
            // Boveda bloqueada: una actividad que desbloquea y guarda (Android 9+ deja lanzarla desde aqui).
            var intent = new Intent(this, typeof(AutofillSaveActivity));
            intent.PutExtra("user", typed.Username);
            intent.PutExtra("pass", typed.Password);
            intent.PutExtra("domain", typed.WebDomain ?? string.Empty);
            intent.PutExtra("package", typed.Package ?? string.Empty);
            var pending = PendingIntent.GetActivity(this, 1002, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable)!;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
                callback.OnSuccess(pending.IntentSender);
            else
                callback.OnFailure(Helpers.ServiceHelper.GetRequiredService<ILocalizationService>()["Unlock"]);
        }
        catch (Exception ex)
        {
            callback.OnFailure(ex.Message);
        }
    }

    /// <summary>Que campos hay que vigilar para ofrecer guardar (AndroidAutofillLogic.SavePlanFor).</summary>
    private static SaveInfo SaveInfoFor(FoundFields fields)
    {
        var plan = AndroidAutofillLogic.SavePlanFor(fields);
        var builder = new SaveInfo.Builder((SaveDataType)plan.DataType, plan.Required.Select(Id).ToArray()!);
        if (plan.Optional.Count > 0)
            builder.SetOptionalIds(plan.Optional.Select(Id).ToArray()!);
        // En las webs el formulario desaparece al enviarse sin «commit» explicito: que pregunte igual.
        builder.SetFlags(SaveFlags.SaveOnAllViewsInvisible);
        return builder.Build();
    }

    /// <summary>Guarda lo escrito y avisa con un toast.</summary>
    public static async Task SaveTypedAsync(Context context, VaultStore store, TypedCredentials typed)
    {
        if (!await AndroidAutofillLogic.SaveTypedAsync(store, typed, package => AppLabel(context, package)))
            return;
        var l = Helpers.ServiceHelper.GetRequiredService<ILocalizationService>();
        new Handler(Looper.MainLooper!).Post(() => Toast.MakeText(context, l["AutofillSaved"], ToastLength.Short)?.Show());
    }

    private static string? AppLabel(Context context, string? package)
    {
        if (string.IsNullOrEmpty(package))
            return null;
        try { return context.PackageManager!.GetApplicationLabel(context.PackageManager.GetApplicationInfo(package, 0)).ToString(); }
        catch (Exception) { return null; }
    }

    /// <summary>Un dataset por entrada: usuario y contraseña en sus campos, con el nombre de la entrada como texto.</summary>
    public static FillResponse.Builder OfferResponse(Context context, IEnumerable<Credential> entries, AutofillId? user, AutofillId? pass)
    {
        var builder = new FillResponse.Builder();
        foreach (var entry in entries)
        {
            var views = new RemoteViews(context.PackageName, global::Android.Resource.Layout.SimpleListItem1);
            views.SetTextViewText(global::Android.Resource.Id.Text1, AndroidAutofillLogic.DatasetLabel(entry));
            var ds = new Dataset.Builder(views);
            if (user is not null) ds.SetValue(user, AutofillValue.ForText(entry.Username));
            if (pass is not null) ds.SetValue(pass, AutofillValue.ForText(entry.Password));
            builder.AddDataset(ds.Build());
        }
        return builder;
    }

    /// <summary>Boveda bloqueada: una sola sugerencia que abre la aplicacion para desbloquear y vuelve con los datos.</summary>
    private FillResponse LockedResponse(FoundFields fields)
    {
        var views = new RemoteViews(PackageName, global::Android.Resource.Layout.SimpleListItem1);
        views.SetTextViewText(global::Android.Resource.Id.Text1, "sOC Credentials");
        var intent = new Intent(this, typeof(AutofillAuthActivity));
        intent.PutExtra(ExtraUserField, Id(fields.User)?.ToString() ?? string.Empty);
        intent.PutExtra(ExtraPassField, Id(fields.Pass)?.ToString() ?? string.Empty);
        intent.PutExtra("domain", fields.WebDomain ?? string.Empty);
        intent.PutExtra("package", fields.Package ?? string.Empty);
        var pending = PendingIntent.GetActivity(this, 1001, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable)!;
        var ids = new[] { Id(fields.User), Id(fields.Pass) }.OfType<AutofillId>().ToArray();
        return new FillResponse.Builder()
            .SetAuthentication(ids, pending.IntentSender, views)
            .SetSaveInfo(SaveInfoFor(fields))
            .Build();
    }

    // ------------------------------------------------------------------ que campos hay en pantalla

    public static FoundFields FindFields(AssistStructure structure) => AndroidAutofillLogic.FindFields(Roots(structure), structure.ActivityComponent?.PackageName);

    public static AutofillId? Id(IViewNodeInfo? node) => (node as Node)?.ViewNode.AutofillId;

    private static IEnumerable<IViewNodeInfo?> Roots(AssistStructure structure) =>
        Enumerable.Range(0, structure.WindowNodeCount).Select(i => Node.Of(structure.GetWindowNodeAt(i)?.RootViewNode));

    /// <summary>Un ViewNode del sistema visto como IViewNodeInfo.</summary>
    private sealed class Node(AssistStructure.ViewNode node) : IViewNodeInfo
    {
        public static Node? Of(AssistStructure.ViewNode? node) => node is null ? null : new Node(node);
        public AssistStructure.ViewNode ViewNode => node;
        public string? WebDomain => node.WebDomain;
        public bool HasAutofillId => node.AutofillId is not null;
        public bool IsVisible => node.Visibility == ViewStates.Visible;
        public string? TextValue => node.AutofillValue is { IsText: true } value ? value.TextValue?.ToString() ?? string.Empty : null;
        public IReadOnlyList<string>? AutofillHints => node.GetAutofillHints();
        public int InputType => (int)node.InputType;
        public string? HtmlTag => node.HtmlInfo?.Tag;
        public IReadOnlyList<(string? Name, string? Value)>? HtmlAttributes => node.HtmlInfo is { } html ? html.Attributes?.Select(a => (a.First?.ToString(), a.Second?.ToString())).ToList() ?? [] : null;
        public string? IdEntry => node.IdEntry;
        public string? Hint => node.Hint;
        public IEnumerable<IViewNodeInfo?> Children => Enumerable.Range(0, node.ChildCount).Select(i => Of(node.GetChildAt(i)));
    }
}

/// <summary>Lo comun a las dos actividades del autocompletar: la puerta (biometria o la pagina de contraseña).</summary>
internal static class AutofillGate
{
    public static Task<bool> EnsureUnlockedAsync(VaultStore store) =>
        AndroidAutofillLogic.EnsureUnlockedAsync(store, Helpers.ServiceHelper.GetRequiredService<ISettingsService>(), Helpers.ServiceHelper.GetRequiredService<IBiometric>(),
            Helpers.ServiceHelper.GetRequiredService<ILocalizationService>(), ShowUnlockPageAsync);

    private static async Task ShowUnlockPageAsync()
    {
        var page = new Pages.UnlockPage();
        var tcs = new TaskCompletionSource();
        page.Disappearing += (_, _) => tcs.TrySetResult();
        if (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page is { } root)
        {
            await root.Navigation.PushModalAsync(page, animated: false);
            await tcs.Task;
        }
    }
}

/// <summary>
/// La puerta que abre el autocompletar cuando la boveda esta bloqueada: enseña la pagina de
/// desbloqueo y, al entrar, devuelve al sistema las sugerencias para que el usuario elija.
/// </summary>
[Activity(Name = "com.socratic.credentials.AutofillAuth", Theme = "@style/Maui.SplashTheme", Exported = false, ExcludeFromRecents = true, NoHistory = true)]
public class AutofillAuthActivity : MauiAppCompatActivity
{
    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        try
        {
            var store = Helpers.ServiceHelper.GetRequiredService<VaultStore>();
            if (!await AutofillGate.EnsureUnlockedAsync(store))
            {
                SetResult(Result.Canceled);
                Finish();
                return;
            }
            // Los AutofillId se pasan por el intent original de autenticacion (Android los conserva en EXTRA_ASSIST_STRUCTURE).
            var fields = Intent?.GetParcelableExtra(AutofillManager.ExtraAssistStructure) is AssistStructure structure ? CredentialsAutofillService.FindFields(structure) : new FoundFields();
            var candidates = AndroidAutofillLogic.Candidates(store, Intent?.GetStringExtra("domain"), Intent?.GetStringExtra("package"));
            var reply = new Intent();
            reply.PutExtra(AutofillManager.ExtraAuthenticationResult, CredentialsAutofillService.OfferResponse(this, candidates, CredentialsAutofillService.Id(fields.User), CredentialsAutofillService.Id(fields.Pass)).Build());
            SetResult(Result.Ok, reply);
        }
        catch (Exception)
        {
            SetResult(Result.Canceled);
        }
        Finish();
    }
}

/// <summary>
/// Guardar con la boveda bloqueada: el usuario ya dijo que si al aviso del sistema; aqui se pide
/// desbloquear (biometria o contraseña) y se guarda lo escrito.
/// </summary>
[Activity(Name = "com.socratic.credentials.AutofillSave", Theme = "@style/Maui.SplashTheme", Exported = false, ExcludeFromRecents = true, NoHistory = true)]
public class AutofillSaveActivity : MauiAppCompatActivity
{
    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        try
        {
            var store = Helpers.ServiceHelper.GetRequiredService<VaultStore>();
            if (await AutofillGate.EnsureUnlockedAsync(store))
                await CredentialsAutofillService.SaveTypedAsync(this, store, TypedCredentials.FromExtras(
                    Intent?.GetStringExtra("user"), Intent?.GetStringExtra("pass"), Intent?.GetStringExtra("domain"), Intent?.GetStringExtra("package")));
        }
        catch (Exception) { }
        Finish();
    }
}
