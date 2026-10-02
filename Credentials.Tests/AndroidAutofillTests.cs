using Credentials.Models;
using Credentials.Platforms.Android;
using Credentials.Services;
using static Credentials.Platforms.Android.AndroidAutofillLogic;

namespace Credentials.Tests;

/// <summary>El autocompletar de Android sin Android: el arbol de la pantalla es de mentira.</summary>
public class AndroidAutofillTests
{
    private const string Master = "Correcta Caballo Pila Grapa";

    private sealed class Node : IViewNodeInfo
    {
        public string? WebDomain { get; init; }
        public bool HasAutofillId { get; init; } = true;
        public bool IsVisible { get; init; } = true;
        public string? TextValue { get; init; }
        public IReadOnlyList<string>? AutofillHints { get; init; }
        public int InputType { get; init; }
        public string? HtmlTag { get; init; }
        public IReadOnlyList<(string? Name, string? Value)>? HtmlAttributes { get; init; }
        public string? IdEntry { get; init; }
        public string? Hint { get; init; }
        public List<IViewNodeInfo?> Kids { get; init; } = [];
        public IEnumerable<IViewNodeInfo?> Children => Kids;
    }

    private static Node Html(string tag, params (string?, string?)[] attrs) => new() { HtmlTag = tag, HtmlAttributes = attrs };

    public static TheoryData<string, char> ClassifyCases => new()
    {
        { "hint:password", 'p' }, { "hint:newPassword", 'p' }, { "hint:username", 'u' }, { "hint:emailAddress", 'u' }, { "hint:phone", 'u' }, { "hint:name", ' ' },
        { "type:0x81", 'p' }, { "type:0xe1", 'p' }, { "type:0x91", 'p' }, { "type:0x12", 'p' }, { "type:0x21", 'u' }, { "type:0xd1", 'u' }, { "type:0x1", ' ' },
        { "html:type=password", 'p' }, { "html:autocomplete=username", 'u' }, { "html:autocomplete=email", 'u' }, { "html:autocomplete=off", ' ' },
        { "html:name=passwd", 'p' }, { "html:id=user_pass", 'p' }, { "html:name=login", 'u' }, { "html:id=email", 'u' }, { "html:name=userid", 'u' },
        { "input:type=email", 'u' }, { "html:type=email", ' ' }, { "html:", ' ' },
        { "id:et_password", 'p' }, { "id:username_field", 'u' }, { "id:email", 'u' }, { "id:login_box", 'u' },
        { "label:Contraseña", 'p' }, { "label:Password", 'p' }, { "label:Usuario", 'u' }, { "label:Correo", 'u' }, { "label:Email", 'u' }, { "label:Your user", 'u' }, { "label:Buscar", ' ' },
    };

    [Theory]
    [MemberData(nameof(ClassifyCases))]
    public void Classify_ByHints_InputType_Html_IdAndLabel(string how, char expected)
    {
        var (kind, value) = (how[..how.IndexOf(':')], how[(how.IndexOf(':') + 1)..]);
        var node = kind switch
        {
            "hint" => new Node { AutofillHints = [value] },
            "type" => new Node { InputType = Convert.ToInt32(value, 16) },
            "html" => value.Length == 0 ? Html("input") : Html("div", (value.Split('=')[0].ToUpperInvariant(), value.Split('=')[1].ToUpperInvariant())),
            "input" => Html("input", (value.Split('=')[0], value.Split('=')[1])),
            "id" => new Node { IdEntry = value },
            _ => new Node { Hint = value },
        };
        Assert.Equal(expected, Classify(node));
    }

    [Fact]
    public void Classify_NullAttributeParts_DoNotBreak()
    {
        Assert.Equal(' ', Classify(Html("input", (null, null), ("type", null))));
        Assert.Equal('p', Classify(new Node { AutofillHints = ["foo", "PASSWORD"] }));
    }

    [Fact]
    public void FindFields_FirstVisibleOfEach_AndTheWebDomain()
    {
        var hiddenPass = new Node { IsVisible = false, AutofillHints = ["password"] };
        var noId = new Node { HasAutofillId = false, AutofillHints = ["username"] };
        var user = new Node { AutofillHints = ["username"] };
        var pass = new Node { AutofillHints = ["password"] };
        var tree = new Node
        {
            Kids =
            [
                null,
                new Node { WebDomain = "accounts.example.com", HasAutofillId = false, Kids = [hiddenPass, noId, user, new Node { WebDomain = "otra.com", Kids = [pass, new Node { AutofillHints = ["password"] }] }] },
                new Node { AutofillHints = ["emailAddress"] },
            ],
        };

        var fields = FindFields([tree, null], "com.android.chrome");
        Assert.Same(user, fields.User);
        Assert.Same(pass, fields.Pass);
        Assert.Equal("accounts.example.com", fields.WebDomain);
        Assert.Equal("com.android.chrome", fields.Package);

        var empty = FindFields([], null);
        Assert.Null(empty.User);
        Assert.Null(empty.Pass);
    }

