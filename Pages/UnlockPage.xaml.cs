using System.Security.Cryptography;
using Credentials.Helpers;
using Credentials.Services;

namespace Credentials.Pages;

/// <summary>
/// La puerta: crear la boveda la primera vez (contraseña maestra dos veces) o desbloquearla
/// (contraseña, o huella en Android si se activo). Se abre como modal encima de todo y se
/// cierra al entrar; si la boveda se bloquea, vuelve a salir.
/// </summary>
public partial class UnlockPage : ContentPage
{
    private readonly ILocalizationService _l;
    private readonly VaultStore _store;
    private readonly ISettingsService _settings;
    private readonly IBiometric _biometric;
    private readonly IDialogService _dialogs;
    private readonly IDesktopIntegration _desktop;
    private readonly IAndroidIntegration _android;
    private bool _creating;
    private bool _biometricTried;

    public UnlockPage()
    {
        InitializeComponent();
        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _store = ServiceHelper.GetRequiredService<VaultStore>();
        _settings = ServiceHelper.GetRequiredService<ISettingsService>();
        _biometric = ServiceHelper.GetRequiredService<IBiometric>();
        _dialogs = ServiceHelper.GetRequiredService<IDialogService>();
        _desktop = ServiceHelper.GetRequiredService<IDesktopIntegration>();
        _android = ServiceHelper.GetRequiredService<IAndroidIntegration>();
        _creating = !_store.Exists;
        ApplyTexts();
        // Si la boveda se abre por otro camino (biometria desde otra pagina, el gancho de pruebas
        // en Debug), esta puerta se retira sola.
        _store.Changed += OnStoreChanged;
        _store.Unlocked += OnStoreChanged;
        _l.LanguageChanged += (_, _) => ApplyTexts();
        if (_desktop.IsAvailable)
        {
            Stack.SizeChanged += (_, _) => FitWindowHeight();
            SizeChanged += (_, _) => FitWindowHeight();
        }
    }

    /// <summary>
    /// En Windows la puerta va en una ventana pequeña (<see cref="IDesktopIntegration.CompactForGate"/>):
    /// su alto se ajusta a lo que hay dentro, sin huecos arriba ni abajo, y crece si aparece un error.
    /// </summary>
    private void FitWindowHeight(bool force = false)
    {
        if (Width <= 0 || Height <= 0)
            return;
        try
        {
            // El alto que pide el contenido, no el que ocupa: dentro del ScrollView se estira hasta la ventana.
            _desktop.FitGateHeight(this, Stack.Measure(Width, double.PositiveInfinity).Height, force);
        }
        catch (Exception)
        {
            // Solo es la forma de la ventana: si falla, se queda como estaba.
        }
    }

    /// <summary>Volver a ajustar el alto (la ventana se ha recolocado desde fuera).</summary>
    internal void RefitHeight() => FitWindowHeight(force: true);

    private void OnStoreChanged()
    {
        if (_store.IsUnlocked)
            Dispatcher.RunOnUi(async () => await CloseAsync());
    }

    private void ApplyTexts()
    {
        TitleLabel.Text = _creating ? _l["CreateTitle"] : _l["UnlockTitle"];
        // Al desbloquear solo hace falta la casilla y el botón a la vista, sin desplazarse: la
        // presentación de la app sobra. Al crear la bóveda sí se explica.
        IntroLabel.Text = _creating ? _l["CreateIntro"] : string.Empty;
        IntroLabel.IsVisible = _creating;
        // En la ventanita de Windows, al desbloquear, sobran el candado y el titulo: la casilla y
        // el boton ya dicen que hacer (y el boton pone «Desbloquear»).
        if (_desktop.IsAvailable)
            Logo.IsVisible = TitleLabel.IsVisible = _creating;
        PasswordTitle.Text = _l["MasterPassword"];
        RepeatTitle.Text = _l["MasterPasswordRepeat"];
        RepeatTitle.IsVisible = RepeatEntry.IsVisible = _creating;
        GoButton.Text = _creating ? _l["Create"] : _l["Unlock"];
        GoButton.ImageSource = _creating ? "ic_key_w.png" : "ic_unlock_w.png";
        BiometricButton.Text = _l["UnlockBiometric"];
        ForgotButton.Text = _l["ForgotMaster"];
        ForgotButton.IsVisible = !_creating;
        SemanticProperties.SetDescription(ForgotButton, _l["ForgotMaster"]);
        ShowHint();
    }

