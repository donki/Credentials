using System.Collections.ObjectModel;
using System.ComponentModel;
using Credentials.Helpers;
using Credentials.Models;
using Credentials.Services;
using SocShared;

namespace Credentials.Pages;

/// <summary>Una fila de la lista: la entrada, su icono y el codigo TOTP vivo si lo tiene.</summary>
public sealed class EntryRow(Credential entry) : INotifyPropertyChanged
{
    public Credential Entry { get; } = entry;
    public string Title => Entry.Title;
    public string Subtitle => Entry.Kind switch
    {
        EntryKind.Note => Entry.Folder.Length > 0 ? Entry.Folder : string.Empty,
        _ => string.Join(" · ", new[] { Entry.Username, Entry.Host }.Where(s => s.Length > 0)),
    };
    public string Icon => Entry.Kind switch
    {
        EntryKind.App => "ic_app.png",
        EntryKind.Totp => "ic_totp.png",
        EntryKind.Note => "ic_note.png",
        _ => "ic_web.png",
    };
    public bool HasUser => Entry.Username.Length > 0;
    public bool HasPassword => Entry.HasPassword;
    public bool HasCode => Entry.HasTotp;

    private string _code = string.Empty;
    public string Code
    {
        get => _code;
        set { if (_code == value) return; _code = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// La boveda: buscador, filtros por favoritas, clase, carpeta y etiqueta, y las entradas con
/// copiar usuario / contraseña / codigo desde la fila. Los codigos TOTP se refrescan cada segundo
/// mientras la pagina esta a la vista.
/// </summary>
public partial class VaultPage : ContentPage
{
    private readonly ILocalizationService _l;
    private readonly VaultStore _store;
    private readonly ISettingsService _settings;
    private readonly IToastService _toast;
    private readonly ObservableCollection<EntryRow> _rows = [];
    private string _search = string.Empty;
    private string _filter = "all";      // all | fav | kind:X | folder:X | tag:X
    private IDispatcherTimer? _tick;

    public VaultPage()
    {
        InitializeComponent();
        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _store = ServiceHelper.GetRequiredService<VaultStore>();
        _settings = ServiceHelper.GetRequiredService<ISettingsService>();
        _toast = ServiceHelper.GetRequiredService<IToastService>();
        List.ItemsSource = _rows;
        _store.Changed += () => MainThread.BeginInvokeOnMainThread(Refresh);
        // Al bloquearse (boton o inactividad) la lista se vacia y sale la pantalla de desbloqueo,
        // sin esperar a que el usuario toque nada.
        _store.Locked += () => MainThread.BeginInvokeOnMainThread(async () =>
        {
            _rows.Clear();
            if (await Gate.EnsureUnlockedAsync(this))
                Refresh();
        });
        _l.LanguageChanged += (_, _) => ApplyTexts();
        ApplyTexts();
    }

    private void ApplyTexts()
    {
        Title = _l["MenuVault"];
        SearchEntry.Placeholder = _l["SearchPlaceholder"];
        EmptyLabel.Text = _l["NoEntries"];
        EmptyHint.Text = _l["NoEntriesHint"];
        foreach (var (b, k) in new[] { (SortButton, "SortBy"), (LockButton, "Lock"), (AddButton, "Add") })
        {
            SemanticProperties.SetDescription(b, _l[k]);
            ToolTipProperties.SetText(b, _l[k]);
        }
        Refresh();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!await Gate.EnsureUnlockedAsync(this))
            return;
        Refresh();
        _tick ??= Dispatcher.CreateTimer();
        _tick.Interval = TimeSpan.FromSeconds(1);
        _tick.Tick -= OnTick;
        _tick.Tick += OnTick;
        _tick.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _tick?.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_store.LockIfIdle())
            return;
        foreach (var row in _rows)
        {
            if (!row.HasCode)
                continue;
            var t = Totp.Parse(row.Entry.Totp);
            if (t is null) { row.Code = string.Empty; continue; }
            var (code, left) = t.Now();
            row.Code = $"{code[..(code.Length / 2)]} {code[(code.Length / 2)..]} · {left}";
        }
    }

    // ------------------------------------------------------------------ lista y filtros

    private void Refresh()
    {
        if (!_store.IsUnlocked)
            return;
        var entries = _store.Data!.Entries.Where(e => !e.Deleted);
        entries = _filter switch
        {
            "fav" => entries.Where(e => e.Favorite),
            var f when f.StartsWith("kind:") => entries.Where(e => e.Kind.ToString() == f[5..]),
            var f when f.StartsWith("folder:") => entries.Where(e => string.Equals(e.Folder, f[7..], StringComparison.OrdinalIgnoreCase)),
            var f when f.StartsWith("tag:") => entries.Where(e => e.Tags.Contains(f[4..], StringComparer.OrdinalIgnoreCase)),
            _ => entries,
        };
        if (_search.Length > 0)
        {
            var q = _search.Trim();
            entries = entries.Where(e => e.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                      || e.Username.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                      || e.Url.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                      || e.Notes.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                      || e.Tags.Any(t => t.Contains(q, StringComparison.CurrentCultureIgnoreCase)));
        }
        entries = _settings.SortMode switch
        {
            "modified" => entries.OrderByDescending(e => e.ModifiedAt),
            "created" => entries.OrderByDescending(e => e.CreatedAt),
            _ => entries.OrderByDescending(e => e.Favorite).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase),
        };
        var list = entries.ToList();
        _rows.Clear();
        foreach (var e in list)
            _rows.Add(new EntryRow(e));
        OnTick(null, EventArgs.Empty);
        CountLabel.Text = list.Count == 1 ? _l["OneEntry"] : string.Format(_l.CurrentCulture, _l["EntriesCount"], list.Count);
        BuildChips();
    }

