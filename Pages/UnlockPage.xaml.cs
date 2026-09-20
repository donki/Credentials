using System.Security.Cryptography;
using Credentials.Helpers;
using Credentials.Services;

namespace Credentials.Pages;

/// <summary>
/// La puerta: crear la boveda la primera vez (contraseña maestra dos veces) o desbloquearla
/// (contraseña, o Windows Hello / huella si se activo). Se abre como modal encima de todo y se
/// cierra al entrar; si la boveda se bloquea, vuelve a salir.
/// </summary>
public partial class UnlockPage : ContentPage
{
    private readonly ILocalizationService _l;
    private readonly VaultStore _store;
    private readonly ISettingsService _settings;
    private readonly IBiometric _biometric;
    private readonly bool _creating;
    private bool _biometricTried;

    public UnlockPage()
    {
        InitializeComponent();
        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _store = ServiceHelper.GetRequiredService<VaultStore>();
        _settings = ServiceHelper.GetRequiredService<ISettingsService>();
        _biometric = ServiceHelper.GetRequiredService<IBiometric>();
        _creating = !_store.Exists;
        ApplyTexts();
        // Si la boveda se abre por otro camino (biometria desde otra pagina, el gancho de pruebas
        // en Debug), esta puerta se retira sola.
        _store.Changed += OnStoreChanged;
        _store.Unlocked += OnStoreChanged;
        _l.LanguageChanged += (_, _) => ApplyTexts();
    }

    private void OnStoreChanged()
    {
        if (_store.IsUnlocked)
            MainThread.BeginInvokeOnMainThread(async () => await CloseAsync());
    }

    private void ApplyTexts()
    {
        TitleLabel.Text = _creating ? _l["CreateTitle"] : _l["UnlockTitle"];
        IntroLabel.Text = _creating ? _l["CreateIntro"] : _l["AppDescription"];
        PasswordTitle.Text = _l["MasterPassword"];
        RepeatTitle.Text = _l["MasterPasswordRepeat"];
        RepeatTitle.IsVisible = RepeatEntry.IsVisible = _creating;
        GoButton.Text = _creating ? _l["Create"] : _l["Unlock"];
        GoButton.ImageSource = _creating ? "ic_key_w.png" : "ic_unlock_w.png";
        BiometricButton.Text = _l["UnlockBiometric"];
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var canBio = !_creating && _settings.Biometrics && _store.HasStoredKey && await _biometric.IsAvailableAsync();
        BiometricButton.IsVisible = canBio;
        if (canBio && !_biometricTried)
        {
            _biometricTried = true;
            await TryBiometricAsync();
        }
        else
        {
            PasswordEntry.Focus();
        }
    }

    private void OnEyeClicked(object? sender, EventArgs e)
    {
        PasswordEntry.IsPassword = !PasswordEntry.IsPassword;
        RepeatEntry.IsPassword = PasswordEntry.IsPassword;
        EyeButton.Source = PasswordEntry.IsPassword ? "ic_eye.png" : "ic_eye_off.png";
    }

    private async void OnBiometricClicked(object? sender, EventArgs e) => await TryBiometricAsync();

    private async Task TryBiometricAsync()
    {
        if (!await _biometric.AuthenticateAsync(_l["AppName"], _l["BiometricReason"]))
            return;
        SetBusy(true);
        var ok = await _store.UnlockWithStoredKeyAsync();
        SetBusy(false);
        if (ok)
            await CloseAsync();
        else
            ShowError(_l["MasterPasswordWrong"]);
    }

    private async void OnGo(object? sender, EventArgs e)
    {
        var password = PasswordEntry.Text ?? string.Empty;
        ErrorLabel.IsVisible = false;
        if (_creating)
        {
            if (password.Length < 8) { ShowError(_l["MasterPasswordShort"]); return; }
            if (password != (RepeatEntry.Text ?? string.Empty)) { ShowError(_l["MasterPasswordMismatch"]); return; }
        }
        SetBusy(true);
        try
        {
            if (_creating)
                await _store.CreateAsync(password);
            else
                await _store.UnlockAsync(password);
            // Con biometria activada, la clave se renueva en la boveda del sistema (por si cambio).
            if (_settings.Biometrics)
                await _store.RememberKeyAsync(true);
            await CloseAsync();
        }
        catch (CryptographicException)
        {
            ShowError(_l["MasterPasswordWrong"]);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowError(string text)
    {
        ErrorLabel.Text = text;
        ErrorLabel.IsVisible = true;
    }

    private void SetBusy(bool on)
    {
        Busy.IsVisible = Busy.IsRunning = on;
        GoButton.IsEnabled = !on;
        BiometricButton.IsEnabled = !on;
    }

    private bool _closed;

    private async Task CloseAsync()
    {
        if (_closed)
            return;
        _closed = true;
        _store.Changed -= OnStoreChanged;
        _store.Unlocked -= OnStoreChanged;
        PasswordEntry.Text = string.Empty;
        RepeatEntry.Text = string.Empty;
        if (Navigation.ModalStack.Contains(this))
            await Navigation.PopModalAsync(animated: true);
    }

    /// <summary>Sin salir por atras: bloqueada es bloqueada.</summary>
    protected override bool OnBackButtonPressed() => true;
}
