using Android.App;
using Android.App.Assist;
using Android.Content;
using Android.OS;
using Android.Service.Autofill;
using Android.Text;
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
/// HTML (type, name, id, autocomplete). No se guarda nada nuevo desde aqui (sin OnSaveRequest de
/// verdad) en esta primera version.
/// </remarks>
[Service(Name = "com.socratic.credentials.AutofillService", Permission = "android.permission.BIND_AUTOFILL_SERVICE", Exported = true, Label = "sOC Credentials")]
[IntentFilter(["android.service.autofill.AutofillService"])]
[MetaData("android.autofill", Resource = "@xml/autofill_service")]
public class CredentialsAutofillService : global::Android.Service.Autofill.AutofillService
{
    public const string ExtraEntryId = "entryId";
    public const string ExtraUserField = "userField";
    public const string ExtraPassField = "passField";

    public override void OnFillRequest(FillRequest request, CancellationSignal cancellationSignal, FillCallback callback)
    {
        try
        {
            var structure = request.FillContexts[^1].Structure;
            var fields = FindFields(structure);
            if (fields.UserId is null && fields.PassId is null)
            {
                callback.OnSuccess(null);
                return;
            }

            var store = Helpers.ServiceHelper.GetRequiredService<VaultStore>();
            if (!store.IsUnlocked)
            {
                callback.OnSuccess(LockedResponse(fields));
                return;
            }
            var candidates = Match(store.Data!.Entries, fields.WebDomain, fields.Package);
            if (candidates.Count == 0)
            {
                callback.OnSuccess(null);
                return;
            }
            var builder = new FillResponse.Builder();
            foreach (var entry in candidates.Take(8))
                builder.AddDataset(Dataset(entry, fields));
            callback.OnSuccess(builder.Build());
        }
        catch (Exception ex)
        {
            callback.OnFailure(ex.Message);
        }
    }

    public override void OnSaveRequest(SaveRequest request, SaveCallback callback) => callback.OnSuccess();

    // ------------------------------------------------------------------ que se rellena

    /// <summary>Un dataset por entrada: usuario y contraseña en sus campos, con el nombre de la entrada como texto.</summary>
    private Dataset Dataset(Credential entry, Fields fields)
    {
        var views = new RemoteViews(PackageName, global::Android.Resource.Layout.SimpleListItem1);
        views.SetTextViewText(global::Android.Resource.Id.Text1, entry.Username.Length > 0 ? $"{entry.Title} · {entry.Username}" : entry.Title);
        var builder = new Dataset.Builder(views);
        if (fields.UserId is not null)
            builder.SetValue(fields.UserId, AutofillValue.ForText(entry.Username));
        if (fields.PassId is not null)
            builder.SetValue(fields.PassId, AutofillValue.ForText(entry.Password));
        return builder.Build();
    }

    /// <summary>Boveda bloqueada: una sola sugerencia que abre la aplicacion para desbloquear y vuelve con los datos.</summary>
    private FillResponse LockedResponse(Fields fields)
    {
        var views = new RemoteViews(PackageName, global::Android.Resource.Layout.SimpleListItem1);
        views.SetTextViewText(global::Android.Resource.Id.Text1, "🔒 sOC Credentials");
        var intent = new Intent(this, typeof(AutofillAuthActivity));
        intent.PutExtra(ExtraUserField, fields.UserId?.ToString() ?? string.Empty);
        intent.PutExtra(ExtraPassField, fields.PassId?.ToString() ?? string.Empty);
        intent.PutExtra("domain", fields.WebDomain ?? string.Empty);
        intent.PutExtra("package", fields.Package ?? string.Empty);
        var pending = PendingIntent.GetActivity(this, 1001, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable)!;
        var ids = new List<AutofillId>();
        if (fields.UserId is not null) ids.Add(fields.UserId);
        if (fields.PassId is not null) ids.Add(fields.PassId);
        return new FillResponse.Builder()
            .SetAuthentication(ids.ToArray(), pending.IntentSender, views)
            .Build();
    }

