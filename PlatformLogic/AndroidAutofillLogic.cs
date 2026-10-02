using Credentials.Models;
using Credentials.Services;

namespace Credentials.Platforms.Android;

/// <summary>
/// Un nodo de la pantalla que Android pasa al autocompletar (AssistStructure.ViewNode), con lo justo
/// para reconocer los campos. En la aplicacion lo envuelve CredentialsAutofillService; en las
/// pruebas es un arbol en memoria.
/// </summary>
public interface IViewNodeInfo
{
    string? WebDomain { get; }

    /// <summary>Tiene AutofillId (se puede rellenar).</summary>
    bool HasAutofillId { get; }

    bool IsVisible { get; }

    /// <summary>El texto escrito, si su valor de autocompletar es de texto; null si no.</summary>
    string? TextValue { get; }

    IReadOnlyList<string>? AutofillHints { get; }

    /// <summary>InputType de Android (clase y variacion).</summary>
    int InputType { get; }

    /// <summary>La etiqueta HTML (en las webs), o null.</summary>
    string? HtmlTag { get; }

    /// <summary>Los atributos HTML (en las webs), o null si el nodo no es HTML.</summary>
    IReadOnlyList<(string? Name, string? Value)>? HtmlAttributes { get; }

    string? IdEntry { get; }

    string? Hint { get; }

    IEnumerable<IViewNodeInfo?> Children { get; }
}

/// <summary>Los campos de usuario y contraseña de la pantalla, y de que web o app son.</summary>
public sealed class FoundFields
{
    public IViewNodeInfo? User;
    public IViewNodeInfo? Pass;
    public string? WebDomain;
    public string? Package;
}

/// <summary>Lo que el usuario ha escrito en los campos de usuario y contraseña.</summary>
public sealed class TypedCredentials
{
    public string Username = string.Empty;
    public string Password = string.Empty;
    public string? WebDomain;
    public string? Package;

    /// <summary>Lo que viene en los extras del intent de AutofillSaveActivity (vacio = sin dato).</summary>
    public static TypedCredentials FromExtras(string? user, string? pass, string? domain, string? package) => new()
    {
        Username = user ?? string.Empty,
        Password = pass ?? string.Empty,
        WebDomain = string.IsNullOrEmpty(domain) ? null : domain,
        Package = string.IsNullOrEmpty(package) ? null : package,
    };
}

/// <summary>Que contestar a una peticion de autocompletar.</summary>
public enum FillKind
{
    /// <summary>No hay campos de usuario ni contraseña: nada.</summary>
    None,

    /// <summary>Boveda bloqueada: una sugerencia que abre la aplicacion para desbloquear.</summary>
    Locked,

    /// <summary>Las entradas que casan (puede no haber ninguna: igual se pide guardar).</summary>
    Offer,
}

/// <summary>Que hacer cuando Android pide guardar lo escrito.</summary>
public enum SaveKind { Nothing, SaveNow, NeedUnlock }

/// <summary>Los campos a vigilar para ofrecer guardar y el tipo de datos (valores de SaveInfo.SAVE_DATA_TYPE_*).</summary>
public sealed record SavePlan(IReadOnlyList<IViewNodeInfo> Required, IReadOnlyList<IViewNodeInfo> Optional, int DataType);

/// <summary>
/// La logica del autocompletar de Android sin Android: reconocer los campos en el arbol de la
/// pantalla, leer lo escrito, decidir que contestar y que guardar. CredentialsAutofillService solo
/// traduce entre esto y las clases del sistema.
/// </summary>
public static class AndroidAutofillLogic
{
    // InputType de Android.
    public const int MaskVariation = 0x00000ff0;
    public const int TextVariationEmailAddress = 0x00000020;
    public const int TextVariationPassword = 0x00000080;
    public const int TextVariationVisiblePassword = 0x00000090;
    public const int TextVariationWebEmailAddress = 0x000000d0;
    public const int TextVariationWebPassword = 0x000000e0;
    public const int NumberVariationPassword = 0x00000010;

    // SaveInfo.SAVE_DATA_TYPE_*.
    public const int SaveTypeGeneric = 0x0, SaveTypePassword = 0x1, SaveTypeUsername = 0x8;

    /// <summary>Cuantas sugerencias se ofrecen como mucho.</summary>
    public const int MaxDatasets = 8;

    public static FoundFields FindFields(IEnumerable<IViewNodeInfo?> roots, string? package)
    {
        var fields = new FoundFields { Package = package };
        foreach (var root in roots)
            Walk(root, fields);
        return fields;
    }

    private static void Walk(IViewNodeInfo? node, FoundFields fields)
    {
        if (node is null)
            return;
        if (!string.IsNullOrEmpty(node.WebDomain))
            fields.WebDomain ??= node.WebDomain;
        if (node.HasAutofillId && node.IsVisible)
        {
            var kind = Classify(node);
            if (kind == 'p' && fields.Pass is null) fields.Pass = node;
            else if (kind == 'u' && fields.User is null) fields.User = node;
        }
        foreach (var child in node.Children)
            Walk(child, fields);
    }