    private void BuildChips()
    {
        Chips.Clear();
        var all = _store.Data!.Entries.Where(e => !e.Deleted).ToList();
        void Chip(string key, string text)
        {
            var on = _filter == key;
            var b = new Button { Text = text, Style = (Style)Application.Current!.Resources[on ? "ChipOn" : "Chip"] };
            b.Clicked += (_, _) => { _filter = on ? "all" : key; Refresh(); };
            Chips.Add(b);
        }
        Chip("all", _l["AllEntries"]);
        if (all.Any(e => e.Favorite))
            Chip("fav", "★ " + _l["Favorites"]);
        foreach (var kind in all.Select(e => e.Kind).Distinct().OrderBy(k => k))
            Chip("kind:" + kind, _l["Kind" + kind]);
        foreach (var folder in all.Select(e => e.Folder).Concat(_store.Data.Folders).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f))
            Chip("folder:" + folder, "📁 " + folder);
        foreach (var tag in all.SelectMany(e => e.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t))
            Chip("tag:" + tag, "#" + tag);
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _search = e.NewTextValue ?? string.Empty;
        _store.Touch();
        Refresh();
    }

    private async void OnSortClicked(object? sender, EventArgs e)
    {
        var options = new[] { ("title", _l["SortTitle"]), ("modified", _l["SortModified"]), ("created", _l["SortCreated"]) };
        var chosen = await ModernDialog.ActionSheetAsync(this, _l["SortBy"], _l["Cancel"], options.Select(o => o.Item2).ToArray());
        var pick = options.FirstOrDefault(o => o.Item2 == chosen);
        if (pick.Item1 is null)
            return;
        _settings.SortMode = pick.Item1;
        Refresh();
    }

    private void OnLockClicked(object? sender, EventArgs e) => _store.Lock();

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        var kinds = new[] { EntryKind.Login, EntryKind.App, EntryKind.Totp, EntryKind.Note };
        var labels = kinds.Select(k => _l["Kind" + k]).ToArray();
        var chosen = await ModernDialog.ActionSheetAsync(this, _l["Add"], _l["Cancel"], labels);
        var index = Array.IndexOf(labels, chosen);
        if (index < 0)
            return;
        var entry = new Credential { Kind = kinds[index] };
        if (_filter.StartsWith("folder:"))
            entry.Folder = _filter[7..];
        await Navigation.PushAsync(new EntryPage(entry, isNew: true));
    }

    private async void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not EntryRow row)
            return;
        _store.Touch();
        await Navigation.PushAsync(new EntryPage(row.Entry, isNew: false));
    }

    // ------------------------------------------------------------------ copiar desde la fila

    private async void OnCopyUserClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is EntryRow row)
            await ClipboardHelper.CopyAsync(row.Entry.Username, _settings, _toast, _l["CopiedUser"], _l["ClipboardCleared"], sensitive: false);
    }

    private async void OnCopyPasswordClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is EntryRow row)
            await ClipboardHelper.CopyAsync(row.Entry.Password, _settings, _toast, _l["CopiedPassword"], _l["ClipboardCleared"], sensitive: true);
    }

    private async void OnCopyCodeClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is EntryRow row && Totp.Parse(row.Entry.Totp) is { } t)
            await ClipboardHelper.CopyAsync(t.Now().Code, _settings, _toast, _l["CopiedCode"], _l["ClipboardCleared"], sensitive: true);
    }
}

/// <summary>Portapapeles con vaciado automatico para lo sensible (contraseñas, codigos).</summary>
public static class ClipboardHelper
{
    public static async Task CopyAsync(string text, ISettingsService settings, IToastService toast, string message, string clearedMessage, bool sensitive)
    {
        if (text.Length == 0)
            return;
        await Clipboard.Default.SetTextAsync(text);
        toast.Show(message);
        if (!sensitive || settings.ClipboardSeconds <= 0)
            return;
        var seconds = settings.ClipboardSeconds;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            try
            {
                if (await Clipboard.Default.GetTextAsync() == text)
                {
                    await Clipboard.Default.SetTextAsync(" ");
                    toast.Show(clearedMessage);
                }
            }
            catch (Exception) { }
        });
    }
}

/// <summary>Si la boveda esta bloqueada, saca la puerta de desbloqueo encima de la pagina.</summary>
public static class Gate
{
    private static bool _showing;

    public static async Task<bool> EnsureUnlockedAsync(Page page)
    {
        var store = ServiceHelper.GetRequiredService<VaultStore>();
        if (store.IsUnlocked)
            return true;
        if (_showing)
            return false;
        _showing = true;
        try
        {
            var unlock = new UnlockPage();
            var tcs = new TaskCompletionSource();
            unlock.Disappearing += (_, _) => tcs.TrySetResult();
            await page.Navigation.PushModalAsync(unlock, animated: false);
            await tcs.Task;
        }
        finally
        {
            _showing = false;
        }
        return store.IsUnlocked;
    }
}