    /// <summary>Entradas que casan con el dominio (web) o con el paquete (app).</summary>
    public static List<Credential> Match(IEnumerable<Credential> entries, string? domain, string? package)
    {
        var live = entries.Where(e => !e.Deleted && e.Kind is EntryKind.Login or EntryKind.App && (e.Username.Length > 0 || e.Password.Length > 0)).ToList();
        if (!string.IsNullOrEmpty(domain))
        {
            var d = domain.ToLowerInvariant();
            var byHost = live.Where(e => e.Host.Length > 0 && (d.EndsWith(e.Host.ToLowerInvariant(), StringComparison.Ordinal) || e.Host.ToLowerInvariant().EndsWith(d, StringComparison.Ordinal))).ToList();
            if (byHost.Count > 0)
                return byHost.OrderByDescending(e => e.Favorite).ThenBy(e => e.Title).ToList();
        }
        if (!string.IsNullOrEmpty(package))
        {
            // com.twitter.android → «twitter»: se busca en la URL, el titulo y un campo extra «android».
            var parts = package.Split('.').Where(p => p.Length > 3 && p is not ("com" or "org" or "net" or "android" or "app" or "mobile")).ToList();
            var byApp = live.Where(e =>
                e.Fields.Any(f => f.Name.Equals("android", StringComparison.OrdinalIgnoreCase) && f.Value.Equals(package, StringComparison.OrdinalIgnoreCase)) ||
                parts.Any(p => e.Host.Contains(p, StringComparison.OrdinalIgnoreCase) || e.Title.Contains(p, StringComparison.OrdinalIgnoreCase))).ToList();
            return byApp.OrderByDescending(e => e.Favorite).ThenBy(e => e.Title).ToList();
        }
        return [];
    }

    // ------------------------------------------------------------------ que campos hay en pantalla

    public sealed class Fields
    {
        public AutofillId? UserId;
        public AutofillId? PassId;
        public string? WebDomain;
        public string? Package;
    }

    public static Fields FindFields(AssistStructure structure)
    {
        var fields = new Fields { Package = structure.ActivityComponent?.PackageName };
        for (var i = 0; i < structure.WindowNodeCount; i++)
            Walk(structure.GetWindowNodeAt(i).RootViewNode, fields);
        return fields;
    }

    private static void Walk(AssistStructure.ViewNode? node, Fields fields)
    {
        if (node is null)
            return;
        if (!string.IsNullOrEmpty(node.WebDomain))
            fields.WebDomain ??= node.WebDomain;
        if (node.AutofillId is not null && node.Visibility == ViewStates.Visible)
        {
            var kind = Classify(node);
            if (kind == 'p' && fields.PassId is null) fields.PassId = node.AutofillId;
            else if (kind == 'u' && fields.UserId is null) fields.UserId = node.AutofillId;
        }
        for (var i = 0; i < node.ChildCount; i++)
            Walk(node.GetChildAt(i), fields);
    }

