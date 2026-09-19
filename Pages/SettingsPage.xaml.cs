using System.Text;
using Credentials.Helpers;
using Credentials.Services;
using SocShared;

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
    private bool _loading;
    private static readonly int[] LockMinutes = [0, 1, 2, 5, 10, 30, 60];
    private static readonly int[] ClipSeconds = [0, 15, 30, 60, 120];

    public SettingsPage()
    {
        InitializeComponent();
        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _settings = ServiceHelper.GetRequiredService<ISettingsService>();
        _store = ServiceHelper.GetRequiredService<VaultStore>();
        _biometric = ServiceHelper.GetRequiredService<IBiometric>();
        _toast = ServiceHelper.GetRequiredService<IToastService>();
        _store.Status += s => MainThread.BeginInvokeOnMainThread(() => ShowStatus(s));
        _l.LanguageChanged += (_, _) => ApplyTexts();
        ApplyTexts();
    }

    private void ApplyTexts()
    {
        _loading = true;
        Title = _l["SettingsTitle"];
        StorageTitle.Text = _l["StorageTitle"];
        StorageHint.Text = _l["StorageHint"];
        LocalRadio.Content = _l["StorageLocal"];
        GoogleRadio.Content = _l["StorageGoogle"];
        OneDriveRadio.Content = _l["StorageOneDrive"];
        SyncButton.Text = _l["SyncNow"];
        SignOutButton.Text = _l["SignOut"];
        SecurityTitle.Text = _l["SecurityTitle"];
        BiometricsLabel.Text = _l["Biometrics"];
        BiometricsHint.Text = _l["BiometricsHint"];
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

        AutoLockPicker.ItemsSource = LockMinutes.Select(m => m == 0 ? _l["AutoLockNever"] : string.Format(_l.CurrentCulture, _l["AutoLockMinutes"], m)).ToList();
        AutoLockPicker.SelectedIndex = Math.Max(0, Array.IndexOf(LockMinutes, _settings.AutoLockMinutes));
        ClipboardPicker.ItemsSource = ClipSeconds.Select(s => s == 0 ? _l["ClipboardNever"] : string.Format(_l.CurrentCulture, _l["ClipboardSeconds"], s)).ToList();
        ClipboardPicker.SelectedIndex = Math.Max(0, Array.IndexOf(ClipSeconds, _settings.ClipboardSeconds));
        BiometricsSwitch.IsToggled = _settings.Biometrics;
        RefreshStorage();
        _loading = false;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!await Gate.EnsureUnlockedAsync(this))
            return;
        BiometricsSwitch.IsEnabled = await _biometric.IsAvailableAsync();
        if (!BiometricsSwitch.IsEnabled)
            BiometricsHint.Text = _l["BiometricsUnavailable"];
        RefreshStorage();
    }

    private void RefreshStorage()
    {
        _loading = true;
        var mode = _settings.Storage;
        LocalRadio.IsChecked = mode == StorageMode.Local;
        GoogleRadio.IsChecked = mode == StorageMode.GoogleDrive;
        OneDriveRadio.IsChecked = mode == StorageMode.OneDrive;
        var cloud = mode != StorageMode.Local;
        AccountLabel.Text = cloud && _settings.AccountEmail.Length > 0 ? string.Format(_l.CurrentCulture, _l["SignedInAs"], _settings.AccountEmail) : string.Empty;
        SyncButton.IsVisible = SignOutButton.IsVisible = cloud;
        _loading = false;
    }

    private void ShowStatus(string status)
    {
        SyncStatus.Text = status.StartsWith("cloud:ok:") ? string.Empty : status.StartsWith("cloud:error:") ? string.Format(_l.CurrentCulture, _l["SyncFailed"], status[12..]) : status;
    }

    // ------------------------------------------------------------------ almacenamiento

    private async void OnStorageChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (_loading || !e.Value || sender is not RadioButton rb)
            return;
        var mode = Enum.Parse<StorageMode>((string)rb.Value);
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
            await ModernDialog.AlertAsync(this, _l["Error"], string.Format(_l.CurrentCulture, _l["StorageNotConfigured"], mode == StorageMode.GoogleDrive ? "Google" : "Microsoft"), _l["Ok"]);
            RefreshStorage();
            return;
        }
        try
        {
            SyncStatus.Text = "…";
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await _store.SignInAsync(mode, cts.Token);
            RefreshStorage();
            await SyncAsync();
        }
        catch (OperationCanceledException)
        {
            RefreshStorage();
        }
        catch (Exception ex)
        {
            _store.SignOut();
            RefreshStorage();
            await ModernDialog.AlertAsync(this, _l["Error"], ex.Message == "scope" ? _l["SyncScope"] : ex.Message, _l["Ok"]);
        }
    }

    private async void OnSyncClicked(object? sender, EventArgs e) => await SyncAsync();

    private async Task SyncAsync()
    {
        try
        {
            SyncStatus.Text = "…";
            var changed = await _store.SyncAsync();
            SyncStatus.Text = changed > 0 ? string.Format(_l.CurrentCulture, _l["SyncDone"], changed) : _l["SyncNothing"];
        }
        catch (VaultPasswordNeededException needed)
        {
            var password = await ModernDialog.PromptAsync(this, _l["MasterPassword"], _l["SyncPasswordNeeded"], _l["Ok"], _l["Cancel"], isPassword: true);
            if (string.IsNullOrEmpty(password))
            {
                SyncStatus.Text = string.Empty;
                return;
            }
            try
            {
                var changed = await _store.MergeRemoteWithPasswordAsync(needed.RemoteContent, password);
                SyncStatus.Text = string.Format(_l.CurrentCulture, _l["SyncDone"], changed);
            }
            catch (Exception)
            {
                SyncStatus.Text = _l["MasterPasswordWrong"];
            }
        }
        catch (CloudException ex) when (ex.IsScopeProblem)
        {
            SyncStatus.Text = _l["SyncScope"];
        }
        catch (Exception ex)
        {
            SyncStatus.Text = string.Format(_l.CurrentCulture, _l["SyncFailed"], ex.Message);
        }
    }

    private void OnSignOutClicked(object? sender, EventArgs e)
    {
        _store.SignOut();
        RefreshStorage();
    }

    // ------------------------------------------------------------------ seguridad

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
        await _store.RememberKeyAsync(e.Value);
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
        var p1 = await ModernDialog.PromptAsync(this, _l["ChangeMaster"], _l["MasterPassword"], _l["Continue"], _l["Cancel"], isPassword: true);
        if (string.IsNullOrEmpty(p1))
            return;
        if (p1.Length < 8)
        {
            await ModernDialog.AlertAsync(this, _l["Error"], _l["MasterPasswordShort"], _l["Ok"]);
            return;
        }
        var p2 = await ModernDialog.PromptAsync(this, _l["ChangeMaster"], _l["MasterPasswordRepeat"], _l["Save"], _l["Cancel"], isPassword: true);
        if (p2 != p1)
        {
            await ModernDialog.AlertAsync(this, _l["Error"], _l["MasterPasswordMismatch"], _l["Ok"]);
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
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = _l["ImportPick"] });
            if (file is null)
                return;
            using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = await reader.ReadToEndAsync();
            await ImportContentAsync(content);
        }
        catch (Exception ex)
        {
            await ModernDialog.AlertAsync(this, _l["Error"], ex.Message, _l["Ok"]);
        }
    }

    private async void OnImportGaClicked(object? sender, EventArgs e)
    {
        var text = await ScanPage.ScanAsync(this, _l);
        if (text is not null)
            await ImportContentAsync(text);
    }

    private async Task ImportContentAsync(string content)
    {
        var result = Importers.Parse(content);
        if (result is null)
        {
            await ModernDialog.AlertAsync(this, _l["Import"], _l["ImportUnknown"], _l["Ok"]);
            return;
        }
        var (added, skipped) = Importers.MergeInto(_store.Data!, result.Entries);
        if (added > 0)
            await _store.SaveAsync();
        await ModernDialog.AlertAsync(this, result.Source, string.Format(_l.CurrentCulture, _l["ImportDone"], added, skipped), _l["Ok"]);
    }

    private async void OnExportEncryptedClicked(object? sender, EventArgs e) =>
        await ExportAsync($"sOCCredentials-{DateTime.Now:yyyyMMdd-HHmm}.soccred", _store.ExportEncrypted());

    private async void OnExportPlainClicked(object? sender, EventArgs e)
    {
        if (!await ModernDialog.AlertAsync(this, _l["ExportPlain"], _l["ExportPlainConfirm"], _l["Continue"], _l["Cancel"]))
            return;
        await ExportAsync($"sOCCredentials-{DateTime.Now:yyyyMMdd-HHmm}.json", _store.ExportPlainJson());
    }

    private async Task ExportAsync(string name, string content)
    {
        try
        {
            var folder = DeviceInfo.Platform == DevicePlatform.WinUI
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : FileSystem.CacheDirectory;
            var path = Path.Combine(folder, name);
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
            if (DeviceInfo.Platform == DevicePlatform.WinUI)
                _toast.Show(string.Format(_l.CurrentCulture, _l["Exported"], path));
            else
                await Share.Default.RequestAsync(new ShareFileRequest { Title = name, File = new ShareFile(path) });
        }
        catch (Exception ex)
        {
            await ModernDialog.AlertAsync(this, _l["Error"], ex.Message, _l["Ok"]);
        }
    }

    // ------------------------------------------------------------------ zona peligrosa

    private async void OnDeleteVaultClicked(object? sender, EventArgs e)
    {
        var word = await ModernDialog.PromptAsync(this, _l["DeleteVault"], _l["DeleteVaultConfirm"], _l["Delete"], _l["Cancel"]);
        if (word is null || !(word.Trim().Equals("BORRAR", StringComparison.OrdinalIgnoreCase) || word.Trim().Equals("DELETE", StringComparison.OrdinalIgnoreCase)))
            return;
        _store.Lock();
        await _store.RememberKeyAsync(false);
        _store.SignOut();
        try { File.Delete(VaultStore.FilePath); File.Delete(VaultStore.FilePath + ".bak"); } catch (Exception) { }
        await Shell.Current.GoToAsync("//VaultPage");
    }
}