    /// <summary>Lo que el usuario ha escrito en los campos de usuario y contraseña de la pantalla.</summary>
    public static TypedCredentials ReadTyped(IEnumerable<IViewNodeInfo?> roots, string? package)
    {
        var typed = new TypedCredentials { Package = package };
        foreach (var root in roots)
            WalkTyped(root, typed);
        return typed;
    }

    private static void WalkTyped(IViewNodeInfo? node, TypedCredentials typed)
    {
        if (node is null)
            return;
        if (!string.IsNullOrEmpty(node.WebDomain))
            typed.WebDomain ??= node.WebDomain;
        if (node.HasAutofillId && node.TextValue is { } text)
        {
            var kind = Classify(node);
            if (kind == 'p' && typed.Password.Length == 0 && text.Length > 0) typed.Password = text;
            else if (kind == 'u' && typed.Username.Length == 0 && text.Length > 0) typed.Username = text;
        }
        foreach (var child in node.Children)
            WalkTyped(child, typed);
    }

    /// <summary>'u' usuario, 'p' contraseña, ' ' nada: por pistas, tipo de entrada y atributos HTML.</summary>
    public static char Classify(IViewNodeInfo node)
    {
        foreach (var h in node.AutofillHints ?? [])
        {
            var hint = h.ToLowerInvariant();
            if (hint.Contains("password")) return 'p';
            if (hint.Contains("username") || hint.Contains("email") || hint.Contains("phone")) return 'u';
        }
        var variation = node.InputType & MaskVariation;
        if (variation is TextVariationPassword or TextVariationWebPassword or TextVariationVisiblePassword or NumberVariationPassword)
            return 'p';
        if (variation is TextVariationEmailAddress or TextVariationWebEmailAddress)
            return 'u';
        if (node.HtmlAttributes is { } htmlAttributes)
        {
            var attrs = htmlAttributes.Select(a => (a.Name ?? string.Empty).ToLowerInvariant() + "=" + (a.Value ?? string.Empty).ToLowerInvariant()).ToList();
            if (attrs.Contains("type=password")) return 'p';
            if (attrs.Any(a => a.StartsWith("autocomplete=") && (a.Contains("username") || a.Contains("email")))) return 'u';
            if (attrs.Any(a => (a.StartsWith("name=") || a.StartsWith("id=")) && a.Contains("pass"))) return 'p';
            if (attrs.Any(a => (a.StartsWith("name=") || a.StartsWith("id=")) && (a.Contains("user") || a.Contains("email") || a.Contains("login")))) return 'u';
            if (node.HtmlTag == "input" && attrs.Contains("type=email")) return 'u';
        }
        var id = (node.IdEntry ?? string.Empty).ToLowerInvariant();
        var hintText = (node.Hint ?? string.Empty).ToLowerInvariant();
        if (id.Contains("pass") || hintText.Contains("contraseña") || hintText.Contains("password")) return 'p';
        if (id.Contains("user") || id.Contains("email") || id.Contains("login") || hintText.Contains("usuario") || hintText.Contains("correo") || hintText.Contains("email") || hintText.Contains("user")) return 'u';
        return ' ';
    }

    /// <summary>Que campos hay que vigilar para ofrecer guardar: la contraseña es obligatoria; el usuario, si esta.</summary>
    public static SavePlan SavePlanFor(FoundFields fields)
    {
        var required = new List<IViewNodeInfo>();
        var optional = new List<IViewNodeInfo>();
        var type = SaveTypeGeneric;
        if (fields.Pass is not null) { required.Add(fields.Pass); type |= SaveTypePassword; }
        if (fields.User is not null) { (fields.Pass is null ? required : optional).Add(fields.User); type |= SaveTypeUsername; }
        return new SavePlan(required, optional, type);
    }

    /// <summary>El texto de cada sugerencia: «Titulo · usuario» (o solo el titulo).</summary>
    public static string DatasetLabel(Credential entry) => entry.Username.Length > 0 ? $"{entry.Title} · {entry.Username}" : entry.Title;

    /// <summary>Las entradas que se ofrecen para un sitio o app (como mucho <see cref="MaxDatasets"/>).</summary>
    public static List<Credential> Candidates(VaultStore store, string? domain, string? package) =>
        AutofillLogic.Match(store.Data!.Entries, domain, package).Take(MaxDatasets).ToList();

    /// <summary>Que contestar a una peticion de autocompletar (con «Confiar en este dispositivo» la boveda se abre sola).</summary>
    public static async Task<(FillKind Kind, List<Credential> Entries)> DecideFillAsync(VaultStore store, FoundFields fields)
    {
        if (fields.User is null && fields.Pass is null)
            return (FillKind.None, []);
        if (!store.IsUnlocked && !await store.TryTrustedUnlockAsync())
            return (FillKind.Locked, []);
        return (FillKind.Offer, Candidates(store, fields.WebDomain, fields.Package));
    }

