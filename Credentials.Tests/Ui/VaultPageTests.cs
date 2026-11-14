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
        // Bloquear va solo en la barra de arriba, no entre «ordenar» y «añadir».
        var lockItem = page.Find<ToolbarItem>("LockItem");
        Assert.Contains(lockItem, page.ToolbarItems);
        Assert.Equal(app["Lock"], SemanticProperties.GetDescription(lockItem));
        Assert.Equal(app["Lock"], ToolTipProperties.GetText(lockItem));
        Assert.Equal("ic_lock.png", ((FileImageSource)lockItem.IconImageSource).File);   // en Windows, el del tema
        Assert.Null(page.FindByName("LockButton"));
        Assert.Equal(3, page.Find<Button>("SortButton").Parent is Grid g ? g.Children.Count : 0);   // buscador, ordenar y añadir
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
    public async Task Un_aviso_de_la_boveda_sin_cambios_no_rehace_la_lista()
    {
        // La fuga de 2026.10.22: cada aviso de la boveda (la sincronizacion de cada cinco minutos)
        // vaciaba y volvia a llenar la lista, y en Windows las filas viejas no se liberaban.
        using var app = TestHost.Start();
        await app.CreateVaultAsync("contraseña-larga", Sample());
        var page = new VaultPage();
        await page.Appear();
        var rows = Rows(page).ToList();
        var chips = Chips(page);

        await app.Store.SaveAsync(upload: false);   // avisa de cambios sin haber ninguno
        Assert.Equal(rows, Rows(page));              // las mismas filas, no otras iguales
        Assert.Equal(chips, Chips(page));
        Assert.Equal("4 entradas", page.Find<Label>("CountLabel").Text);

        // Un cambio de verdad en una entrada (en el mismo objeto, como la ficha): esa fila se rehace.
        var banco = app.Store.Data!.Entries.Single(e => e.Title == "Banco");
        banco.Username = "otro-usuario";
        banco.ModifiedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        await app.Store.SaveAsync(upload: false);
        Assert.Equal("otro-usuario", Rows(page).Single(r => r.Title == "Banco").Subtitle);
        Assert.NotSame(rows[1], Rows(page).Single(r => r.Title == "Banco"));
        Assert.Equal(chips, Chips(page));            // las fichas no cambian

        // Una entrada nueva en otra carpeta: filas y fichas nuevas.
        app.Store.Data!.Entries.Add(new Credential { Title = "Nueva", Password = "x", Folder = "Casa" });
        await app.Store.SaveAsync(upload: false);
        Assert.Equal(5, Rows(page).Count);
        Assert.Contains("/Casa", Chips(page).Select(b => b.Text));

        // Otro idioma: filas nuevas, con las pistas en ese idioma.
        var before = Rows(page).ToList();
        app.Texts.SetLanguage("en");
        Assert.DoesNotContain(Rows(page), before.Contains);
        Assert.Equal(app["CopyUser"], Rows(page)[0].CopyUserTip);
    }

    [Fact]
    public void Huella_de_la_fila_y_misma_lista()
    {
        using var app = TestHost.Start();
        var a = new Credential { Title = "A", Username = "ana", Password = "1" };
        var b = new Credential { Title = "B", Totp = Seed };
        var rows = new List<EntryRow> { new(a, app.Texts), new(b, app.Texts) };
        Assert.True(VaultQuery.SameRows(rows, [a, b]));
        Assert.False(VaultQuery.SameRows(rows, [b, a]));                       // otro orden
        Assert.False(VaultQuery.SameRows(rows, [a]));                          // una menos
        Assert.False(VaultQuery.SameRows(rows, [a, new Credential { Id = b.Id, Title = "B", Totp = Seed, ModifiedAt = b.ModifiedAt }])); // otro objeto
        var key = VaultQuery.RowKey(a);
        foreach (var change in new Action[] { () => a.Title = "A2", () => a.Username = "eva", () => a.Password = string.Empty,
                     () => a.Kind = EntryKind.App, () => a.Folder = "F", () => a.Url = "https://x.example", () => a.Totp = Seed })
        {
            change();
            Assert.NotEqual(key, VaultQuery.RowKey(a));
            Assert.False(VaultQuery.SameRows(rows, [a, b]));
            key = VaultQuery.RowKey(a);
        }
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
        Assert.True(github.HasSubtitle);
        Assert.False(github.HasPreview);   // las notas, sin el ajuste, no enseñan nada de su texto
        var note = rows.Single(r => r.Title == "Licencia");
        Assert.Equal("Trabajo", note.Subtitle);
        Assert.Equal(string.Empty, note.Preview);
        Assert.False(note.HasPreview);
        Assert.Equal("ic_note.png", note.Icon);
        Assert.False(note.HasUser || note.HasPassword || note.HasCode);
        Assert.Equal("ic_totp.png", rows.Single(r => r.Title == "Roto").Icon);
        Assert.Equal("ic_app.png", new EntryRow(new Credential { Kind = EntryKind.App }, app.Texts).Icon);
        Assert.Equal(string.Empty, new EntryRow(new Credential { Kind = EntryKind.Note }, app.Texts).Subtitle);
        Assert.False(new EntryRow(new Credential { Kind = EntryKind.Note }, app.Texts).HasSubtitle);
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
    public async Task Las_filas_no_tienen_papelera()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync("contraseña-larga", Sample());
        var page = new VaultPage();
        // Se pulsaba sin querer: borrar va en la ficha, con confirmacion (EntryPageTests).
        Assert.Null(typeof(VaultPage).GetMethod("OnDeleteRowClicked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.Null(typeof(EntryRow).GetProperty("DeleteTip"));
        var template = (Grid)((Border)page.Find<CollectionView>("List").ItemTemplate.CreateContent()).Content;
        Assert.DoesNotContain(template.Children.OfType<ImageButton>(), b => b.Source is FileImageSource { File: "ic_trash_danger.png" });
        Assert.Equal(4, Rows(page).Count);
    }

    [Fact]
    public async Task Notas_seguras_con_un_trozo_solo_con_el_ajuste()
    {
        using var app = TestHost.Start();
        var nota = new Credential { Kind = EntryKind.Note, Title = "Wifi", Notes = "  red: casa\n\n\tclave:   secreta  " };
        await app.CreateVaultAsync("contraseña-larga", nota, new Credential { Title = "Web", Notes = "no es nota" });
        var page = new VaultPage();
        Assert.All(Rows(page), r => Assert.False(r.HasPreview));   // apagado por defecto

        app.Settings.NotePreview = true;
        await page.Appear();   // al volver de Ajustes, la lista lo recoge
        var row = Rows(page).Single(r => r.Title == "Wifi");
        Assert.Equal("red: casa clave: secreta", row.Preview);
        Assert.True(row.HasPreview);
        Assert.False(Rows(page).Single(r => r.Title == "Web").HasPreview);   // solo las notas seguras

        app.Store.Lock();   // bloqueada, la lista (y el trozo) desaparece
        Assert.Empty(Rows(page));
        await Ui.UnlockThrough(page, "contraseña-larga");
        await Ui.Until(() => Rows(page).Count == 2);

        app.Settings.NotePreview = false;
        await page.Appear();
        Assert.All(Rows(page), r => Assert.False(r.HasPreview));
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
        ((IMenuItemController)page.Find<ToolbarItem>("LockItem")).Activate();
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