    [Fact]
    public void ReadTyped_TheFirstNonEmptyValues()
    {
        var tree = new Node
        {
            Kids =
            [
                new Node { WebDomain = "example.com", HasAutofillId = false },
                new Node { AutofillHints = ["username"], TextValue = "" },
                new Node { AutofillHints = ["username"], TextValue = "ana" },
                new Node { AutofillHints = ["username"], TextValue = "otra" },
                new Node { AutofillHints = ["password"], TextValue = null },
                new Node { AutofillHints = ["password"], HasAutofillId = false, TextValue = "no" },
                new Node { AutofillHints = ["password"], TextValue = "s3cr3t", IsVisible = false },
                new Node { AutofillHints = ["password"], TextValue = "segunda" },
                new Node { TextValue = "nada" },
                null,
            ],
        };
        var typed = ReadTyped([tree, null], "com.app");
        Assert.Equal("ana", typed.Username);
        Assert.Equal("s3cr3t", typed.Password);
        Assert.Equal("example.com", typed.WebDomain);
        Assert.Equal("com.app", typed.Package);
    }

    [Fact]
    public void SavePlan_PasswordRequired_UserOptional()
    {
        var user = new Node();
        var pass = new Node();
        var both = SavePlanFor(new FoundFields { User = user, Pass = pass });
        Assert.Equal([pass], both.Required);
        Assert.Equal([user], both.Optional);
        Assert.Equal(SaveTypePassword | SaveTypeUsername, both.DataType);

        var onlyUser = SavePlanFor(new FoundFields { User = user });
        Assert.Equal([user], onlyUser.Required);
        Assert.Empty(onlyUser.Optional);
        Assert.Equal(SaveTypeUsername, onlyUser.DataType);

        var onlyPass = SavePlanFor(new FoundFields { Pass = pass });
        Assert.Equal(SaveTypePassword, onlyPass.DataType);
        Assert.Equal(SaveTypeGeneric, SavePlanFor(new FoundFields()).DataType);
    }

    [Fact]
    public void DatasetLabel_TitleAndUser()
    {
        Assert.Equal("GitHub · ana", DatasetLabel(new Credential { Title = "GitHub", Username = "ana" }));
        Assert.Equal("GitHub", DatasetLabel(new Credential { Title = "GitHub" }));
    }

    [Fact]
    public void TypedFromExtras_EmptyMeansNothing()
    {
        var t = TypedCredentials.FromExtras(null, "p", "", "com.app");
        Assert.Equal(("", "p", null, "com.app"), (t.Username, t.Password, t.WebDomain, t.Package));
        var w = TypedCredentials.FromExtras("ana", null, "example.com", null);
        Assert.Equal(("ana", "", "example.com", null), (w.Username, w.Password, w.WebDomain, w.Package));
    }

    [Fact]
    public async Task DecideFill_None_Locked_Offer()
    {
        using var box = Sandbox.Create();
        var user = new Node();
        Assert.Equal(FillKind.None, (await DecideFillAsync(box.Store, new FoundFields())).Kind);
        Assert.Equal(FillKind.Locked, (await DecideFillAsync(box.Store, new FoundFields { User = user })).Kind);

        await box.Store.CreateAsync(Master);
        box.Store.Data!.Entries.AddRange(Enumerable.Range(0, 10).Select(i => new Credential { Title = $"E{i}", Url = "https://example.com", Username = "u", Password = "p" }));
        box.Store.Data.Entries.Add(new Credential { Title = "Banco", Kind = EntryKind.App, Username = "u", Password = "p", Fields = { new CustomField { Name = "android", Value = "es.banco" } } });

        var web = await DecideFillAsync(box.Store, new FoundFields { User = user, WebDomain = "www.example.com" });
        Assert.Equal(FillKind.Offer, web.Kind);
        Assert.Equal(MaxDatasets, web.Entries.Count);
        var app = await DecideFillAsync(box.Store, new FoundFields { Pass = user, Package = "es.banco" });
        Assert.Equal(["Banco"], app.Entries.Select(e => e.Title));
        Assert.Empty((await DecideFillAsync(box.Store, new FoundFields { Pass = user, WebDomain = "nada.org" })).Entries);
    }