    /// <summary>Que hacer con lo escrito cuando Android pide guardarlo.</summary>
    public static SaveKind DecideSave(TypedCredentials typed, VaultStore store) =>
        typed.Password.Length == 0 && typed.Username.Length == 0 ? SaveKind.Nothing
        : store.IsUnlocked ? SaveKind.SaveNow
        : SaveKind.NeedUnlock;

    /// <summary>
    /// Guarda lo escrito (AutofillLogic.UpsertAsync); en las apps el titulo es el nombre de la app.
    /// Devuelve si se guardo algo; si falla (sin red para subir, boveda cerrada en medio), false.
    /// </summary>
    public static async Task<bool> SaveTypedAsync(VaultStore store, TypedCredentials typed, Func<string?, string?> appLabel)
    {
        try
        {
            var web = !string.IsNullOrEmpty(typed.WebDomain);
            return await AutofillLogic.UpsertAsync(store, typed.WebDomain, typed.Package, web ? null : appLabel(typed.Package), typed.Username, typed.Password);
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// La puerta de las actividades del autocompletar: con «Confiar» se abre sola; si no, la
    /// biometria (si esta activada y hay clave guardada) y, si sigue cerrada, la pagina de
    /// contraseña. Devuelve si la boveda queda abierta.
    /// </summary>
    public static async Task<bool> EnsureUnlockedAsync(VaultStore store, ISettingsService settings, IBiometric biometric, ILocalizationService l, Func<Task> showUnlockPage)
    {
        if (!store.IsUnlocked)
        {
            if (!await store.TryTrustedUnlockAsync() && settings.Biometrics && store.HasStoredKey && await biometric.IsAvailableAsync() && await biometric.AuthenticateAsync(l["AppName"], l["BiometricReason"]))
                await store.UnlockWithStoredKeyAsync();
        }
        if (!store.IsUnlocked)
            await showUnlockPage();
        return store.IsUnlocked;
    }

    /// <summary>Al apagarse la pantalla la boveda se cierra, salvo con «Confiar en este usuario y dispositivo».</summary>
    public static void OnScreenOff(ISettingsService settings, VaultStore store)
    {
        if (!settings.TrustDevice)
            store.Lock();
    }
}

/// <summary>Elegir el servicio de autocompletar y el de contraseñas del dispositivo.</summary>
public sealed class AutofillSetupLogic
{
    /// <summary>Los navegadores con su propio gestor de contraseñas que se buscan en el dispositivo.</summary>
    public static readonly (string Package, string Name)[] KnownBrowsers =
    [
        ("com.microsoft.emmx", "Microsoft Edge"),
        ("com.android.chrome", "Google Chrome"),
        ("com.brave.browser", "Brave"),
        ("org.mozilla.firefox", "Firefox"),
    ];

    /// <summary>La pantalla del gestor preferido se llama distinto segun la version y el fabricante: se prueban por orden.</summary>
    public static readonly string[] PreferredServiceActions = ["android.settings.CREDENTIAL_PROVIDER", "android.settings.REQUEST_SET_AUTOFILL_SERVICE"];

    private bool _offeredThisSession;

    public static IReadOnlyList<(string Package, string Name)> Installed(Func<string, bool> isInstalled) =>
        KnownBrowsers.Where(b => isInstalled(b.Package)).ToList();

    /// <summary>Abre la primera que se deje (las que lanzan se saltan).</summary>
    public static bool OpenFirst(IEnumerable<string> actions, Action<string> open)
    {
        foreach (var action in actions)
        {
            try
            {
                open(action);
                return true;
            }
            catch (Exception) { }
        }
        return false;
    }

    /// <summary>Tras desbloquear: si otro gestor (o ninguno) rellena las contraseñas, se propone cambiar a este (una vez por sesion).</summary>
    public async Task OfferAfterUnlockAsync(Func<string, string, string[], Task<string?>> actionSheet, ISettingsService settings, ILocalizationService l, Func<bool> supported, Func<bool> isOurs, Action request)
    {
        if (_offeredThisSession || !settings.AskAutofill || !supported() || isOurs())
            return;
        _offeredThisSession = true;
        var choice = await actionSheet(l["AutofillOfferTitle"], l["NotNow"], [l["AutofillUseThis"], l["ExtDontAsk"]]);
        if (choice == l["ExtDontAsk"])
        {
            settings.AskAutofill = false;
            return;
        }
        if (choice == l["AutofillUseThis"])
            request();
    }
}

/// <summary>La entrada con Google o Microsoft en Android (WebAuthenticator).</summary>
public static class AndroidOAuth
{
    /// <summary>Google exige el identificador de cliente invertido; Microsoft vuelve por el esquema propio.</summary>
    public static string RedirectUri(string providerName, string googleScheme) =>
        providerName == "Google" ? googleScheme + ":/oauth" : "com.socratic.credentials://auth";

    /// <summary>La vuelta con lo que trae el navegador (code, state…) como consulta, para el codigo comun de la nube.</summary>
    public static Uri CallbackWithProperties(Uri callback, IEnumerable<KeyValuePair<string, string>> properties) =>
        new($"{callback}?{string.Join('&', properties.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"))}");
}
