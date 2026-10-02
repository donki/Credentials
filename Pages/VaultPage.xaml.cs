using System.Collections.ObjectModel;
using System.ComponentModel;
using Credentials.Helpers;
using Credentials.Models;
using Credentials.Services;

namespace Credentials.Pages;

/// <summary>Una fila de la lista: la entrada, su icono y el codigo TOTP vivo si lo tiene.</summary>
public sealed class EntryRow(Credential entry, ILocalizationService l) : INotifyPropertyChanged
{
    public Credential Entry { get; } = entry;
    // Pistas de los botones de la fila (tooltip en Windows, pulsacion larga en Android).
    public string CopyUserTip => l["CopyUser"];
    public string CopyPasswordTip => l["CopyPassword"];
    public string CopyCodeTip => l["CopyCode"];
    public string DeleteTip => l["DeleteEntry"];
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
    private readonly IDialogService _dialogs;
    private readonly ObservableCollection<EntryRow> _rows = [];
    private string _search = string.Empty;
    private string _filter = VaultQuery.All;      // all | fav | kind:X | folder:X | tag:X
    private IDispatcherTimer? _tick;

    public VaultPage()
    {
        InitializeComponent();
        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _store = ServiceHelper.GetRequiredService<VaultStore>();
        _settings = ServiceHelper.GetRequiredService<ISettingsService>();
        _toast = ServiceHelper.GetRequiredService<IToastService>();
        _dialogs = ServiceHelper.GetRequiredService<IDialogService>();
        List.ItemsSource = _rows;
        _store.Changed += () => Dispatcher.RunOnUi(Refresh);
        // Al bloquearse (boton o inactividad) la lista se vacia y sale la pantalla de desbloqueo,
        // sin esperar a que el usuario toque nada.
        _store.Locked += () => Dispatcher.RunOnUi(async () =>
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
            if (row.HasCode)
                row.Code = VaultQuery.RowCode(row.Entry.Totp);
        }
    }

    // ------------------------------------------------------------------ lista y filtros

    private void Refresh()
    {
        if (!_store.IsUnlocked)
            return;
        var list = VaultQuery.Apply(_store.Data!.Entries, _filter, _search, _settings.SortMode);
        _rows.Clear();
        foreach (var e in list)
            _rows.Add(new EntryRow(e, _l));
        OnTick(null, EventArgs.Empty);
        CountLabel.Text = VaultQuery.CountText(list.Count, _l);
        BuildChips();
    }

    private void BuildChips()
    {
        Chips.Clear();
        foreach (var (key, text) in VaultQuery.Chips(_store.Data!, _l))
        {
            var b = new Button { Text = text, Style = (Style)Application.Current!.Resources[_filter == key ? "ChipOn" : "Chip"] };
            b.Clicked += (_, _) => { _filter = VaultQuery.Toggle(_filter, key); Refresh(); };
            Chips.Add(b);
        }
    }

