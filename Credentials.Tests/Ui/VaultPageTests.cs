using System.Collections.ObjectModel;
using Credentials.Models;
using Credentials.Pages;

namespace Credentials.Ui.Tests;

public class VaultPageTests
{
    private const string Seed = "otpauth://totp/GitHub:ana?secret=JBSWY3DPEHPK3PXP&issuer=GitHub";

    private static Credential[] Sample() =>
    [
        new() { Title = "GitHub", Username = "ana@example.com", Password = "clave-1", Url = "https://github.com", Folder = "Trabajo", Tags = ["dev"], Favorite = true, Totp = Seed },
        new() { Title = "Banco", Username = "12345678A", Password = "clave-2", Folder = "Personal" },
        new() { Kind = EntryKind.Totp, Title = "Roto", Totp = "no vale: 0189!" },
        new() { Kind = EntryKind.Note, Title = "Licencia", Notes = "XXXX", Folder = "Trabajo" },
    ];

    private static ObservableCollection<EntryRow> Rows(VaultPage page) => Ui.Field<ObservableCollection<EntryRow>>(page, "_rows");

    private static List<Button> Chips(VaultPage page) => page.Find<HorizontalStackLayout>("Chips").Children.Cast<Button>().ToList();

    [Fact]
    public void Bloqueada_no_enseña_nada_pero_pone_los_textos()
    {
        using var app = TestHost.Start();
        var page = new VaultPage();
        Assert.Equal(app["MenuVault"], page.Title);
        Assert.Equal(app["SearchPlaceholder"], page.Find<Entry>("SearchEntry").Placeholder);
        Assert.Equal(app["NoEntries"], page.Find<Label>("EmptyLabel").Text);
        Assert.Equal(app["Lock"], SemanticProperties.GetDescription(page.Find<Button>("LockButton")));
        Assert.Equal(app["Add"], ToolTipProperties.GetText(page.Find<Button>("AddButton")));
        Assert.Empty(Rows(page));

        app.Texts.SetLanguage("en");   // cambio de idioma en caliente
        Assert.Equal(app["MenuVault"], page.Title);
    }

    [Fact]
    public async Task Abierta_lista_cuenta_fichas_y_codigos()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync("contraseña-larga", Sample());
        var page = new VaultPage();
        await page.Appear();

        Assert.Equal(["GitHub", "Banco", "Licencia", "Roto"], Rows(page).Select(r => r.Title));
        Assert.Equal("4 entradas", page.Find<Label>("CountLabel").Text);
        Assert.Equal(
            [app["AllEntries"], "★ " + app["Favorites"], app["KindLogin"], app["KindTotp"], app["KindNote"], "/Personal", "/Trabajo", "#dev"],
            Chips(page).Select(b => b.Text));

        // El temporizador de cada segundo refresca los codigos; el roto se queda vacio.
        var timer = Assert.Single(app.Dispatcher.Timers);
        Assert.True(timer.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(1), timer.Interval);
        Rows(page)[0].Code = string.Empty;
        string? changed = null;
        Rows(page)[0].PropertyChanged += (_, e) => changed = e.PropertyName;
        timer.Fire();
        Assert.Matches(@"^\d{3} \d{3} · \d+$", Rows(page)[0].Code);
        Assert.Equal(nameof(EntryRow.Code), changed);
        Assert.Equal(string.Empty, Rows(page).Single(r => r.Title == "Roto").Code);

