using Credentials.Helpers;
using Credentials.Models;
using Credentials.Services;
using SocShared;

namespace Credentials.Pages;

/// <summary>
/// Una entrada: ver, editar, copiar, generar contraseña, dar de alta el segundo factor (a mano o
/// con el QR) y ver el codigo vivo. Se trabaja sobre una copia y solo se escribe en la boveda al
/// guardar; el borrado es logico, para que llegue a los demas dispositivos.
/// </summary>
public partial class EntryPage : ContentPage
{
    private readonly ILocalizationService _l;
    private readonly VaultStore _store;
    private readonly ISettingsService _settings;
    private readonly IToastService _toast;
    private readonly Credential _original;
    private readonly Credential _entry;
    private readonly bool _isNew;
    private bool _dirty;
    private IDispatcherTimer? _tick;
    private readonly List<(Entry field, Entry value, CheckBox hidden)> _fieldRows = [];

    public EntryPage(Credential entry, bool isNew)
    {
        InitializeComponent();
        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _store = ServiceHelper.GetRequiredService<VaultStore>();
        _settings = ServiceHelper.GetRequiredService<ISettingsService>();
        _toast = ServiceHelper.GetRequiredService<IToastService>();
        _original = entry;
        _entry = entry.Clone();
        _isNew = isNew;
        ApplyTexts();
        Fill();
        _dirty = false;
    }

    private void ApplyTexts()
    {
        Title = _isNew ? _l["Add"] : _entry.Title;
        KindLabel.Text = _l["Kind" + _entry.Kind];
        TitleTitle.Text = _l["Title"];
        UsernameTitle.Text = _entry.Kind == EntryKind.Totp ? _l["Username"] : _l["Username"];
        PasswordTitle.Text = _l["Password"];
        UrlTitle.Text = _l["Url"];
        GeneratorTitle.Text = _l["Generator"];
        UpperLabel.Text = _l["Uppercase"];
        LowerLabel.Text = _l["Lowercase"];
        DigitsLabel.Text = _l["Digits"];
        SymbolsLabel.Text = _l["Symbols"];
        AmbiguousLabel.Text = _l["AvoidAmbiguous"];
        RegenerateButton.Text = _l["Generate"];
        UseButton.Text = _l["Use"];
        TotpTitle.Text = _l["TotpSection"];
        TotpHint.Text = _l["TotpNone"];
        TotpEntry.Placeholder = _l["TotpSecret"];
        SeedTitle.Text = _l["TotpSeed"];
        SeedQrHint.Text = _l["TotpQrHint"];
        RecoveryTitle.Text = _l["RecoveryTitle"];
        RecoveryEditor.Placeholder = _l["RecoveryPlaceholder"];
        RecoveryAddButton.Text = _l["RecoveryAdd"];
        ToolTipProperties.SetText(SeedButton, _l["TotpShowSeed"]);
        ToolTipProperties.SetText(RecoveryEye, _l["RecoveryShow"]);
        FolderTitle.Text = _l["Folder"];
        TagsTitle.Text = _l["Tags"];
        TagsEntry.Placeholder = _l["TagsHint"];
        NotesTitle.Text = _l["Notes"];
        FieldsTitle.Text = _l["Fields"];
        HistoryTitle.Text = _l["History"];
        SaveButton.Text = _l["Save"];
        ToolTipProperties.SetText(ScanButton, _l["TotpScan"]);
        ToolTipProperties.SetText(DeleteButton, _l["DeleteEntry"]);

        // Que campos enseña cada clase de entrada.
        var login = _entry.Kind is EntryKind.Login or EntryKind.App;
        UsernameTitle.IsVisible = UsernameRow.IsVisible = _entry.Kind != EntryKind.Note;
        PasswordTitle.IsVisible = PasswordRow.IsVisible = StrengthRow.IsVisible = login;
        UrlTitle.IsVisible = UrlRow.IsVisible = login;
        TotpCard.IsVisible = _entry.Kind != EntryKind.Note;
        DeleteButton.IsVisible = !_isNew;
    }