    [Fact]
    public async Task DecideFill_TrustedDevice_OpensByItself()
    {
        using var box = Sandbox.Create();
        await box.Store.CreateAsync(Master);
        await box.Store.RememberKeyAsync(true);
        box.Store.Lock();
        box.Settings.TrustDevice = true;
        Assert.Equal(FillKind.Offer, (await DecideFillAsync(box.Store, new FoundFields { Pass = new Node() })).Kind);
    }

    [Fact]
    public async Task DecideSave_And_SaveTyped()
    {
        using var box = Sandbox.Create();
        Assert.Equal(SaveKind.Nothing, DecideSave(new TypedCredentials(), box.Store));
        Assert.Equal(SaveKind.NeedUnlock, DecideSave(new TypedCredentials { Password = "x" }, box.Store));
        Assert.False(await SaveTypedAsync(box.Store, new TypedCredentials { Username = "a", Password = "b", WebDomain = "x.com" }, _ => null));   // cerrada: no se guarda

        await box.Store.CreateAsync(Master);
        Assert.Equal(SaveKind.SaveNow, DecideSave(new TypedCredentials { Username = "ana" }, box.Store));

        // App: el titulo es el nombre de la app; web: el dominio (sin preguntar el nombre).
        var asked = new List<string?>();
        Assert.True(await SaveTypedAsync(box.Store, new TypedCredentials { Username = "ana", Password = "1", Package = "es.banco" }, p => { asked.Add(p); return "Mi Banco"; }));
        Assert.True(await SaveTypedAsync(box.Store, new TypedCredentials { Username = "luis", Password = "2", WebDomain = "example.com", Package = "com.android.chrome" }, p => { asked.Add(p); return "Chrome"; }));
        Assert.Equal(["es.banco"], asked);
        Assert.Equal(["Mi Banco", "example.com"], box.Store.Data!.Entries.Select(e => e.Title));
        // Lo mismo otra vez: nada que guardar.
        Assert.False(await SaveTypedAsync(box.Store, new TypedCredentials { Username = "luis", Password = "2", WebDomain = "example.com" }, _ => null));
    }

    private sealed class FakeBio(bool available, bool ok) : IBiometric
    {
        public int Prompts;
        public Task<bool> IsAvailableAsync() => Task.FromResult(available);

        public Task<bool> AuthenticateAsync(string title, string reason)
        {
            Prompts++;
            return Task.FromResult(ok);
        }
    }

    [Fact]
    public async Task EnsureUnlocked_Biometrics_ThenThePage()
    {
        using var box = Sandbox.Create();
        var l = box.Texts("es");
        await box.Store.CreateAsync(Master);
        await box.Store.RememberKeyAsync(true);
        box.Store.Lock();
        var pages = 0;

        // Biometria desactivada: la pagina (que el usuario cierra sin abrir).
        Assert.False(await EnsureUnlockedAsync(box.Store, box.Settings, new FakeBio(true, true), l, () => { pages++; return Task.CompletedTask; }));
        Assert.Equal(1, pages);

        // Activada pero falla: la pagina, que si la abre.
        box.Settings.Biometrics = true;
        var failing = new FakeBio(true, false);
        Assert.True(await EnsureUnlockedAsync(box.Store, box.Settings, failing, l, () => { pages++; return box.Store.UnlockAsync(Master); }));
        Assert.Equal((1, 2), (failing.Prompts, pages));
        box.Store.Lock();

        // Sin biometria en el dispositivo: ni se pregunta.
        var none = new FakeBio(false, true);
        Assert.False(await EnsureUnlockedAsync(box.Store, box.Settings, none, l, () => Task.CompletedTask));
        Assert.Equal(0, none.Prompts);

        // Huella buena: abierta sin pagina.
        var good = new FakeBio(true, true);
        Assert.True(await EnsureUnlockedAsync(box.Store, box.Settings, good, l, () => throw new InvalidOperationException("no deberia")));
        // Ya abierta: nada.
        Assert.True(await EnsureUnlockedAsync(box.Store, box.Settings, good, l, () => throw new InvalidOperationException("no deberia")));
        Assert.Equal(1, good.Prompts);
    }