    /// <summary>
    /// Atras en la boveda (inicio, Mobile 7): primero quita la busqueda y el filtro que haya; sin nada
    /// de eso, la aplicacion se oculta (no se cierra).
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        if (_search.Length > 0 || _filter != VaultQuery.All)
        {
            _filter = VaultQuery.All;
            if (_search.Length > 0)
                SearchEntry.Text = string.Empty;   // OnSearchChanged refresca
            else
                Refresh();
            SearchEntry.Unfocus();
            return true;
        }
#if ANDROID
        Platform.CurrentActivity?.MoveTaskToBack(true);
        return true;
#else
        return base.OnBackButtonPressed();
#endif
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
        var chosen = await _dialogs.ActionSheetAsync(this, _l["SortBy"], _l["Cancel"], options.Select(o => o.Item2).ToArray());
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
        var chosen = await _dialogs.ActionSheetAsync(this, _l["Add"], _l["Cancel"], labels);
        var index = Array.IndexOf(labels, chosen);
        if (index < 0)
            return;
        var entry = new Credential { Kind = kinds[index], Folder = VaultQuery.FolderOf(_filter) };
        await Navigation.PushAsync(new EntryPage(entry, isNew: true));
    }

    private async void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not EntryRow row)
            return;
        _store.Touch();
        await Navigation.PushAsync(new EntryPage(row.Entry, isNew: false));
    }

    /// <summary>Borrar desde la fila, con confirmacion: mismo borrado logico que en la ficha.</summary>
    private async void OnDeleteRowClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not EntryRow row)
            return;
        _store.Touch();
        var ok = await _dialogs.AlertAsync(this, _l["DeleteEntry"], string.Format(_l.CurrentCulture, _l["DeleteEntryConfirm"], row.Entry.Title), _l["Delete"], _l["Cancel"]);
        if (!ok)
            return;
        await _store.DeleteAsync(row.Entry.Id);
        _toast.Show(_l["EntryDeleted"]);
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
        var clipboard = ServiceHelper.GetRequiredService<IClipboard>();
        await clipboard.SetTextAsync(text);
        toast.Show(message);
        if (!sensitive || settings.ClipboardSeconds <= 0)
            return;
        var seconds = settings.ClipboardSeconds;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            try
            {
                if (await clipboard.GetTextAsync() == text)
                {
                    await clipboard.SetTextAsync(" ");
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

    /// <summary>La puerta que esta a la vista ahora mismo, si la hay.</summary>
    public static UnlockPage? Current { get; private set; }

    public static async Task<bool> EnsureUnlockedAsync(Page page)
    {
        var store = ServiceHelper.GetRequiredService<VaultStore>();
        if (store.IsUnlocked || await store.TryTrustedUnlockAsync())
            return true;
        if (_showing)
            return false;
        _showing = true;
        try
        {
            var unlock = new UnlockPage();
            Current = unlock;
            var tcs = new TaskCompletionSource();
            unlock.Disappearing += (_, _) => tcs.TrySetResult();
#if WINDOWS
            var restore = Compact(page.Window);
#endif
            await page.Navigation.PushModalAsync(unlock, animated: false);
            await tcs.Task;
#if WINDOWS
            restore?.Invoke();
#endif
        }
        finally
        {
            Current = null;
            _showing = false;
        }
        return store.IsUnlocked;
    }

#if WINDOWS
    /// <summary>
    /// En Windows, mientras se pide la contraseña, la ventana se queda pequeña y abajo a la derecha
    /// del escritorio (como un aviso del sistema). Devuelve lo que la deja como estaba.
    /// </summary>
    private static Action? Compact(Window? window)
    {
        try
        {
            if (window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native)
                return null;
            var app = native.AppWindow;
            var presenter = app.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
            var wasMaximized = presenter?.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;
            if (wasMaximized)
                presenter!.Restore();
            var saved = new global::Windows.Graphics.RectInt32(app.Position.X, app.Position.Y, app.Size.Width, app.Size.Height);
            PlaceSmall(native);
            _compactWindow = native;
            return () =>
            {
                _compactWindow = null;
                try
                {
                    if (ServiceHelper.GetRequiredService<VaultStore>().IsUnlocked)
                    {
                        // Desbloqueada: la ventana se va directamente a la barra de tareas y el
                        // tamaño de antes queda para cuando se restaure. Así no se ve crecer la
                        // ventana pequeña con la pantalla de la contraseña todavía dentro.
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
                        var display = Microsoft.UI.Windowing.DisplayArea.GetFromRect(saved, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                        Platforms.Windows.WindowPlacement.MinimizeWithRestoreBounds(hwnd, saved,
                            display.WorkArea.X - display.OuterBounds.X, display.WorkArea.Y - display.OuterBounds.Y, wasMaximized);
                        return;
                    }
                    app.MoveAndResize(saved);
                    if (wasMaximized)
                        presenter!.Maximize();
                }
                catch (Exception) { }
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>La ventana que está en pequeño pidiendo la contraseña, si la hay.</summary>
    private static Microsoft.UI.Xaml.Window? _compactWindow;

    /// <summary>Pequeña y abajo a la derecha de la zona de trabajo; el alto lo ajusta luego UnlockPage.</summary>
    private static void PlaceSmall(Microsoft.UI.Xaml.Window native)
    {
        var app = native.AppWindow;
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(app.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        var scale = native.Content?.XamlRoot?.RasterizationScale ?? 1.0;
        int w = (int)(420 * scale), h = (int)(340 * scale), margin = (int)(12 * scale);
        app.MoveAndResize(new global::Windows.Graphics.RectInt32(area.X + area.Width - w - margin, area.Y + area.Height - h - margin, w, h));
    }

    /// <summary>
    /// Vuelve a dejar en pequeño la ventana que pide la contraseña. Hace falta al volver de la
    /// bandeja: al bloquear Windows (Win+L) la bóveda se cierra y la puerta se prepara con la ventana
    /// escondida; al volver al escritorio, sacarla de la bandeja la restauraba a su tamaño de antes
    /// (incluso maximizada) con la contraseña dentro.
    /// </summary>
    public static void Recompact()
    {
        try
        {
            if (Current is null || _compactWindow is not { } native)
                return;
            if (native.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Maximized } presenter)
                presenter.Restore();
            PlaceSmall(native);
            Current.RefitHeight();
        }
        catch (Exception) { }
    }
#endif
}