    private void Fill()
    {
        TitleEntry.Text = _entry.Title;
        UsernameEntry.Text = _entry.Username;
        PasswordEntry.Text = _entry.Password;
        UrlEntry.Text = _entry.Url;
        FolderEntry.Text = _entry.Folder;
        TagsEntry.Text = string.Join(", ", _entry.Tags);
        NotesEditor.Text = _entry.Notes;
        FavoriteButton.Source = _entry.Favorite ? "ic_star_on.png" : "ic_star.png";
        UpdateStrength();
        UpdateTotp();
        RenderRecovery();
        FieldsBox.Clear();
        _fieldRows.Clear();
        foreach (var f in _entry.Fields)
            AddFieldRow(f.Name, f.Value, f.Hidden);
        HistoryCard.IsVisible = _entry.History.Count > 0;
        HistoryBox.Clear();
        foreach (var h in _entry.History.OrderByDescending(h => h.ChangedAt).Take(10))
        {
            var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)] };
            row.Add(new Label { Text = $"{h.ChangedAt.LocalDateTime:g} · {new string('•', Math.Min(12, h.Password.Length))}", Style = (Style)Application.Current!.Resources["HintText"], VerticalOptions = LayoutOptions.Center });
            var copy = new ImageButton { Style = (Style)Application.Current.Resources["RowIconButton"], Source = "ic_copy.png" };
            var password = h.Password;
            copy.Clicked += async (_, _) => await ClipboardHelper.CopyAsync(password, _settings, _toast, _l["CopiedPassword"], _l["ClipboardCleared"], sensitive: true);
            row.Add(copy, 1);
            HistoryBox.Add(row);
        }
        var ci = _l.CurrentCulture;
        DatesLabel.Text = _isNew ? string.Empty : $"{string.Format(ci, _l["Created"], _entry.CreatedAt.LocalDateTime.ToString("g", ci))} · {string.Format(ci, _l["Modified"], _entry.ModifiedAt.LocalDateTime.ToString("g", ci))}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!await Gate.EnsureUnlockedAsync(this))
            return;
        _tick ??= Dispatcher.CreateTimer();
        _tick.Interval = TimeSpan.FromSeconds(1);
        _tick.Tick -= OnTick;
        _tick.Tick += OnTick;
        _tick.Start();
        if (_isNew)
            TitleEntry.Focus();
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
        if (!_entry.HasTotp || Totp.Parse(_entry.Totp) is not { } t)
            return;
        var (code, left) = t.Now();
        TotpCode.Text = $"{code[..(code.Length / 2)]} {code[(code.Length / 2)..]}";
        TotpProgress.Progress = t.IsCounter ? 1 : left / (double)t.Period;
    }

    // ------------------------------------------------------------------ cambios

    private void OnAnyChanged(object? sender, EventArgs e)
    {
        _dirty = true;
        _store.Touch();
    }

    private void OnPasswordChanged(object? sender, TextChangedEventArgs e)
    {
        OnAnyChanged(sender, e);
        UpdateStrength();
    }

    private void UpdateStrength()
    {
        var s = PasswordGenerator.Strength(PasswordEntry.Text ?? string.Empty);
        StrengthBar.Progress = (PasswordEntry.Text ?? string.Empty).Length == 0 ? 0 : (s + 1) / 5.0;
        StrengthBar.ProgressColor = s switch { 0 => Color.FromArgb("#BA1A1A"), 1 => Color.FromArgb("#D97706"), 2 => Color.FromArgb("#CA8A04"), 3 => Color.FromArgb("#0E9F6E"), _ => Color.FromArgb("#059669") };
        StrengthLabel.Text = (PasswordEntry.Text ?? string.Empty).Length == 0 ? string.Empty : _l["Strength" + s];
    }

    private void OnFavoriteClicked(object? sender, EventArgs e)
    {
        _entry.Favorite = !_entry.Favorite;
        FavoriteButton.Source = _entry.Favorite ? "ic_star_on.png" : "ic_star.png";
        _dirty = true;
    }

    private void OnEyeClicked(object? sender, EventArgs e)
    {
        PasswordEntry.IsPassword = !PasswordEntry.IsPassword;
        EyeButton.Source = PasswordEntry.IsPassword ? "ic_eye.png" : "ic_eye_off.png";
    }

    private async void OnCopyUserClicked(object? sender, EventArgs e) => await ClipboardHelper.CopyAsync(UsernameEntry.Text ?? string.Empty, _settings, _toast, _l["CopiedUser"], _l["ClipboardCleared"], sensitive: false);
    private async void OnCopyPasswordClicked(object? sender, EventArgs e) => await ClipboardHelper.CopyAsync(PasswordEntry.Text ?? string.Empty, _settings, _toast, _l["CopiedPassword"], _l["ClipboardCleared"], sensitive: true);

    private async void OnOpenUrlClicked(object? sender, EventArgs e)
    {
        var url = (UrlEntry.Text ?? string.Empty).Trim();
        if (url.Length == 0)
            return;
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url;
        try { await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred); }
        catch (Exception ex) { await ModernDialog.AlertAsync(this, _l["Error"], ex.Message, _l["Ok"]); }
    }

    // ------------------------------------------------------------------ generador

    private void OnGenerateClicked(object? sender, EventArgs e)
    {
        GeneratorCard.IsVisible = !GeneratorCard.IsVisible;
        if (GeneratorCard.IsVisible)
            OnGeneratorChanged(sender, e);
    }

    private void OnGeneratorChanged(object? sender, EventArgs e)
    {
        var length = (int)Math.Round(LengthSlider.Value);
        LengthLabel.Text = string.Format(_l.CurrentCulture, _l["Length"], length);
        GeneratedLabel.Text = PasswordGenerator.Generate(length, UpperCheck.IsChecked, LowerCheck.IsChecked, DigitsCheck.IsChecked, SymbolsCheck.IsChecked, AmbiguousCheck.IsChecked);
    }

    private void OnUseGeneratedClicked(object? sender, EventArgs e)
    {
        PasswordEntry.Text = GeneratedLabel.Text;
        PasswordEntry.IsPassword = false;
        EyeButton.Source = "ic_eye_off.png";
        GeneratorCard.IsVisible = false;
        _dirty = true;
    }

    // ------------------------------------------------------------------ segundo factor

    private void UpdateTotp()
    {
        var t = _entry.HasTotp ? Totp.Parse(_entry.Totp) : null;
        TotpLive.IsVisible = t is not null;
        TotpHint.IsVisible = t is null;
        TotpEntry.IsVisible = ScanButton.IsVisible = t is null;
        if (t is not null)
        {
            TotpIssuer.Text = string.Join(" · ", new[] { t.Issuer, t.Account }.Where(s => s.Length > 0)) + $"  ({t.Algorithm}, {t.Digits}, {t.Period}s)";
            OnTick(null, EventArgs.Empty);
        }
        UpdateSeed();
    }

    private void OnTotpEntered(object? sender, EventArgs e) => ApplyTypedTotp();

    // ------------------------------------------------------------------ semilla

    private bool _seedVisible;

    /// <summary>El ojo del segundo factor: enseña (o esconde) la clave secreta, para llevarla a otra app.</summary>
    private void OnSeedClicked(object? sender, EventArgs e)
    {
        _seedVisible = !_seedVisible;
        UpdateSeed();
    }

    private void UpdateSeed()
    {
        var t = _entry.HasTotp ? Totp.Parse(_entry.Totp) : null;
        SeedRow.IsVisible = _seedVisible && t is not null;
        SeedQrBox.IsVisible = SeedRow.IsVisible;
        // El QR lleva el enlace otpauth entero (emisor, cuenta, algoritmo, dígitos y periodo), como el
        // que enseñan los sitios: cualquier app de autenticación lo da de alta igual.
        SeedQr.Value = SeedRow.IsVisible ? t!.ToUri() : null;
        SeedButton.Source = _seedVisible ? "ic_eye_off.png" : "ic_eye.png";
        // En grupos de cuatro, como la dan los sitios: se lee y se teclea mejor.
        SeedLabel.Text = t is null ? string.Empty : string.Join(" ", t.Secret.TrimEnd('=').Chunk(4).Select(c => new string(c)));
    }

    private async void OnCopySeedClicked(object? sender, EventArgs e)
    {
        if (Totp.Parse(_entry.Totp) is { } t)
            await ClipboardHelper.CopyAsync(t.Secret, _settings, _toast, _l["CopiedSeed"], _l["ClipboardCleared"], sensitive: true);
    }

    // ------------------------------------------------------------------ codigos de respaldo

    private bool _recoveryVisible;

    private void OnRecoveryEyeClicked(object? sender, EventArgs e)
    {
        _recoveryVisible = !_recoveryVisible;
        RenderRecovery();
    }

    /// <summary>Lo pegado en la casilla pasa a la lista: uno por linea (o separados por comas), sin repetidos.</summary>
    private bool AddTypedRecovery()
    {
        var text = RecoveryEditor.Text ?? string.Empty;
        var codes = text.Split(['\n', '\r', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(c => c.Length > 0)
            .ToList();
        if (codes.Count == 0)
            return false;
        foreach (var code in codes)
            if (!_entry.RecoveryCodes.Any(r => r.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                _entry.RecoveryCodes.Add(new RecoveryCode { Code = code });
        RecoveryEditor.Text = string.Empty;
        _dirty = true;
        _recoveryVisible = true;
        RenderRecovery();
        return true;
    }

    private void OnAddRecoveryClicked(object? sender, EventArgs e) => AddTypedRecovery();

    private void RenderRecovery()
    {
        var total = _entry.RecoveryCodes.Count;
        var unused = _entry.RecoveryCodes.Count(r => !r.Used);
        RecoveryHint.Text = total == 0
            ? _l["RecoveryNone"]
            : string.Format(_l.CurrentCulture, _l["RecoveryCount"], total, unused);
        RecoveryEye.IsVisible = total > 0;
        RecoveryEye.Source = _recoveryVisible ? "ic_eye_off.png" : "ic_eye.png";
        RecoveryBox.IsVisible = _recoveryVisible && total > 0;
        RecoveryBox.Clear();
        if (!RecoveryBox.IsVisible)
            return;
        var resources = Application.Current!.Resources;
        foreach (var r in _entry.RecoveryCodes)
        {
            var item = r;
            var row = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
                ColumnSpacing = 4,
            };
            var code = new Label
            {
                Text = item.Code,
                FontFamily = "Consolas",
                FontSize = 15,
                VerticalOptions = LayoutOptions.Center,
                TextDecorations = item.Used ? TextDecorations.Strikethrough : TextDecorations.None,
                Opacity = item.Used ? 0.5 : 1,
            };
            code.SetAppThemeColor(Label.TextColorProperty, (Color)resources["TextPrimaryLight"], (Color)resources["TextPrimaryDark"]);
            row.Add(code, 0);
            // Casilla «usado»: el codigo sigue en la lista, tachado, para saber cuales quedan.
            var used = new CheckBox { IsChecked = item.Used, Color = (Color)resources["Primary"], VerticalOptions = LayoutOptions.Center };
            ToolTipProperties.SetText(used, _l["RecoveryUsed"]);
            used.CheckedChanged += (_, a) => { item.Used = a.Value; _dirty = true; RenderRecovery(); };
            row.Add(used, 1);
            var copy = new ImageButton { Style = (Style)resources["RowIconButton"], Source = "ic_copy.png" };
            copy.Clicked += async (_, _) => await ClipboardHelper.CopyAsync(item.Code, _settings, _toast, _l["CopiedRecovery"], _l["ClipboardCleared"], sensitive: true);
            row.Add(copy, 2);
            var remove = new ImageButton { Style = (Style)resources["RowIconButton"], Source = "ic_trash.png" };
            remove.Clicked += (_, _) => { _entry.RecoveryCodes.Remove(item); _dirty = true; RenderRecovery(); };
            row.Add(remove, 3);
            RecoveryBox.Add(row);
        }
    }

    /// <summary>Al salir de la casilla tambien se aplica: pegar el secreto y no pulsar Intro era lo normal.</summary>
    private void OnTotpUnfocused(object? sender, FocusEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TotpEntry.Text))
            ApplyTypedTotp();
    }

    /// <summary>Lo escrito en la casilla del secreto pasa a la entrada. False si no es un secreto valido.</summary>
    private bool ApplyTypedTotp()
    {
        var t = Totp.Parse(TotpEntry.Text ?? string.Empty);
        if (t is null)
        {
            TotpError.Text = _l["TotpInvalid"];
            TotpError.IsVisible = true;
            return false;
        }
        TotpError.IsVisible = false;
        _entry.Totp = t.ToUri();
        TotpEntry.Text = string.Empty;
        _dirty = true;
        UpdateTotp();
        return true;
    }

    private void OnRemoveTotpClicked(object? sender, EventArgs e)
    {
        _entry.Totp = string.Empty;
        _dirty = true;
        UpdateTotp();
    }

    private async void OnCopyCodeClicked(object? sender, EventArgs e)
    {
        if (Totp.Parse(_entry.Totp) is { } t)
            await ClipboardHelper.CopyAsync(t.Now().Code, _settings, _toast, _l["CopiedCode"], _l["ClipboardCleared"], sensitive: true);
    }

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        var result = await ScanPage.ScanAsync(this, _l);
        if (result is null)
            return;
        TotpEntry.Text = result;
        OnTotpEntered(sender, e);
    }

    // ------------------------------------------------------------------ campos extra

    private void OnAddFieldClicked(object? sender, EventArgs e)
    {
        AddFieldRow(string.Empty, string.Empty, false);
        _dirty = true;
    }

    private void AddFieldRow(string name, string value, bool hidden)
    {
        var row = new Grid { ColumnDefinitions = [new ColumnDefinition(new GridLength(2, GridUnitType.Star)), new ColumnDefinition(new GridLength(3, GridUnitType.Star)), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 6 };
        var nameEntry = new Entry { Text = name, Placeholder = _l["FieldName"], Style = (Style)Application.Current!.Resources["Field"] };
        var valueEntry = new Entry { Text = value, Placeholder = _l["FieldValue"], IsPassword = hidden, Style = (Style)Application.Current.Resources["Field"] };
        var hiddenCheck = new CheckBox { IsChecked = hidden, VerticalOptions = LayoutOptions.Center, Color = (Color)Application.Current.Resources["Primary"] };
        ToolTipProperties.SetText(hiddenCheck, _l["FieldHidden"]);
        hiddenCheck.CheckedChanged += (_, a) => { valueEntry.IsPassword = a.Value; _dirty = true; };
        nameEntry.TextChanged += OnAnyChanged;
        valueEntry.TextChanged += OnAnyChanged;
        var remove = new ImageButton { Style = (Style)Application.Current.Resources["RowIconButton"], Source = "ic_trash.png" };
        row.Add(nameEntry, 0);
        row.Add(valueEntry, 1);
        row.Add(hiddenCheck, 2);
        row.Add(remove, 3);
        var tuple = (nameEntry, valueEntry, hiddenCheck);
        _fieldRows.Add(tuple);
        remove.Clicked += (_, _) => { FieldsBox.Remove(row); _fieldRows.Remove(tuple); _dirty = true; };
        FieldsBox.Add(row);
    }

    // ------------------------------------------------------------------ guardar y borrar

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var title = (TitleEntry.Text ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            await ModernDialog.AlertAsync(this, _l["Error"], _l["TitleRequired"], _l["Ok"]);
            return;
        }
        // Un secreto de doble factor pegado sin pulsar Intro se aplica ahora; antes se perdia sin avisar.
        if (!string.IsNullOrWhiteSpace(TotpEntry.Text) && !ApplyTypedTotp())
        {
            TotpEntry.Focus();
            return;
        }
        if (!string.IsNullOrWhiteSpace(RecoveryEditor.Text))
            AddTypedRecovery();
        var newPassword = PasswordEntry.Text ?? string.Empty;
        if (!_isNew && _original.Password.Length > 0 && newPassword != _original.Password)
            _entry.History.Insert(0, new PasswordHistoryItem(_original.Password, _original.ModifiedAt));
        _entry.Title = title;
        _entry.Username = (UsernameEntry.Text ?? string.Empty).Trim();
        _entry.Password = newPassword;
        _entry.Url = (UrlEntry.Text ?? string.Empty).Trim();
        _entry.Folder = (FolderEntry.Text ?? string.Empty).Trim().Trim('/');
        _entry.Tags = (TagsEntry.Text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _entry.Notes = NotesEditor.Text ?? string.Empty;
        _entry.Fields = _fieldRows.Where(r => (r.field.Text ?? string.Empty).Trim().Length > 0 || (r.value.Text ?? string.Empty).Length > 0)
            .Select(r => new CustomField { Name = (r.field.Text ?? string.Empty).Trim(), Value = r.value.Text ?? string.Empty, Hidden = r.hidden.IsChecked }).ToList();
        _entry.ModifiedAt = DateTimeOffset.UtcNow;

        var data = _store.Data!;
        var index = data.Entries.FindIndex(x => x.Id == _entry.Id);
        if (index >= 0)
            data.Entries[index] = _entry;
        else
            data.Entries.Add(_entry);
        if (_entry.Folder.Length > 0 && !data.Folders.Contains(_entry.Folder, StringComparer.OrdinalIgnoreCase))
            data.Folders.Add(_entry.Folder);
        await _store.SaveAsync();
        _dirty = false;
        _toast.Show(_l["Saved"]);
        await Navigation.PopAsync();
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        var ok = await ModernDialog.AlertAsync(this, _l["DeleteEntry"], string.Format(_l.CurrentCulture, _l["DeleteEntryConfirm"], _entry.Title), _l["Delete"], _l["Cancel"]);
        if (!ok)
            return;
        // Borrado logico (VaultStore.DeleteAsync): la baja tiene que llegar a los demas dispositivos al mezclar.
        await _store.DeleteAsync(_entry.Id);
        await Navigation.PopAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_dirty)
            return base.OnBackButtonPressed();
        Dispatcher.Dispatch(async () =>
        {
            if (await ModernDialog.AlertAsync(this, _l["Save"], _l["SaveChangesQuestion"], _l["Save"], _l["Cancel"]))
                OnSaveClicked(this, EventArgs.Empty);
            else
                await Navigation.PopAsync();
        });
        return true;
    }
}
