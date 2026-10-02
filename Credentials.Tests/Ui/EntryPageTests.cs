using Credentials.Models;
using Credentials.Pages;
using Credentials.Services;
using ZXing.Net.Maui.Controls;

namespace Credentials.Ui.Tests;

public class EntryPageTests
{
    private const string Seed = "otpauth://totp/GitHub:ana?secret=JBSWY3DPEHPK3PXP&issuer=GitHub";

    private static Credential Full() => new()
    {
        Title = "GitHub",
        Username = "ana@example.com",
        Password = "vieja-clave",
        Url = "github.com",
        Folder = "Trabajo",
        Tags = ["dev", "git"],
        Notes = "nota",
        Totp = Seed,
        Favorite = true,
        Fields = [new() { Name = "PIN", Value = "1234", Hidden = true }, new() { Name = "Pregunta", Value = "gato" }],
        RecoveryCodes = [new() { Code = "AAA-111" }, new() { Code = "BBB-222", Used = true }],
        History = [new("primera", DateTimeOffset.UtcNow.AddDays(-30)), new("segunda", DateTimeOffset.UtcNow.AddDays(-10))],
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-40),
        ModifiedAt = DateTimeOffset.UtcNow.AddDays(-5),
    };

    /// <summary>Abre la ficha encima de otra pagina (como desde la lista), con la boveda abierta.</summary>
    private static async Task<(TestHost App, ContentPage Host, EntryPage Page, Credential Entry)> Open(Credential? entry = null, bool isNew = false)
    {
        var app = TestHost.Start();
        entry ??= Full();
        await app.CreateVaultAsync("contraseña-larga", isNew ? [] : [entry]);
        var host = new ContentPage();
        var page = new EntryPage(entry, isNew);
        await host.Navigation.PushAsync(page);
        return (app, host, page, entry);
    }

    private static Credential Working(EntryPage page) => Ui.Field<Credential>(page, "_entry");

    private static bool Dirty(EntryPage page) => Ui.Field<bool>(page, "_dirty");

    [Fact]
    public async Task Enseña_la_entrada_entera()
    {
        var (app, _, page, entry) = await Open();
        using var _app = app;
        Assert.Equal("GitHub", page.Title);
        Assert.Equal(app["KindLogin"], page.Find<Label>("KindLabel").Text);
        Assert.Equal("ana@example.com", page.Find<Entry>("UsernameEntry").Text);
        Assert.Equal("vieja-clave", page.Find<Entry>("PasswordEntry").Text);
        Assert.Equal("dev, git", page.Find<Entry>("TagsEntry").Text);
        Assert.Equal("ic_star_on.png", ((FileImageSource)page.Find<ImageButton>("FavoriteButton").Source).File);
        Assert.True(page.Find<Button>("DeleteButton").IsVisible);
        Assert.True(page.Find<VisualElement>("TotpLive").IsVisible);
        Assert.False(page.Find<Entry>("TotpEntry").IsVisible);
        Assert.Equal("GitHub · ana  (SHA1, 6, 30s)", page.Find<Label>("TotpIssuer").Text);
        Assert.Matches(@"^\d{3} \d{3}$", page.Find<Label>("TotpCode").Text);
        Assert.Equal(2, page.Find<VerticalStackLayout>("FieldsBox").Count);
        Assert.Equal(2, page.Find<VerticalStackLayout>("HistoryBox").Count);
        Assert.True(page.Find<VisualElement>("HistoryCard").IsVisible);
        Assert.Equal(string.Format(app["RecoveryCount"], 2, 1), page.Find<Label>("RecoveryHint").Text);
        Assert.Contains(entry.CreatedAt.LocalDateTime.ToString("g", app.Texts.CurrentCulture), page.Find<Label>("DatesLabel").Text);
        Assert.False(Dirty(page));
        Assert.NotSame(entry, Working(page));   // se trabaja sobre una copia
    }

    [Fact]
    public async Task Nota_nueva_sin_lo_que_no_le_toca()
    {
        var (app, _, page, _) = await Open(new Credential { Kind = EntryKind.Note }, isNew: true);
        using var _app = app;
        Assert.Equal(app["Add"], page.Title);
        Assert.False(page.Find<VisualElement>("UsernameRow").IsVisible);
        Assert.False(page.Find<VisualElement>("PasswordRow").IsVisible);
        Assert.False(page.Find<VisualElement>("UrlRow").IsVisible);
        Assert.False(page.Find<VisualElement>("TotpCard").IsVisible);
        Assert.False(page.Find<Button>("DeleteButton").IsVisible);
        Assert.False(page.Find<VisualElement>("HistoryCard").IsVisible);
        Assert.Equal(string.Empty, page.Find<Label>("DatesLabel").Text);
        Assert.Equal(app["RecoveryNone"], page.Find<Label>("RecoveryHint").Text);
        Assert.False(page.Find<ImageButton>("RecoveryEye").IsVisible);

        await page.Appear();   // nueva: el foco al titulo, y el latido en marcha
        Assert.True(Assert.Single(app.Dispatcher.Timers).IsRunning);
    }

    [Fact]
    public async Task Bloqueada_pide_la_contraseña_y_sin_ella_no_sigue()
    {
        var (app, _, page, _) = await Open();
        using var _app = app;
        app.Store.Lock();
        var appearing = page.Appear();
        Ui.Dismiss(page, await Ui.GateOn(page));
        await appearing;
        Assert.Empty(app.Dispatcher.Timers);
    }

    [Fact]
    public async Task El_latido_refresca_el_codigo_y_bloquea_por_inactividad()
    {
        var (app, _, page, _) = await Open();
        using var _app = app;
        await page.Appear();
        var timer = Assert.Single(app.Dispatcher.Timers);
        page.Find<Label>("TotpCode").Text = string.Empty;
        timer.Fire();
        Assert.Matches(@"^\d{3} \d{3}$", page.Find<Label>("TotpCode").Text);
        Assert.InRange(page.Find<ProgressBar>("TotpProgress").Progress, 0, 1);
        await page.Disappear();
        Assert.False(timer.IsRunning);
        await page.Appear();
        Assert.Single(app.Dispatcher.Timers);

        // Sin segundo factor el latido no toca nada.
        await Ui.Call(page, "OnRemoveTotpClicked");
        page.Find<Label>("TotpCode").Text = "x";
        timer.Fire();
        Assert.Equal("x", page.Find<Label>("TotpCode").Text);

        app.Settings.AutoLockMinutes = 1;
        Ui.SetField(app.Store, "_lastActivity", DateTimeOffset.UtcNow.AddMinutes(-3));
        timer.Fire();
        Assert.False(app.Store.IsUnlocked);
    }

    [Fact]
    public async Task Cambios_favorita_ojo_y_fuerza()
    {
        var (app, _, page, _) = await Open();
        using var _app = app;
        page.Find<Entry>("TitleEntry").Text = "GitHub 2";
        Assert.True(Dirty(page));

        page.Find<Entry>("PasswordEntry").Text = "";
        Assert.Equal(string.Empty, page.Find<Label>("StrengthLabel").Text);
        Assert.Equal(0, page.Find<ProgressBar>("StrengthBar").Progress);
        const string strong = "Una-Clave-Muy-Larga-Y-Rara-2026!";
        page.Find<Entry>("PasswordEntry").Text = strong;
        var level = PasswordGenerator.Strength(strong);
        Assert.Equal(app["Strength" + level], page.Find<Label>("StrengthLabel").Text);
        Assert.Equal(EntryRules.StrengthColor(level), page.Find<ProgressBar>("StrengthBar").ProgressColor);

        page.Find<ImageButton>("FavoriteButton").SendClicked();
        Assert.False(Working(page).Favorite);
        Assert.Equal("ic_star.png", ((FileImageSource)page.Find<ImageButton>("FavoriteButton").Source).File);

        var password = page.Find<Entry>("PasswordEntry");
        Assert.True(password.IsPassword);
        page.Find<ImageButton>("EyeButton").SendClicked();
        Assert.False(password.IsPassword);
        Assert.Equal("ic_eye_off.png", ((FileImageSource)page.Find<ImageButton>("EyeButton").Source).File);
        page.Find<ImageButton>("EyeButton").SendClicked();
        Assert.True(password.IsPassword);
    }

    [Fact]
    public async Task Copiar_y_abrir_el_enlace()
    {
        var (app, _, page, _) = await Open();
        using var _app = app;
        await Ui.Call(page, "OnCopyUserClicked");
        Assert.Equal("ana@example.com", app.Clipboard.Text);
        await Ui.Call(page, "OnCopyPasswordClicked");
        Assert.Equal("vieja-clave", app.Clipboard.Text);
        await Ui.Call(page, "OnCopyCodeClicked");
        Assert.Matches(@"^\d{6}$", app.Clipboard.Text);
        Assert.Equal(app["CopiedCode"], app.Toast.Shown[^1]);

        // La contraseña de antes, desde el historial (la mas reciente primero).
        var historyCopy = ((Grid)page.Find<VerticalStackLayout>("HistoryBox")[0]).Children.OfType<ImageButton>().Single();
        await AsyncVoid.Run(historyCopy.SendClicked);
        Assert.Equal("segunda", app.Clipboard.Text);

        await Ui.Call(page, "OnOpenUrlClicked");
        Assert.Equal("https://github.com/", app.Browser.Opened.Single().ToString());

        page.Find<Entry>("UrlEntry").Text = "  ";
        await Ui.Call(page, "OnOpenUrlClicked");
        Assert.Single(app.Browser.Opened);

        page.Find<Entry>("UrlEntry").Text = "https://roto.example";
        app.Browser.Fail = new InvalidOperationException("sin navegador");
        await Ui.Call(page, "OnOpenUrlClicked");
        Assert.Equal("sin navegador", app.Dialogs.Log[^1].Message);
    }

    [Fact]
    public async Task Generador_de_contraseñas()
    {
        var (app, _, page, _) = await Open();
        using var _app = app;
        var card = page.Find<VisualElement>("GeneratorCard");
        Assert.False(card.IsVisible);
        await Ui.Call(page, "OnGenerateClicked");
        Assert.True(card.IsVisible);
        var generated = page.Find<Label>("GeneratedLabel").Text;
        var length = (int)Math.Round(page.Find<Slider>("LengthSlider").Value);
        Assert.Equal(length, generated.Length);
        Assert.Equal(string.Format(app["Length"], length), page.Find<Label>("LengthLabel").Text);

        // Solo cifras y 24 de largo.
        page.Find<CheckBox>("UpperCheck").IsChecked = false;
        page.Find<CheckBox>("LowerCheck").IsChecked = false;
        page.Find<CheckBox>("SymbolsCheck").IsChecked = false;
        page.Find<Slider>("LengthSlider").Value = 24;
        Assert.Matches(@"^\d{24}$", page.Find<Label>("GeneratedLabel").Text);
        await page.Click("RegenerateButton");
        Assert.Matches(@"^\d{24}$", page.Find<Label>("GeneratedLabel").Text);

        await page.Click("UseButton");
        Assert.Equal(page.Find<Label>("GeneratedLabel").Text, page.Find<Entry>("PasswordEntry").Text);
        Assert.False(page.Find<Entry>("PasswordEntry").IsPassword);
        Assert.False(card.IsVisible);
        Assert.True(Dirty(page));

        await Ui.Call(page, "OnGenerateClicked");
        await Ui.Call(page, "OnGenerateClicked");   // abrir y cerrar
        Assert.False(card.IsVisible);
    }

    [Fact]
    public async Task Segundo_factor_a_mano_por_QR_y_quitarlo()
    {
        var (app, _, page, _) = await Open(new Credential { Title = "Sitio", Username = "ana" });
        using var _app = app;
        var input = page.Find<Entry>("TotpEntry");
        Assert.True(input.IsVisible);

        input.Text = "esto: no!";
        await Ui.Call(page, "OnTotpEntered");
        Assert.True(page.Find<Label>("TotpError").IsVisible);
        Assert.Equal(app["TotpInvalid"], page.Find<Label>("TotpError").Text);
        Assert.False(Working(page).HasTotp);

        input.Text = "jbsw y3dp ehpk 3pxp";   // a pelo y en grupos
        await Ui.Call(page, "OnTotpUnfocused", input, new FocusEventArgs(input, false));
        Assert.False(page.Find<Label>("TotpError").IsVisible);
        Assert.StartsWith("otpauth://totp/", Working(page).Totp);
        Assert.True(page.Find<VisualElement>("TotpLive").IsVisible);
        Assert.Equal(string.Empty, input.Text);

        // Salir de la casilla vacia no hace nada.
        await Ui.Call(page, "OnTotpUnfocused", input, new FocusEventArgs(input, false));

        await Ui.Call(page, "OnRemoveTotpClicked");
        Assert.False(Working(page).HasTotp);
        Assert.True(input.IsVisible);

        // Lector QR: cancelado, y luego leido.
        await page.Click("ScanButton");
        Assert.False(Working(page).HasTotp);
        app.Scanner.Results.Enqueue(Seed);
        await page.Click("ScanButton");
        Assert.Equal("GitHub", Totp.Parse(Working(page).Totp)!.Issuer);

        // Copiar el codigo sin segundo factor: nada.
        await Ui.Call(page, "OnRemoveTotpClicked");
        var copies = app.Clipboard.History.Count;
        await Ui.Call(page, "OnCopyCodeClicked");
        await Ui.Call(page, "OnCopySeedClicked");
        Assert.Equal(copies, app.Clipboard.History.Count);
    }

    [Fact]
    public async Task La_semilla_se_enseña_copia_y_sale_en_QR()
    {
        var (app, _, page, _) = await Open();
        using var _app = app;
        Assert.False(page.Find<VisualElement>("SeedRow").IsVisible);
        page.Find<ImageButton>("SeedButton").SendClicked();
        Assert.True(page.Find<VisualElement>("SeedRow").IsVisible);
        Assert.True(page.Find<VisualElement>("SeedQrBox").IsVisible);
        Assert.Equal("JBSW Y3DP EHPK 3PXP", page.Find<Label>("SeedLabel").Text);
        Assert.StartsWith("otpauth://totp/", page.Find<BarcodeGeneratorView>("SeedQr").Value);
        await Ui.Call(page, "OnCopySeedClicked");
        Assert.Equal("JBSWY3DPEHPK3PXP", app.Clipboard.Text);
        Assert.Equal(app["CopiedSeed"], app.Toast.Shown[^1]);
        page.Find<ImageButton>("SeedButton").SendClicked();
        Assert.False(page.Find<VisualElement>("SeedQrBox").IsVisible);
        Assert.Null(page.Find<BarcodeGeneratorView>("SeedQr").Value);
    }

    [Fact]
    public async Task Codigos_de_respaldo()
    {
        var (app, _, page, _) = await Open();
        using var _app = app;
        var box = page.Find<VerticalStackLayout>("RecoveryBox");
        Assert.False(box.IsVisible);
        page.Find<ImageButton>("RecoveryEye").SendClicked();
        Assert.True(box.IsVisible);
        Assert.Equal(2, box.Count);

        // Pegar varios: los repetidos no entran y la lista se enseña.
        page.Find<Editor>("RecoveryEditor").Text = "aaa-111\nCCC-333, DDD-444";
        await page.Click("RecoveryAddButton");
        Assert.Equal(["AAA-111", "BBB-222", "CCC-333", "DDD-444"], Working(page).RecoveryCodes.Select(r => r.Code));
        Assert.Equal(string.Empty, page.Find<Editor>("RecoveryEditor").Text);
        Assert.Equal(4, box.Count);
        await page.Click("RecoveryAddButton");   // vacio: nada
        Assert.Equal(4, Working(page).RecoveryCodes.Count);

        // Marcar como usado, copiar y quitar desde la fila.
        var row = (Grid)box[0];
        row.Children.OfType<CheckBox>().Single().IsChecked = true;
        Assert.True(Working(page).RecoveryCodes[0].Used);
        row = (Grid)box[0];
        var buttons = row.Children.OfType<ImageButton>().ToList();
        await AsyncVoid.Run(buttons[0].SendClicked);
        Assert.Equal("AAA-111", app.Clipboard.Text);
        buttons[1].SendClicked();
        Assert.Equal(3, Working(page).RecoveryCodes.Count);
        Assert.Equal(3, box.Count);
        Assert.Equal(string.Format(app["RecoveryCount"], 3, 2), page.Find<Label>("RecoveryHint").Text);

        page.Find<ImageButton>("RecoveryEye").SendClicked();
        Assert.False(box.IsVisible);
        Assert.Empty(box.Children);
    }

    [Fact]
    public async Task Campos_extra()
    {
        var (app, _, page, _) = await Open();
        using var _app = app;
        var box = page.Find<VerticalStackLayout>("FieldsBox");
        var pin = (Grid)box[0];
        var entries = pin.Children.OfType<Entry>().ToList();
        Assert.Equal("PIN", entries[0].Text);
        Assert.True(entries[1].IsPassword);
        pin.Children.OfType<CheckBox>().Single().IsChecked = false;
        Assert.False(entries[1].IsPassword);

        await Ui.Call(page, "OnAddFieldClicked");
        Assert.Equal(3, box.Count);
        var added = ((Grid)box[2]).Children.OfType<Entry>().ToList();
        added[0].Text = "  Nuevo ";
        added[1].Text = "valor";

        // Quitar el segundo; y uno vacio no se guarda.
        ((Grid)box[1]).Children.OfType<ImageButton>().Single().SendClicked();
        await Ui.Call(page, "OnAddFieldClicked");
        Assert.Equal(3, box.Count);

        await page.Click("SaveButton");
        var saved = app.Store.Data!.Entries.Single();
        Assert.Equal([("PIN", "1234", false), ("Nuevo", "valor", false)], saved.Fields.Select(f => (f.Name, f.Value, f.Hidden)));
    }

    [Fact]
    public async Task Guardar_valida_y_escribe_en_la_boveda()
    {
        var (app, host, page, entry) = await Open();
        using var _app = app;

        page.Find<Entry>("TitleEntry").Text = "   ";
        await page.Click("SaveButton");
        Assert.Equal(app["TitleRequired"], app.Dialogs.Log[^1].Message);

        // Un secreto pegado sin Intro que no vale: no se guarda.
        await Ui.Call(page, "OnRemoveTotpClicked");
        page.Find<Entry>("TitleEntry").Text = " GitHub nuevo ";
        page.Find<Entry>("TotpEntry").Text = "no vale: 0!";
        await page.Click("SaveButton");
        Assert.True(page.Find<Label>("TotpError").IsVisible);
        Assert.Equal("GitHub", app.Store.Data!.Entries.Single().Title);

        page.Find<Entry>("TotpEntry").Text = "JBSWY3DPEHPK3PXP";
        page.Find<Editor>("RecoveryEditor").Text = "EEE-555";
        page.Find<Entry>("PasswordEntry").Text = "nueva-clave";
        page.Find<Entry>("FolderEntry").Text = " /Casa/ ";
        page.Find<Entry>("TagsEntry").Text = "uno, DOS, dos";
        page.Find<Editor>("NotesEditor").Text = "otra nota";
        await page.Click("SaveButton");

        var saved = app.Store.Data!.Entries.Single();
        Assert.Equal("GitHub nuevo", saved.Title);
        Assert.Equal("nueva-clave", saved.Password);
        Assert.Equal("Casa", saved.Folder);
        Assert.Equal(["uno", "DOS"], saved.Tags);
        Assert.Equal("otra nota", saved.Notes);
        Assert.True(saved.HasTotp);
        Assert.Contains(saved.RecoveryCodes, r => r.Code == "EEE-555");
        Assert.Equal("vieja-clave", saved.History[0].Password);
        Assert.Contains("Casa", app.Store.Data.Folders);
        Assert.Equal(app["Saved"], app.Toast.Shown[^1]);
        Assert.False(Dirty(page));
        Assert.Equal("vieja-clave", entry.Password);   // el original no se toca hasta guardar: ahora es otra instancia
    }

    [Fact]
    public async Task Guardar_una_nueva_la_añade()
    {
        var (app, _, page, _) = await Open(new Credential { Kind = EntryKind.App }, isNew: true);
        using var _app = app;
        page.Find<Entry>("TitleEntry").Text = "Wi-Fi";
        page.Find<Entry>("PasswordEntry").Text = "clave";
        await page.Click("SaveButton");
        var saved = app.Store.Data!.Entries.Single();
        Assert.Equal(("Wi-Fi", EntryKind.App), (saved.Title, saved.Kind));
        Assert.Empty(saved.History);
    }

    [Fact]
    public async Task Borrar_con_confirmacion()
    {
        var (app, _, page, entry) = await Open();
        using var _app = app;
        await page.Click("DeleteButton");
        Assert.False(app.Store.Data!.Entries.Single().Deleted);
        Assert.Equal(string.Format(app["DeleteEntryConfirm"], "GitHub"), app.Dialogs.Log[^1].Message);
        app.Dialogs.Answer(true);
        await page.Click("DeleteButton");
        Assert.True(app.Store.Data!.Entries.Single().Deleted);
    }

    [Fact]
    public async Task Atras_con_cambios_pregunta()
    {
        var (app, _, page, _) = await Open();
        using var _app = app;
        Assert.False(page.Back());   // sin cambios: atras normal

        page.Find<Entry>("TitleEntry").Text = "Otro titulo";
        Assert.True(page.Back());    // «no guardar»: se va sin guardar
        Assert.Equal("GitHub", app.Store.Data!.Entries.Single().Title);
        Assert.Equal(app["SaveChangesQuestion"], app.Dialogs.Log[^1].Message);

        app.Dialogs.Answer(true);
        Assert.True(page.Back());    // «guardar»
        await Ui.Until(() => app.Store.Data!.Entries.Single().Title == "Otro titulo");
    }

    [Fact]
    public async Task Fecha_y_textos_en_ingles()
    {
        var app = TestHost.Start("en");
        using var _app = app;
        var entry = Full();
        await app.CreateVaultAsync("contraseña-larga", entry);
        var page = new EntryPage(entry, isNew: false);
        Assert.Equal("Title", page.Find<Label>("TitleTitle").Text);
        Assert.StartsWith(app["Created"][..7], page.Find<Label>("DatesLabel").Text);
    }
}