    /// <summary>Si el dispositivo tiene huella o cara y si la clave esta guardada (lo mira OnAppearing).</summary>
    private bool _biometricAvailable, _hasStoredKey, _checked;

    /// <summary>El texto de ayuda bajo el boton (UnlockHelp): como abrirla sin escribir la contraseña.</summary>
    private void ShowHint()
    {
        var android = OperatingSystem.IsAndroid();
        // En Android depende de la huella, que se pregunta al aparecer: hasta entonces, nada.
        var key = android && !_checked ? null
            : UnlockHelp.HintKey(_creating, android, _biometricAvailable, _settings.Biometrics, _hasStoredKey, _settings.TrustDevice);
        HintLabel.Text = key is null ? string.Empty : _l[key];
        HintLabel.IsVisible = key is not null;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Se pregunta siempre (tambien para la ayuda de debajo); si el sensor falla, como si no hubiera.
        try { _biometricAvailable = await _biometric.IsAvailableAsync(); }
        catch (Exception) { _biometricAvailable = false; }
        _hasStoredKey = !_creating && _store.HasStoredKey;
        _checked = true;
        var canBio = !_creating && _settings.Biometrics && _hasStoredKey && _biometricAvailable;
        BiometricButton.IsVisible = canBio;
        ShowHint();
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

    /// <summary>
    /// Volver a ofrecer la biometria sin que el usuario la pida: al volver a la sesion de Windows
    /// tras Win+L, que cerro la boveda y saco esta puerta cuando aun no se podia contestar.
    /// </summary>
    public void RetryBiometric() => Dispatcher.RunOnUi(async () =>
    {
        try
        {
            if (_store.IsUnlocked || _creating || !_settings.Biometrics || !_store.HasStoredKey || !await _biometric.IsAvailableAsync())
                return;
            await TryBiometricAsync();
        }
        catch (Exception) { }
    });

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
        // Al abrir solo se mira que no este vacia; al crear, tambien el largo y que coincidan.
        var problem = MasterPasswordRules.Problem(password, _creating ? RepeatEntry.Text ?? string.Empty : null);
        if (problem == "MasterPasswordEmpty") { ShowError(_l[problem]); PasswordEntry.Focus(); return; }
        if (_creating && problem is not null) { ShowError(_l[problem]); return; }
        SetBusy(true);
        try
        {
            if (_creating)
                await _store.CreateAsync(password);
            else
                await _store.UnlockAsync(password);
            // Con biometria activada, la clave se renueva en la boveda del sistema (por si cambio).
            if (_settings.Biometrics || _settings.TrustDevice)
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

    /// <summary>
    /// «¿Has olvidado la contraseña?»: nadie puede recuperarla (no hay servidor ni copia de la
    /// contraseña). Se dice que se puede hacer (la huella, otro dispositivo que la abra) y, si no queda
    /// otra, se ofrece borrar la bóveda de este dispositivo y crear una nueva, escribiendo BORRAR.
    /// </summary>
    private async void OnForgotClicked(object? sender, EventArgs e)
    {
        var delete = await _dialogs.AlertAsync(this, _l["ForgotMaster"], _l["ForgotMasterBody"], _l["ForgotMasterDelete"], _l["Cancel"], dismissOnOutsideTap: false);
        if (!delete)
            return;
        var word = await _dialogs.PromptAsync(this, _l["DeleteVault"], _l["DeleteVaultConfirm"], _l["Delete"], _l["Cancel"]);
        if (!SettingsRules.IsDeleteWord(word))
            return;
        await _store.DeleteLocalAsync();
        // Sin bóveda, esta misma puerta pasa a crear una nueva.
        _creating = true;
        ErrorLabel.IsVisible = false;
        PasswordEntry.Text = string.Empty;
        BiometricButton.IsVisible = false;
        ApplyTexts();
        PasswordEntry.Focus();
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

    /// <summary>
    /// Atras en la puerta (Mobile 7): bloqueada es bloqueada, asi que no se pasa a lo de debajo. En la
    /// aplicacion, atras la oculta (como en inicio); al rellenar otra app (actividad de autocompletar),
    /// cancela y se vuelve a esa app sin rellenar.
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        if (_android.GateBack() == GateBackAction.CloseGate)
            Dispatcher.Dispatch(async () => await CloseAsync());
        return true;
    }
}
