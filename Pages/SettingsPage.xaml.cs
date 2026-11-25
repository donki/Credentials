using System.Text;
using Credentials.Helpers;
using Credentials.Services;

namespace Credentials.Pages;

/// <summary>
/// Ajustes: donde vive la boveda (local, Google Drive, OneDrive) con la entrada en la cuenta y la
/// sincronizacion; seguridad (biometria, bloqueo por inactividad, portapapeles, contraseña
/// maestra); importar y exportar; idioma; y borrar la boveda local.
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly ILocalizationService _l;
    private readonly ISettingsService _settings;
    private readonly VaultStore _store;
    private readonly IBiometric _biometric;
    private readonly IToastService _toast;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IDesktopIntegration _desktop;
    private readonly IAndroidIntegration _android;
    private bool _loading;
    private static readonly int[] LockMinutes = SettingsRules.LockMinutes;
    private static readonly int[] ClipSeconds = SettingsRules.ClipSeconds;

    public SettingsPage()
    {
        InitializeComponent();
        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _settings = ServiceHelper.GetRequiredService<ISettingsService>();
        _store = ServiceHelper.GetRequiredService<VaultStore>();
        _biometric = ServiceHelper.GetRequiredService<IBiometric>();
        _toast = ServiceHelper.GetRequiredService<IToastService>();
        _dialogs = ServiceHelper.GetRequiredService<IDialogService>();
        _navigation = ServiceHelper.GetRequiredService<INavigationService>();
        _desktop = ServiceHelper.GetRequiredService<IDesktopIntegration>();
        _android = ServiceHelper.GetRequiredService<IAndroidIntegration>();
        _store.Status += s => Dispatcher.RunOnUi(() => ShowStatus(s));
        _l.LanguageChanged += (_, _) => ApplyTexts();
        ApplyTexts();
    }

    private void ApplyTexts()
    {
        _loading = true;
        Title = _l["SettingsTitle"];
        LanguageTitle.Text = _l["SettingsLanguage"];
        LanguageHint.Text = _l["AboutLanguageHint"];
        var isSpanish = _l.CurrentLanguage == "es";
        SpanishButton.Style = LookupStyle(isSpanish ? "PrimaryButton" : "OutlineButton");
        EnglishButton.Style = LookupStyle(isSpanish ? "OutlineButton" : "PrimaryButton");
        StorageTitle.Text = _l["StorageTitle"];
        StorageHint.Text = _l["StorageHint"];
        WindowsTitle.Text = _l["WindowsSection"];
        TrayLabel.Text = _l["TrayOnMinimize"];
        TrayHint.Text = _l["TrayOnMinimizeHint"];
        StartupLabel.Text = _l["StartWithWindows"];
        StartupHint.Text = _l["StartWithWindowsHint"];
        TraySwitch.IsToggled = _settings.TrayOnMinimize;
        if (_desktop.IsAvailable)
        {
            WindowsCard.IsVisible = true;
            StartupSwitch.IsToggled = _desktop.StartWithWindows;
            ExtTitle.Text = _l["ExtSection"];
            ExtHint.Text = _desktop.ExtensionsPackaged ? _l["ExtPackagedHint"] : _l["ExtSectionHint"];
            ExtGetExeButton.Text = _l["ExtGetExe"];
            ExtAskLabel.Text = _l["ExtAskOnUnlock"];
            ExtAskHint.Text = _l["ExtAskOnUnlockHint"];
            ExtAskSwitch.IsToggled = _settings.AskExtensions;
            RefreshBrowsers();
        }
        RefreshAutofill();
        SyncButton.Text = _l["SyncNow"];
        SignOutButton.Text = _l["SignOut"];
        SecurityTitle.Text = _l["SecurityTitle"];
        TrustLabel.Text = _l["TrustDevice"];
        TrustHint.Text = _l["TrustDeviceHint"];
        BiometricsLabel.Text = _l["Biometrics"];
        BiometricsHint.Text = _l["BiometricsHint"];
        ScreenshotsLabel.Text = _l["AllowScreenshots"];
        ScreenshotsHint.Text = _l["AllowScreenshotsHint"];
        NotePreviewLabel.Text = _l["NotePreview"];
        NotePreviewHint.Text = _l["NotePreviewHint"];
        AutoLockLabel.Text = _l["AutoLock"];
        ClipboardLabel.Text = _l["ClipboardClear"];
        ChangeMasterButton.Text = _l["ChangeMaster"];
        DataTitle.Text = _l["DataTitle"];
        ImportHint.Text = _l["ImportHint"];
        ImportButton.Text = _l["Import"];
        ImportGaButton.Text = _l["ImportScanGa"];
        ExportEncButton.Text = _l["ExportEncrypted"];
        ExportPlainButton.Text = _l["ExportPlain"];
        DangerTitle.Text = _l["DangerTitle"];
        DeleteVaultButton.Text = _l["DeleteVault"];

        AutoLockPicker.ItemsSource = SettingsRules.LockLabels(_l);
        AutoLockPicker.SelectedIndex = SettingsRules.IndexOf(LockMinutes, _settings.AutoLockMinutes);
        ClipboardPicker.ItemsSource = SettingsRules.ClipLabels(_l);
        ClipboardPicker.SelectedIndex = SettingsRules.IndexOf(ClipSeconds, _settings.ClipboardSeconds);
        TrustSwitch.IsToggled = _settings.TrustDevice;
        BiometricsSwitch.IsToggled = _settings.Biometrics;
        ScreenshotsSwitch.IsToggled = _settings.AllowScreenshots;
        NotePreviewSwitch.IsToggled = _settings.NotePreview;
        RefreshStorage();
        _loading = false;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!await Gate.EnsureUnlockedAsync(this))
            return;
        RefreshAutofill();
        if (_desktop.IsAvailable)
        {
            BiometricsRow.IsVisible = false;   // en Windows no hay desbloqueo biometrico
            ScreenshotsRow.IsVisible = false;  // ni bloqueo de capturas
        }
        BiometricsSwitch.IsEnabled = await _biometric.IsAvailableAsync();
        if (!BiometricsSwitch.IsEnabled)
            BiometricsHint.Text = _l["BiometricsUnavailable"];
        RefreshStorage();
    }

    private void RefreshStorage()
    {
        _loading = true;
        var mode = _settings.Storage;
        // El boton del modo activo va en primario con una marca; los otros, de contorno. Los logos
        // de Google y Microsoft no cambian con el estado (el de Google en color, como piden sus
        // normas de marca); el candado es monocromo y sigue al color del texto.
        StyleStorageButton(LocalButton, _l["StorageLocal"], mode == StorageMode.Local, mode == StorageMode.Local ? "ic_lock_w.png" : "ic_lock.png");
        StyleStorageButton(GoogleButton, _l["StorageGoogle"], mode == StorageMode.GoogleDrive, "ic_google.png");
        StyleStorageButton(OneDriveButton, _l["StorageOneDrive"], mode == StorageMode.OneDrive, "ic_microsoft.png");
        var cloud = mode != StorageMode.Local;
        AccountLabel.Text = cloud && _settings.AccountEmail.Length > 0 ? string.Format(_l.CurrentCulture, _l["SignedInAs"], _settings.AccountEmail) : string.Empty;
        AccountLabel.IsVisible = AccountLabel.Text.Length > 0;
        SyncButton.IsVisible = SignOutButton.IsVisible = cloud;
        _loading = false;
    }

    private void ShowStatus(string status)
    {
        SyncStatus.Text = SettingsRules.StatusText(status, _l);
        SyncStatus.IsVisible = SyncStatus.Text.Length > 0;
    }

    private static Style? LookupStyle(string key)
        => Application.Current?.Resources.TryGetValue(key, out var s) == true ? s as Style : null;

    private static void StyleStorageButton(Button button, string text, bool active, string icon)
    {
        button.Style = LookupStyle(active ? "PrimaryButton" : "OutlineButton");
        button.Text = active ? "✓ " + text : text;
        button.ImageSource = icon;
    }

    // ------------------------------------------------------------------ idioma

    private void OnSpanishClicked(object? sender, EventArgs e) => SetLanguage("es");

    private void OnEnglishClicked(object? sender, EventArgs e) => SetLanguage("en");

    private void SetLanguage(string code)
    {
        if (code == _l.CurrentLanguage)
            return;
        _settings.Language = code;
        _l.SetLanguage(code);
    }

    // ------------------------------------------------------------------ Windows

    private void OnTrayToggled(object? sender, ToggledEventArgs e)
    {
        if (_loading)
            return;
        _settings.TrayOnMinimize = e.Value;
        _desktop.SetMinimizeToTray(e.Value);
    }

    private void OnStartupToggled(object? sender, ToggledEventArgs e)
    {
        if (_loading)
            return;
        _desktop.StartWithWindows = e.Value;
    }

    private void OnExtAskToggled(object? sender, ToggledEventArgs e)
    {
        if (!_loading)
            _settings.AskExtensions = e.Value;
    }

    // ------------------------------------------------------------------ autocompletar

    /// <summary>Android: estado del servicio de autocompletar y boton para elegir este; Windows: interruptor de la lista en las aplicaciones.</summary>
    private void RefreshAutofill()
    {
        var loading = _loading;
        _loading = true;
        AutofillTitle.Text = _l["AutofillSection"];
        AutofillAskLabel.Text = _l["AutofillAskOnUnlock"];
        AutofillAskHint.Text = _l["AutofillAskOnUnlockHint"];
        AutofillAskSwitch.IsToggled = _settings.AskAutofill;
        DesktopAutofillLabel.Text = _l["DesktopAutofill"];
        DesktopAutofillHint.Text = _l["DesktopAutofillHint"];
        DesktopAutofillSwitch.IsToggled = _settings.DesktopAutofill;
        if (_android.IsAvailable)
        {
            var supported = _android.AutofillSupported;
            AutofillCard.IsVisible = supported;
            DesktopAutofillRow.IsVisible = false;
            var ours = supported && _android.AutofillIsOurs;
            AutofillStatus.Text = ours ? "✓ " + _l["AutofillIsOurs"] : _l["AutofillIsOther"];
            AutofillButton.Text = ours ? _l["AutofillChange"] : _l["AutofillUseThis"];
            AutofillButton.Style = LookupStyle(ours ? "OutlineButton" : "PrimaryButton");
            AutofillButton.ImageSource = ours ? "ic_key.png" : "ic_key_w.png";
            AutofillAskRow.IsVisible = !ours;
            // Lo de arriba solo manda en las aplicaciones. En los navegadores mandan el «servicio
            // preferido de contraseñas» del sistema (Android 14+) y el gestor propio del navegador: los
            // dos se abren aqui.
            var preferred = _android.HasPreferredService;
            PreferredHint.IsVisible = preferred;
            PreferredButton.IsVisible = preferred;
            PreferredHint.Text = _l["AutofillPreferredHint"];
            PreferredButton.Text = _l["AutofillPreferredOpen"];
            _browsers = _android.InstalledBrowsers();
            BrowsersHint.IsVisible = _browsers.Count > 0;
            BrowsersButton.IsVisible = _browsers.Count > 0;
            BrowsersHint.Text = _l["AutofillBrowsersHint"];
            BrowsersButton.Text = _l["AutofillBrowsersOpen"];
        }
        else if (_desktop.IsAvailable)
        {
            AutofillCard.IsVisible = true;
            AutofillStatus.IsVisible = false;
            AutofillButton.IsVisible = false;
            AutofillAskRow.IsVisible = false;
        }
        else
        {
            AutofillCard.IsVisible = false;
        }
        _loading = loading;
    }

    private IReadOnlyList<(string Package, string Name)> _browsers = [];

    /// <summary>Android 14+: la pantalla del sistema donde se elige el gestor de contraseñas preferido.</summary>
    private void OnPreferredClicked(object? sender, EventArgs e)
    {
        if (_android.IsAvailable && !_android.OpenPreferredService())
            _toast.Show(_l["AutofillPreferredNoScreen"]);
    }

    /// <summary>Los ajustes del navegador: su gestor de contraseñas propio se apaga desde dentro.</summary>
    private async void OnBrowserSettingsClicked(object? sender, EventArgs e)
    {
        if (_browsers.Count == 0)
            return;
        var names = _browsers.Select(b => b.Name).ToArray();
        var chosen = names.Length == 1
            ? names[0]
            : await _dialogs.ActionSheetAsync(this, _l["AutofillBrowsersOpen"], _l["Cancel"], names);
        if (chosen is null || chosen == _l["Cancel"])
            return;
        var browser = _browsers.FirstOrDefault(b => b.Name == chosen);
        if (browser.Package is null)
            return;
        await _dialogs.AlertAsync(this, chosen, string.Format(_l.CurrentCulture, _l["AutofillBrowserSteps"], chosen), _l["Ok"]);
        if (!_android.OpenBrowserSettings(browser.Package))
            _toast.Show(_l["AutofillPreferredNoScreen"]);
    }

    private void OnAutofillClicked(object? sender, EventArgs e)
    {
        try { _android.RequestAutofill(); }
        catch (Exception ex) { _toast.Show(ex.Message); }
    }

    private void OnAutofillAskToggled(object? sender, ToggledEventArgs e)
    {
        if (!_loading)
            _settings.AskAutofill = e.Value;
    }

    private void OnDesktopAutofillToggled(object? sender, ToggledEventArgs e)
    {
        if (!_loading)
            _settings.DesktopAutofill = e.Value;
    }

    /// <summary>Version de la Microsoft Store: abre la pagina de descargas de la version exe (la que puede usar las extensiones).</summary>
    private async void OnExtGetExeClicked(object? sender, EventArgs e)
    {
        try { await ServiceHelper.GetRequiredService<IBrowser>().OpenAsync(Platforms.Windows.PackageIdentity.ReleasesUrl, BrowserLaunchMode.External); }
        catch (Exception) { }
    }

    /// <summary>Una fila por navegador del PC: nombre, si la extension ya ha conectado, y el boton de instalar.</summary>
    private void RefreshBrowsers()
    {
        BrowsersBox.Clear();
        // La de la Microsoft Store (MSIX) no puede registrar el puente: la tarjeta lo explica y
        // lleva a la version exe, sin navegadores ni «Instalar…» ni la pregunta al desbloquear.
        var packaged = _desktop.ExtensionsPackaged;
        ExtensionsCard.IsVisible = packaged || _desktop.ExtensionsAvailable;
        ExtGetExeButton.IsVisible = packaged;
        ExtAskRow.IsVisible = !packaged;
        if (packaged)
            return;
        foreach (var b in _desktop.DetectedBrowsers())
        {
            var seen = _settings.ExtensionSeen(b.Key) is not null;
            var grid = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 8 };
            var text = new VerticalStackLayout();
            text.Add(new Label { Text = b.Name, Style = (Style)Application.Current!.Resources["BodyText"] });
            text.Add(new Label { Text = seen ? "✓ " + _l["ExtInstalled"] : _l["ExtNotInstalled"], Style = (Style)Application.Current.Resources["HintText"], FontSize = 11 });
            grid.Add(text, 0, 0);
            var button = new Button { Text = _l["ExtInstallButton"], Style = (Style)Application.Current.Resources[seen ? "OutlineButton" : "PrimaryButton"], Padding = new Thickness(12, 6), VerticalOptions = LayoutOptions.Center };
            var browser = b;
            button.Clicked += async (_, _) => { await _desktop.InstallExtensionAsync(this, browser); RefreshBrowsers(); };
            grid.Add(button, 1, 0);
            BrowsersBox.Add(grid);
        }
    }

    // ------------------------------------------------------------------ almacenamiento

    private async void OnStorageClicked(object? sender, EventArgs e)
    {
        if (_loading || sender is not Button button || button.CommandParameter is not string value)
            return;
        var mode = Enum.Parse<StorageMode>(value);
        if (mode == _settings.Storage)
            return;
        if (mode == StorageMode.Local)
        {
            _store.SignOut();
            RefreshStorage();
            return;
        }
        if (!_store.IsConfigured(mode))
        {
            await _dialogs.AlertAsync(this, _l["Error"], string.Format(_l.CurrentCulture, _l["StorageNotConfigured"], SettingsRules.BrandOf(mode)), _l["Ok"]);
            RefreshStorage();
            return;
        }
        try
        {
            SyncStatus.Text = "…";
            SyncStatus.IsVisible = SyncStatus.Text.Length > 0;
            // Entrar por primera vez (cuenta, contraseña, segundo factor, consentimiento) puede llevar
            // un rato largo: diez minutos antes de darlo por abandonado.
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            await _store.SignInAsync(mode, cts.Token);
            RefreshStorage();
            await SyncAsync();
        }
        catch (OperationCanceledException)
        {
            RefreshStorage();
            SyncStatus.Text = _l["SignInTimeout"];
            SyncStatus.IsVisible = true;
        }
        catch (Exception ex)
        {
            _store.SignOut();
            RefreshStorage();
            await _dialogs.AlertAsync(this, _l["Error"], ex.Message == "scope" ? _l["SyncScope"] : ex.Message, _l["Ok"]);
        }
    }

    private async void OnSyncClicked(object? sender, EventArgs e) => await SyncAsync();

    private async Task SyncAsync()
    {
        try
        {
            SyncStatus.Text = "…";
            SyncStatus.IsVisible = SyncStatus.Text.Length > 0;
            var changed = await _store.SyncAsync();
            SyncStatus.Text = SettingsRules.SyncText(changed, _l);
            SyncStatus.IsVisible = SyncStatus.Text.Length > 0;
        }
        catch (VaultPasswordNeededException needed)
        {
            var password = await _dialogs.PromptAsync(this, _l["MasterPassword"], _l["SyncPasswordNeeded"], _l["Ok"], _l["Cancel"], isPassword: true);
            if (string.IsNullOrEmpty(password))
            {
                SyncStatus.Text = string.Empty;
                SyncStatus.IsVisible = SyncStatus.Text.Length > 0;
                return;
            }
            try
            {
                var changed = await _store.MergeRemoteWithPasswordAsync(needed.RemoteContent, password);
                SyncStatus.Text = string.Format(_l.CurrentCulture, _l["SyncDone"], changed);
                SyncStatus.IsVisible = SyncStatus.Text.Length > 0;
            }
            catch (Exception)
            {
                SyncStatus.Text = _l["MasterPasswordWrong"];
                SyncStatus.IsVisible = SyncStatus.Text.Length > 0;
            }
        }
        catch (CloudException ex) when (ex.IsScopeProblem)
        {
            SyncStatus.Text = _l["SyncScope"];
            SyncStatus.IsVisible = SyncStatus.Text.Length > 0;
        }
        catch (Exception ex)
        {
            AppLog.Error("sincronizar", ex);
            SyncStatus.Text = string.Format(_l.CurrentCulture, _l["SyncFailed"], ex.Message);
            SyncStatus.IsVisible = SyncStatus.Text.Length > 0;
        }
    }

    private void OnSignOutClicked(object? sender, EventArgs e)
    {
        _store.SignOut();
        RefreshStorage();
    }

    // ------------------------------------------------------------------ seguridad

    /// <summary>«Confiar en este usuario y dispositivo»: se guarda la clave en el sistema y la boveda deja de pedir la contraseña.</summary>
    private async void OnTrustToggled(object? sender, ToggledEventArgs e)
    {
        if (_loading)
            return;
        if (e.Value && !await _dialogs.AlertAsync(this, _l["TrustDevice"], _l["TrustDeviceConfirm"], _l["TrustDeviceYes"], _l["Cancel"]))
        {
            _loading = true;
            TrustSwitch.IsToggled = false;
            _loading = false;
            return;
        }
        _settings.TrustDevice = e.Value;
        await _store.RememberKeyAsync(e.Value || _settings.Biometrics);
    }

    private async void OnBiometricsToggled(object? sender, ToggledEventArgs e)
    {
        if (_loading)
            return;
        if (e.Value)
        {
            if (!await _biometric.AuthenticateAsync(_l["AppName"], _l["BiometricReason"]))
            {
                _loading = true;
                BiometricsSwitch.IsToggled = false;
                _loading = false;
                return;
            }
        }
        _settings.Biometrics = e.Value;
        await _store.RememberKeyAsync(e.Value || _settings.TrustDevice);
    }

    /// <summary>«Permitir capturas de pantalla»: quita o vuelve a poner FLAG_SECURE en la ventana al momento.</summary>
    private void OnScreenshotsToggled(object? sender, ToggledEventArgs e)
    {
        if (_loading)
            return;
        _settings.AllowScreenshots = e.Value;
        _android.ApplyScreenshotPolicy();
    }

    /// <summary>«Enseñar el principio de las notas seguras»: la lista lo recoge al volver a ella.</summary>
    private void OnNotePreviewToggled(object? sender, ToggledEventArgs e)
    {
        if (!_loading)
            _settings.NotePreview = e.Value;
    }

    private void OnAutoLockChanged(object? sender, EventArgs e)
    {
        if (!_loading && AutoLockPicker.SelectedIndex >= 0)
            _settings.AutoLockMinutes = LockMinutes[AutoLockPicker.SelectedIndex];
    }

    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        if (!_loading && ClipboardPicker.SelectedIndex >= 0)
            _settings.ClipboardSeconds = ClipSeconds[ClipboardPicker.SelectedIndex];
    }

    private async void OnChangeMasterClicked(object? sender, EventArgs e)
    {
        var p1 = await _dialogs.PromptAsync(this, _l["ChangeMaster"], _l["MasterPassword"], _l["Continue"], _l["Cancel"], isPassword: true);
        if (string.IsNullOrEmpty(p1))
            return;
        if (MasterPasswordRules.Problem(p1, null) is { } tooShort)
        {
            await _dialogs.AlertAsync(this, _l["Error"], _l[tooShort], _l["Ok"]);
            return;
        }
        var p2 = await _dialogs.PromptAsync(this, _l["ChangeMaster"], _l["MasterPasswordRepeat"], _l["Save"], _l["Cancel"], isPassword: true);
        if (MasterPasswordRules.Problem(p1, p2 ?? string.Empty) is { } mismatch)
        {
            await _dialogs.AlertAsync(this, _l["Error"], _l[mismatch], _l["Ok"]);
            return;
        }
        await _store.ChangeMasterPasswordAsync(p1);
        _toast.Show(_l["ChangeMasterDone"]);
    }

    // ------------------------------------------------------------------ importar y exportar

    private async void OnImportClicked(object? sender, EventArgs e)
    {
        try
        {
            var content = await ServiceHelper.GetRequiredService<ITextFilePicker>().PickTextAsync(_l["ImportPick"]);
            if (content is null)
                return;
            await ImportContentAsync(content);
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync(this, _l["Error"], ex.Message, _l["Ok"]);
        }
    }

    private async void OnImportGaClicked(object? sender, EventArgs e)
    {
        var text = await ServiceHelper.GetRequiredService<IQrScanner>().ScanAsync(this);
        if (text is not null)
            await ImportContentAsync(text);
    }

    private async Task ImportContentAsync(string content)
    {
        var result = Importers.Parse(content);
        if (result is null)
        {
            await _dialogs.AlertAsync(this, _l["Import"], _l["ImportUnknown"], _l["Ok"]);
            return;
        }
        var (added, skipped) = Importers.MergeInto(_store.Data!, result.Entries);
        if (added > 0)
            await _store.SaveAsync();
        await _dialogs.AlertAsync(this, _l["ImportResultTitle"], string.Format(_l.CurrentCulture, _l["ImportDone"], added, skipped), _l["Ok"]);
    }

    private async void OnExportEncryptedClicked(object? sender, EventArgs e) =>
        await ExportAsync(SettingsRules.ExportName(DateTime.Now, "soccred"), _store.ExportEncrypted());

    private async void OnExportPlainClicked(object? sender, EventArgs e)
    {
        if (!await _dialogs.AlertAsync(this, _l["ExportPlain"], _l["ExportPlainConfirm"], _l["Continue"], _l["Cancel"]))
            return;
        await ExportAsync(SettingsRules.ExportName(DateTime.Now, "json"), _store.ExportPlainJson());
    }

    private async Task ExportAsync(string name, string content)
    {
        try
        {
            var windows = ServiceHelper.GetRequiredService<IDeviceInfo>().Platform == DevicePlatform.WinUI;
            var folder = windows
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : ServiceHelper.GetRequiredService<IFileSystem>().CacheDirectory;
            var path = Path.Combine(folder, name);
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
            if (windows)
                _toast.Show(string.Format(_l.CurrentCulture, _l["Exported"], path));
            else
                await ServiceHelper.GetRequiredService<IShare>().RequestAsync(new ShareFileRequest { Title = name, File = new ShareFile(path) });
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync(this, _l["Error"], ex.Message, _l["Ok"]);
        }
    }

    // ------------------------------------------------------------------ zona peligrosa

    private async void OnDeleteVaultClicked(object? sender, EventArgs e)
    {
        var word = await _dialogs.PromptAsync(this, _l["DeleteVault"], _l["DeleteVaultConfirm"], _l["Delete"], _l["Cancel"]);
        if (!SettingsRules.IsDeleteWord(word))
            return;
        await _store.DeleteLocalAsync();
        await _navigation.GoToAsync("//VaultPage");
    }
}