    [Fact]
    public async Task ScreenOff_LocksUnlessTrusted()
    {
        using var box = Sandbox.Create();
        await box.Store.CreateAsync(Master);
        box.Settings.TrustDevice = true;
        OnScreenOff(box.Settings, box.Store);
        Assert.True(box.Store.IsUnlocked);
        box.Settings.TrustDevice = false;
        OnScreenOff(box.Settings, box.Store);
        Assert.False(box.Store.IsUnlocked);
    }

    // ------------------------------------------------------------------ elegir el servicio

    [Fact]
    public void Setup_InstalledBrowsers_AndOpenFirst()
    {
        Assert.Equal(["Google Chrome", "Firefox"], AutofillSetupLogic.Installed(p => p is "com.android.chrome" or "org.mozilla.firefox").Select(b => b.Name));
        Assert.Empty(AutofillSetupLogic.Installed(_ => false));

        var tried = new List<string>();
        Assert.True(AutofillSetupLogic.OpenFirst(AutofillSetupLogic.PreferredServiceActions, a => { tried.Add(a); if (a.Contains("CREDENTIAL")) throw new InvalidOperationException("no existe en este Android"); }));
        Assert.Equal(["android.settings.CREDENTIAL_PROVIDER", "android.settings.REQUEST_SET_AUTOFILL_SERVICE"], tried);
        Assert.False(AutofillSetupLogic.OpenFirst(["a", "b"], _ => throw new InvalidOperationException()));
        Assert.True(AutofillSetupLogic.OpenFirst(["a"], _ => { }));
    }

    [Fact]
    public async Task Setup_OfferOncePerSession()
    {
        using var box = Sandbox.Create();
        var l = box.Texts("es");
        var sheets = new List<string>();
        var answers = new Queue<string?>([l["AutofillUseThis"], l["ExtDontAsk"], null]);
        Task<string?> Sheet(string title, string cancel, string[] options)
        {
            sheets.Add($"{title}|{cancel}|{string.Join(",", options)}");
            return Task.FromResult(answers.Dequeue());
        }
        var requests = 0;

        // No soportado, o ya es este: nada.
        await new AutofillSetupLogic().OfferAfterUnlockAsync(Sheet, box.Settings, l, () => false, () => false, () => requests++);
        await new AutofillSetupLogic().OfferAfterUnlockAsync(Sheet, box.Settings, l, () => true, () => true, () => requests++);
        Assert.Empty(sheets);

        var offer = new AutofillSetupLogic();
        await offer.OfferAfterUnlockAsync(Sheet, box.Settings, l, () => true, () => false, () => requests++);
        Assert.Equal($"{l["AutofillOfferTitle"]}|{l["NotNow"]}|{l["AutofillUseThis"]},{l["ExtDontAsk"]}", Assert.Single(sheets));
        Assert.Equal(1, requests);
        await offer.OfferAfterUnlockAsync(Sheet, box.Settings, l, () => true, () => false, () => requests++);
        Assert.Single(sheets);   // una vez por sesion

        await new AutofillSetupLogic().OfferAfterUnlockAsync(Sheet, box.Settings, l, () => true, () => false, () => requests++);
        Assert.False(box.Settings.AskAutofill);
        await new AutofillSetupLogic().OfferAfterUnlockAsync(Sheet, box.Settings, l, () => true, () => false, () => requests++);
        Assert.Equal(2, sheets.Count);

        // «Ahora no»: ni se cambia ni se deja de preguntar.
        box.Settings.AskAutofill = true;
        await new AutofillSetupLogic().OfferAfterUnlockAsync(Sheet, box.Settings, l, () => true, () => false, () => requests++);
        Assert.True(box.Settings.AskAutofill);
        Assert.Equal(1, requests);
    }

    [Fact]
    public void OAuth_RedirectAndCallback()
    {
        Assert.Equal("com.googleusercontent.apps.123:/oauth", AndroidOAuth.RedirectUri("Google", "com.googleusercontent.apps.123"));
        Assert.Equal("com.socratic.credentials://auth", AndroidOAuth.RedirectUri("Microsoft", "x"));
        var uri = AndroidOAuth.CallbackWithProperties(new Uri("com.socratic.credentials://auth"), [new("code", "a b&c"), new("state", "z")]);
        Assert.Equal("com.socratic.credentials://auth/?code=a%20b%26c&state=z", uri.AbsoluteUri);
    }
}