    /// <summary>'u' usuario, 'p' contraseña, ' ' nada: por pistas, tipo de entrada y atributos HTML.</summary>
    private static char Classify(AssistStructure.ViewNode node)
    {
        var hints = node.GetAutofillHints() ?? [];
        foreach (var h in hints)
        {
            var hint = h.ToLowerInvariant();
            if (hint.Contains("password")) return 'p';
            if (hint.Contains("username") || hint.Contains("email") || hint.Contains("phone")) return 'u';
        }
        var type = node.InputType;
        var variation = (int)type & (int)InputTypes.MaskVariation;
        if (variation is (int)InputTypes.TextVariationPassword or (int)InputTypes.TextVariationWebPassword or (int)InputTypes.TextVariationVisiblePassword or (int)InputTypes.NumberVariationPassword)
            return 'p';
        if (variation is (int)InputTypes.TextVariationEmailAddress or (int)InputTypes.TextVariationWebEmailAddress)
            return 'u';
        var html = node.HtmlInfo;
        if (html is not null)
        {
            var attrs = html.Attributes?.Select(a => (a.First?.ToString() ?? string.Empty).ToLowerInvariant() + "=" + (a.Second?.ToString() ?? string.Empty).ToLowerInvariant()).ToList() ?? [];
            if (attrs.Contains("type=password")) return 'p';
            if (attrs.Any(a => a.StartsWith("autocomplete=") && (a.Contains("username") || a.Contains("email")))) return 'u';
            if (attrs.Any(a => (a.StartsWith("name=") || a.StartsWith("id=")) && (a.Contains("pass")))) return 'p';
            if (attrs.Any(a => (a.StartsWith("name=") || a.StartsWith("id=")) && (a.Contains("user") || a.Contains("email") || a.Contains("login")))) return 'u';
            if (html.Tag == "input" && attrs.Contains("type=email")) return 'u';
        }
        var id = (node.IdEntry ?? string.Empty).ToLowerInvariant();
        var hintText = (node.Hint ?? string.Empty).ToLowerInvariant();
        if (id.Contains("pass") || hintText.Contains("contraseña") || hintText.Contains("password")) return 'p';
        if (id.Contains("user") || id.Contains("email") || id.Contains("login") || hintText.Contains("usuario") || hintText.Contains("correo") || hintText.Contains("email") || hintText.Contains("user")) return 'u';
        return ' ';
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
            var l = Helpers.ServiceHelper.GetRequiredService<ILocalizationService>();
            if (!store.IsUnlocked)
            {
                // Primero la biometria, si esta activada; si no, la pagina de contraseña.
                var settings = Helpers.ServiceHelper.GetRequiredService<ISettingsService>();
                var bio = Helpers.ServiceHelper.GetRequiredService<IBiometric>();
                if (settings.Biometrics && store.HasStoredKey && await bio.IsAvailableAsync() && await bio.AuthenticateAsync(l["AppName"], l["BiometricReason"]))
                    await store.UnlockWithStoredKeyAsync();
            }
            if (!store.IsUnlocked)
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
            if (!store.IsUnlocked)
            {
                SetResult(Result.Canceled);
                Finish();
                return;
            }
            var userField = Intent?.GetStringExtra(CredentialsAutofillService.ExtraUserField);
            var passField = Intent?.GetStringExtra(CredentialsAutofillService.ExtraPassField);
            var domain = Intent?.GetStringExtra("domain");
            var package = Intent?.GetStringExtra("package");
            var candidates = CredentialsAutofillService.Match(store.Data!.Entries, domain, package);
            var builder = new FillResponse.Builder();
            var ids = ParseIds(Intent);
            foreach (var entry in candidates.Take(8))
            {
                var views = new RemoteViews(PackageName, global::Android.Resource.Layout.SimpleListItem1);
                views.SetTextViewText(global::Android.Resource.Id.Text1, entry.Username.Length > 0 ? $"{entry.Title} · {entry.Username}" : entry.Title);
                var ds = new Dataset.Builder(views);
                if (ids.User is not null) ds.SetValue(ids.User, AutofillValue.ForText(entry.Username));
                if (ids.Pass is not null) ds.SetValue(ids.Pass, AutofillValue.ForText(entry.Password));
                builder.AddDataset(ds.Build());
            }
            var reply = new Intent();
            reply.PutExtra(AutofillManager.ExtraAuthenticationResult, builder.Build());
            SetResult(Result.Ok, reply);
        }
        catch (Exception)
        {
            SetResult(Result.Canceled);
        }
        Finish();
    }

    /// <summary>Los AutofillId se pasan por el intent original de autenticacion (Android los conserva en EXTRA_ASSIST_STRUCTURE).</summary>
    private (AutofillId? User, AutofillId? Pass) ParseIds(Intent? intent)
    {
        var structure = intent?.GetParcelableExtra(AutofillManager.ExtraAssistStructure) as AssistStructure;
        if (structure is null)
            return (null, null);
        var fields = CredentialsAutofillService.FindFields(structure);
        return (fields.UserId, fields.PassId);
    }
}
