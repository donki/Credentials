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
#if WINDOWS
        Stack.SizeChanged += (_, _) => FitWindowHeight();
        SizeChanged += (_, _) => FitWindowHeight();
#endif
    }

#if WINDOWS
    /// <summary>
    /// En Windows la puerta va en una ventana pequeña (ver <c>VaultPage.Compact</c>): su alto se
    /// ajusta a lo que hay dentro, sin huecos arriba ni abajo, y crece si aparece Windows Hello o un error.
    /// </summary>
    private double? _titleBar;
    private double _fittedContent = -1;

    /// <summary>Volver a ajustar el alto (la ventana se ha recolocado desde fuera).</summary>
    internal void RefitHeight()
    {
        _fittedContent = -1;
        FitWindowHeight();
    }

    private void FitWindowHeight()
    {
        try
        {
            if (Width <= 0 || Height <= 0 || Window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native)
                return;
            var app = native.AppWindow;
            var scale = native.Content?.XamlRoot?.RasterizationScale ?? 1.0;
            // El alto que pide el contenido, no el que ocupa: dentro del ScrollView se estira hasta la ventana.
            var content = Stack.Measure(Width, double.PositiveInfinity).Height;
            if (Math.Abs(content - _fittedContent) < 1)
                return;   // ya ajustada a este contenido (el cambio de tamaño vuelve a llamar aquí)
            var chrome = app.Size.Height - app.ClientSize.Height;   // bordes de Windows
            // MAUI pinta su barra de título dentro del área cliente: lo que sobra entre el área
            // cliente y la página es esa barra. Se mide una vez, con la ventana aún quieta: después
            // la página va un paso por detrás de cada cambio de tamaño.
            _titleBar ??= Math.Max(0, app.ClientSize.Height / scale - Height);
            var wanted = (int)Math.Ceiling((content + _titleBar.Value) * scale) + chrome;
            _fittedContent = content;
            if (Math.Abs(app.Size.Height - wanted) < 2)
                return;
            // Anclada abajo: crece o encoge hacia arriba.
            var bottom = app.Position.Y + app.Size.Height;
            app.MoveAndResize(new global::Windows.Graphics.RectInt32(app.Position.X, bottom - wanted, app.Size.Width, wanted));
        }
        catch (Exception)
        {
            // Solo es la forma de la ventana: si falla, se queda como estaba.
        }
    }
#endif

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
#if WINDOWS
        // En la ventanita de Windows, al desbloquear, sobran el candado y el título: la casilla y
        // el botón ya dicen qué hacer (y el botón pone «Desbloquear»).
        Logo.IsVisible = TitleLabel.IsVisible = _creating;
#endif
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
#if ANDROID
        var activity = Platform.CurrentActivity;
        if (activity is MainActivity)
            activity.MoveTaskToBack(true);
        else if (activity is not null)
            Dispatcher.Dispatch(async () => await CloseAsync());
#endif
        return true;
    }
}