        // Volver a la pagina no crea otro temporizador; irse lo para.
        await page.Disappear();
        Assert.False(timer.IsRunning);
        await page.Appear();
        Assert.Single(app.Dispatcher.Timers);
        Assert.True(timer.IsRunning);
    }

    [Fact]
    public async Task Filas_con_sus_iconos_y_pistas()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync("contraseña-larga", Sample());
        var rows = Rows(new VaultPage());
        var github = rows.Single(r => r.Title == "GitHub");
        Assert.Equal("ana@example.com · github.com", github.Subtitle);
        Assert.Equal("ic_web.png", github.Icon);
        Assert.True(github.HasUser && github.HasPassword && github.HasCode);
        Assert.Equal(app["CopyUser"], github.CopyUserTip);
        Assert.Equal(app["CopyPassword"], github.CopyPasswordTip);
        Assert.Equal(app["CopyCode"], github.CopyCodeTip);
        Assert.Equal(app["DeleteEntry"], github.DeleteTip);
        var note = rows.Single(r => r.Title == "Licencia");
        Assert.Equal("Trabajo", note.Subtitle);
        Assert.Equal("ic_note.png", note.Icon);
        Assert.False(note.HasUser || note.HasPassword || note.HasCode);
        Assert.Equal("ic_totp.png", rows.Single(r => r.Title == "Roto").Icon);
        Assert.Equal("ic_app.png", new EntryRow(new Credential { Kind = EntryKind.App }, app.Texts).Icon);
        Assert.Equal(string.Empty, new EntryRow(new Credential { Kind = EntryKind.Note }, app.Texts).Subtitle);
    }

    [Fact]
    public async Task Buscar_filtrar_y_atras_lo_quita()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync("contraseña-larga", Sample());
        var page = new VaultPage();

        page.Find<Entry>("SearchEntry").Text = "bANco";
        Assert.Equal(["Banco"], Rows(page).Select(r => r.Title));
        Assert.Equal("1 entrada", page.Find<Label>("CountLabel").Text);

        // Atras con busqueda: la quita y no sale de la pagina.
        Assert.True(page.Back());
        Assert.Equal(string.Empty, page.Find<Entry>("SearchEntry").Text);
        Assert.Equal(4, Rows(page).Count);

        // Ficha de carpeta: filtra; pulsarla otra vez la quita.
        Chips(page).Single(b => b.Text == "/Trabajo").SendClicked();
        Assert.Equal(["GitHub", "Licencia"], Rows(page).Select(r => r.Title));
        Assert.Equal("ChipOn", StyleKey(Chips(page).Single(b => b.Text == "/Trabajo")));
        Chips(page).Single(b => b.Text == "/Trabajo").SendClicked();
        Assert.Equal(4, Rows(page).Count);

        // Atras con filtro (sin busqueda): vuelve a todas.
        Chips(page).Single(b => b.Text == "#dev").SendClicked();
        Assert.Single(Rows(page));
        Assert.True(page.Back());
        Assert.Equal(4, Rows(page).Count);

        // Sin nada que quitar, atras es lo de siempre (en Windows no se queda en la pagina).
        Assert.False(page.Back());
    }

    private static string? StyleKey(Button b) =>
        Application.Current!.Resources.TryGetValue("ChipOn", out var on) && ReferenceEquals(on, b.Style) ? "ChipOn" : "Chip";

    [Fact]
    public async Task Ordenar_desde_la_lista_de_opciones()
    {
        using var app = TestHost.Start();
        var entries = Sample();
        entries[1].ModifiedAt = DateTimeOffset.UtcNow.AddDays(1);
        await app.CreateVaultAsync("contraseña-larga", entries);
        var page = new VaultPage();

        app.Dialogs.Answer(app["SortModified"]);
        await page.Click("SortButton");
        Assert.Equal("modified", app.Settings.SortMode);
        Assert.Equal("Banco", Rows(page)[0].Title);
        var sheet = app.Dialogs.Log[^1];
        Assert.Equal([app["SortTitle"], app["SortModified"], app["SortCreated"]], sheet.Options);

        await page.Click("SortButton");   // cancelar: se queda como estaba
        Assert.Equal("modified", app.Settings.SortMode);
    }

    [Fact]
    public async Task Añadir_abre_la_ficha_nueva_en_la_carpeta_del_filtro()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync("contraseña-larga", Sample());
        var page = new VaultPage();

        await page.Click("AddButton");   // cancelado
        Assert.Empty(page.Navigation.NavigationStack);

        Chips(page).Single(b => b.Text == "/Personal").SendClicked();
        app.Dialogs.Answer(app["KindApp"]);
        await page.Click("AddButton");
        var entryPage = Assert.IsType<EntryPage>(page.Navigation.NavigationStack[^1]);
        var entry = Ui.Field<Credential>(entryPage, "_entry");
        Assert.Equal(EntryKind.App, entry.Kind);
        Assert.Equal("Personal", entry.Folder);
        Assert.True(Ui.Field<bool>(entryPage, "_isNew"));
    }

    [Fact]
    public async Task Tocar_una_fila_abre_su_ficha()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync("contraseña-larga", Sample());
        var page = new VaultPage();
        var row = Rows(page)[1];

        await Ui.Call(page, "OnRowTapped", new Label(), new TappedEventArgs(null));   // sin fila: nada
        Assert.Empty(page.Navigation.NavigationStack);
        await Ui.Call(page, "OnRowTapped", new Label { BindingContext = row }, new TappedEventArgs(null));
        var entryPage = Assert.IsType<EntryPage>(page.Navigation.NavigationStack[^1]);
        Assert.Equal(row.Entry.Id, Ui.Field<Credential>(entryPage, "_entry").Id);
        Assert.False(Ui.Field<bool>(entryPage, "_isNew"));
    }

    [Fact]
    public async Task Borrar_desde_la_fila_con_confirmacion()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync("contraseña-larga", Sample());
        var page = new VaultPage();
        var banco = Rows(page).Single(r => r.Title == "Banco");

        await Ui.Call(page, "OnDeleteRowClicked", new Button(), EventArgs.Empty);   // sin fila: nada
        await Ui.Call(page, "OnDeleteRowClicked", new Button { BindingContext = banco }, EventArgs.Empty);   // «no»
        Assert.False(banco.Entry.Deleted);
        Assert.Equal(string.Format(app["DeleteEntryConfirm"], "Banco"), app.Dialogs.Log[^1].Message);

        app.Dialogs.Answer(true);
        await Ui.Call(page, "OnDeleteRowClicked", new Button { BindingContext = banco }, EventArgs.Empty);
        Assert.True(app.Store.Data!.Entries.Single(e => e.Id == banco.Entry.Id).Deleted);
        Assert.Equal(app["EntryDeleted"], app.Toast.Shown[^1]);
        Assert.DoesNotContain(Rows(page), r => r.Title == "Banco");   // la lista se refresca sola (Changed)
    }

    [Fact]
    public async Task Copiar_usuario_contraseña_y_codigo()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync("contraseña-larga", Sample());
        app.Settings.ClipboardSeconds = 0;
        var page = new VaultPage();
        var github = Rows(page).Single(r => r.Title == "GitHub");
        var sender = new ImageButton { BindingContext = github };

        await Ui.Call(page, "OnCopyUserClicked", sender, EventArgs.Empty);
        Assert.Equal("ana@example.com", app.Clipboard.Text);
        Assert.Equal(app["CopiedUser"], app.Toast.Shown[^1]);
        await Ui.Call(page, "OnCopyPasswordClicked", sender, EventArgs.Empty);
        Assert.Equal("clave-1", app.Clipboard.Text);
        await Ui.Call(page, "OnCopyCodeClicked", sender, EventArgs.Empty);
        Assert.Matches(@"^\d{6}$", app.Clipboard.Text);
        Assert.Equal(app["CopiedCode"], app.Toast.Shown[^1]);

        // Sin fila, o sin codigo valido: no se copia nada.
        var before = app.Clipboard.History.Count;
        await Ui.Call(page, "OnCopyUserClicked", new ImageButton(), EventArgs.Empty);
        await Ui.Call(page, "OnCopyPasswordClicked", new ImageButton(), EventArgs.Empty);
        await Ui.Call(page, "OnCopyCodeClicked", new ImageButton { BindingContext = Rows(page).Single(r => r.Title == "Roto") }, EventArgs.Empty);
        // Una fila sin usuario: texto vacio, nada que copiar.
        await Ui.Call(page, "OnCopyUserClicked", new ImageButton { BindingContext = Rows(page).Single(r => r.Title == "Licencia") }, EventArgs.Empty);
        Assert.Equal(before, app.Clipboard.History.Count);
    }

    [Fact]
    public async Task Lo_sensible_se_borra_del_portapapeles_pasado_el_tiempo()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync("contraseña-larga", Sample());
        app.Settings.ClipboardSeconds = 1;
        var page = new VaultPage();
        var sender = new ImageButton { BindingContext = Rows(page).Single(r => r.Title == "Banco") };

        await Ui.Call(page, "OnCopyUserClicked", sender, EventArgs.Empty);   // el usuario no es sensible: se queda
        await Ui.Call(page, "OnCopyPasswordClicked", sender, EventArgs.Empty);
        await Ui.Until(() => app.Clipboard.Text == " ");
        await Ui.Until(() => app.Toast.Shown.Contains(app["ClipboardCleared"]));

        // Si el portapapeles no se deja leer, no pasa nada.
        app.Clipboard.FailRead = true;
        var cleared = app.Toast.Shown.Count(t => t == app["ClipboardCleared"]);
        await Ui.Call(page, "OnCopyPasswordClicked", sender, EventArgs.Empty);
        await Task.Delay(1500);
        Assert.Equal(cleared, app.Toast.Shown.Count(t => t == app["ClipboardCleared"]));
        app.Clipboard.FailRead = false;

        // Si entretanto se copio otra cosa, esa no se toca.
        await Ui.Call(page, "OnCopyPasswordClicked", sender, EventArgs.Empty);
        await app.Clipboard.SetTextAsync("otra cosa");
        await Task.Delay(1500);
        Assert.Equal("otra cosa", app.Clipboard.Text);
    }

    [Fact]
    public async Task Bloquear_vacia_la_lista_y_saca_la_puerta()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync("contraseña-larga", Sample());
        var page = new VaultPage();
        Assert.Equal(4, Rows(page).Count);

        // Sin esperar al manejador: la puerta queda abierta hasta que se use (como en pantalla).
        page.Find<Button>("LockButton").SendClicked();
        Assert.False(app.Store.IsUnlocked);
        Assert.Empty(Rows(page));
        await Ui.UnlockThrough(page, "contraseña-larga");
        Assert.True(app.Store.IsUnlocked);
        await Ui.Until(() => Rows(page).Count == 4);
    }

    [Fact]
    public async Task Inactividad_bloquea_en_el_latido()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync("contraseña-larga", Sample());
        app.Settings.AutoLockMinutes = 1;
        var page = new VaultPage();
        await page.Appear();
        Ui.SetField(app.Store, "_lastActivity", DateTimeOffset.UtcNow.AddMinutes(-5));
        app.Dispatcher.Timers[0].Fire();
        Assert.False(app.Store.IsUnlocked);
        await Ui.UnlockThrough(page, "contraseña-larga");
        Assert.True(app.Store.IsUnlocked);
    }

    [Fact]
    public async Task Al_aparecer_bloqueada_pide_la_contraseña_y_luego_enseña()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync("contraseña-larga", Sample());
        app.Store.Lock();
        var page = new VaultPage();
        var appearing = page.Appear();
        await Ui.UnlockThrough(page, "contraseña-larga");
        await appearing;
        Assert.Equal(4, Rows(page).Count);
        Assert.Single(app.Dispatcher.Timers);
    }

    [Fact]
    public async Task Si_la_puerta_se_cierra_sin_abrir_la_pagina_no_sigue()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync("contraseña-larga", Sample());
        app.Store.Lock();
        var page = new VaultPage();
        var appearing = page.Appear();
        var gate = await Ui.GateOn(page);

        // Mientras la puerta esta, otra pagina que la pida no saca otra.
        var other = new VaultPage();
        Assert.False(await Pages.Gate.EnsureUnlockedAsync(other));
        Assert.Empty(other.Navigation.ModalStack);
        Assert.Same(gate, Pages.Gate.Current);

        Ui.Dismiss(page, gate);
        await appearing;
        Assert.Null(Pages.Gate.Current);
        Assert.Empty(app.Dispatcher.Timers);   // no ha seguido
    }
}
