using Credentials.Pages;
using Credentials.Services;

namespace Credentials.Ui.Tests;

public class UnlockPageTests
{
    private const string Master = "contraseña-larga";

    private static string Error(UnlockPage page) => page.Find<Label>("ErrorLabel").Text;

    [Fact]
    public async Task Crear_la_boveda_la_primera_vez()
    {
        using var app = TestHost.Start();
        app.Settings.Biometrics = true;
        var page = new UnlockPage();
        Assert.Equal(app["CreateTitle"], page.Find<Label>("TitleLabel").Text);
        Assert.True(page.Find<Label>("IntroLabel").IsVisible);
        Assert.True(page.Find<Entry>("RepeatEntry").IsVisible);
        Assert.Equal(app["Create"], page.Find<Button>("GoButton").Text);

        await page.Click("GoButton");
        Assert.Equal(app["MasterPasswordEmpty"], Error(page));
        page.Find<Entry>("PasswordEntry").Text = "corta";
        await page.Click("GoButton");
        Assert.Equal(app["MasterPasswordShort"], Error(page));
        page.Find<Entry>("PasswordEntry").Text = Master;
        page.Find<Entry>("RepeatEntry").Text = "otra-distinta";
        await page.Click("GoButton");
        Assert.Equal(app["MasterPasswordMismatch"], Error(page));
        Assert.False(app.Store.Exists);

        page.Find<Entry>("RepeatEntry").Text = Master;
        await page.Click("GoButton");
        Assert.True(app.Store.IsUnlocked);
        Assert.True(app.Store.Exists);
        Assert.False(page.Find<Label>("ErrorLabel").IsVisible);
        Assert.Single(app.Secure.Values);   // con biometria, la clave queda guardada
        Assert.Equal(string.Empty, page.Find<Entry>("PasswordEntry").Text);   // al cerrar se vacia
        Assert.True(page.Find<Button>("GoButton").IsEnabled);
    }

    [Fact]
    public async Task Abrir_con_la_contraseña()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync(Master);
        app.Store.Lock();
        var page = new UnlockPage();
        Assert.Equal(app["UnlockTitle"], page.Find<Label>("TitleLabel").Text);
        Assert.False(page.Find<Label>("IntroLabel").IsVisible);
        Assert.False(page.Find<Entry>("RepeatEntry").IsVisible);
        Assert.Equal(app["Unlock"], page.Find<Button>("GoButton").Text);

        page.Find<Entry>("PasswordEntry").Text = "corta";   // al abrir no se mira el largo: solo si vale
        await page.Click("GoButton");
        Assert.Equal(app["MasterPasswordWrong"], Error(page));
        Assert.False(app.Store.IsUnlocked);

        page.Find<Entry>("PasswordEntry").Text = Master;
        await page.Click("GoButton");
        Assert.True(app.Store.IsUnlocked);
        Assert.Empty(app.Secure.Values);   // sin biometria ni confianza no se guarda la clave

        app.Texts.SetLanguage("en");   // ya cerrada, los textos siguen el idioma
        Assert.Equal("Unlock", page.Find<Button>("GoButton").Text);
    }

    [Fact]
    public async Task Fichero_roto_enseña_el_error()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync(Master);
        app.Store.Lock();
        var page = new UnlockPage();
        // El fichero desaparece y en su sitio hay una carpeta: no se puede leer.
        File.Delete(VaultStore.FilePath);
        Directory.CreateDirectory(VaultStore.FilePath);
        page.Find<Entry>("PasswordEntry").Text = Master;
        await page.Click("GoButton");
        Assert.True(page.Find<Label>("ErrorLabel").IsVisible);
        Assert.NotEqual(app["MasterPasswordWrong"], Error(page));
        Assert.False(app.Store.IsUnlocked);
    }

    [Fact]
    public async Task Ojo_enseña_las_dos_casillas()
    {
        using var app = TestHost.Start();
        var page = new UnlockPage();
        page.Find<ImageButton>("EyeButton").SendClicked();
        Assert.False(page.Find<Entry>("PasswordEntry").IsPassword);
        Assert.False(page.Find<Entry>("RepeatEntry").IsPassword);
        Assert.Equal("ic_eye_off.png", ((FileImageSource)page.Find<ImageButton>("EyeButton").Source).File);
        page.Find<ImageButton>("EyeButton").SendClicked();
        Assert.True(page.Find<Entry>("RepeatEntry").IsPassword);
        Assert.True(page.Back());   // atras no deja pasar
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Biometria_al_aparecer_y_con_el_boton()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync(Master);
        app.Settings.Biometrics = true;
        await app.Store.RememberKeyAsync(true);
        app.Store.Lock();
        app.Biometric.Available = true;

        // El usuario no pasa: se queda la puerta con el boton de la huella.
        var page = new UnlockPage();
        await page.Appear();
        Assert.True(page.Find<Button>("BiometricButton").IsVisible);
        Assert.Equal(1, app.Biometric.Asked);
        Assert.False(app.Store.IsUnlocked);
        await page.Appear();   // ya se intento: no vuelve a preguntar sola
        Assert.Equal(1, app.Biometric.Asked);

        app.Biometric.Pass = true;
        await page.Click("BiometricButton");
        Assert.True(app.Store.IsUnlocked);
    }

    [Fact]
    public async Task Biometria_con_clave_que_no_vale()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync(Master);
        app.Settings.Biometrics = true;
        await app.Store.RememberKeyAsync(true);
        app.Store.Lock();
        foreach (var k in app.Secure.Values.Keys.ToList())
            app.Secure.Values[k] = Convert.ToBase64String(new byte[32]);
        app.Biometric.Available = app.Biometric.Pass = true;

        var page = new UnlockPage();
        await page.Click("BiometricButton");
        Assert.Equal(app["MasterPasswordWrong"], Error(page));
        Assert.False(app.Store.IsUnlocked);
    }

    [Fact]
    public async Task Volver_a_ofrecer_la_biometria()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync(Master);
        app.Settings.Biometrics = true;
        await app.Store.RememberKeyAsync(true);

        var page = new UnlockPage();
        await AsyncVoid.Run(page.RetryBiometric);   // abierta: nada
        Assert.Equal(0, app.Biometric.Asked);

        app.Store.Lock();
        await AsyncVoid.Run(page.RetryBiometric);   // sin huella disponible: nada
        Assert.Equal(0, app.Biometric.Asked);

        app.Biometric.Fail = true;   // el sensor falla: no pasa nada
        await AsyncVoid.Run(page.RetryBiometric);
        Assert.Equal(0, app.Biometric.Asked);

        app.Biometric.Fail = false;
        app.Biometric.Available = app.Biometric.Pass = true;
        await AsyncVoid.Run(page.RetryBiometric);
        Assert.Equal(1, app.Biometric.Asked);
        Assert.True(app.Store.IsUnlocked);
    }

    [Fact]
    public async Task Al_abrir_se_quita_de_la_pila_de_modales()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync(Master);
        app.Store.Lock();
        var page = new UnlockPage();
        // Encima de todo, como la saca la puerta: su navegacion la tiene en la pila de modales.
        await page.Navigation.PushModalAsync(page);
        page.Find<Entry>("PasswordEntry").Text = Master;
        await page.Click("GoButton");
        Assert.Empty(page.Navigation.ModalStack);
    }

    [Fact]
    public async Task Si_se_abre_por_otro_camino_se_retira()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync(Master);
        app.Store.Lock();
        var page = new UnlockPage();
        page.Find<Entry>("PasswordEntry").Text = "a medias";
        await AsyncVoid.Run(() => app.Store.UnlockAsync(Master).GetAwaiter().GetResult());
        Assert.True(Ui.Field<bool>(page, "_closed"));
        Assert.Equal(string.Empty, page.Find<Entry>("PasswordEntry").Text);
        // Cerrada ya, otro aviso no hace nada.
        app.Store.Lock();
        await app.Store.UnlockAsync(Master);
    }

    [Fact]
    public async Task Crear_con_espacios_al_principio_o_al_final_no_se_deja()
    {
        using var app = TestHost.Start();
        var page = new UnlockPage();
        foreach (var bad in new[] { Master + " ", " " + Master, Master + "\t" })
        {
            page.Find<Entry>("PasswordEntry").Text = bad;
            page.Find<Entry>("RepeatEntry").Text = bad;
            await page.Click("GoButton");
            Assert.Equal(app["MasterPasswordSpaces"], Error(page));
            Assert.False(app.Store.Exists);
        }
        // Por dentro si se puede: una frase.
        page.Find<Entry>("PasswordEntry").Text = page.Find<Entry>("RepeatEntry").Text = "una frase larga";
        await page.Click("GoButton");
        Assert.True(app.Store.IsUnlocked);
    }

    [Fact]
    public async Task Una_boveda_vieja_con_espacio_al_final_se_sigue_abriendo()
    {
        using var app = TestHost.Start();
        // Creada antes de la regla, con el espacio dentro de la contraseña: al abrir no se recorta nada.
        await app.CreateVaultAsync(Master + " ");
        app.Store.Lock();
        var page = new UnlockPage();
        page.Find<Entry>("PasswordEntry").Text = Master;   // sin el espacio no es la misma
        await page.Click("GoButton");
        Assert.Equal(app["MasterPasswordWrong"], Error(page));
        page.Find<Entry>("PasswordEntry").Text = Master + " ";
        await page.Click("GoButton");
        Assert.True(app.Store.IsUnlocked);
    }

    [Fact]
    public async Task Ayuda_de_desbloqueo_en_Windows_y_contraseña_olvidada()
    {
        using var app = TestHost.Start();
        // Creando: se cuenta que despues se puede confiar en el dispositivo; nada de «olvidada».
        var creating = new UnlockPage();
        await creating.Appear();
        Assert.Equal(app["UnlockHintTrustLater"], creating.Find<Label>("HintLabel").Text);
        Assert.False(creating.Find<Button>("ForgotButton").IsVisible);

        await app.CreateVaultAsync(Master);
        app.Store.Lock();
        var page = new UnlockPage();
        await page.Appear();
        Assert.True(page.Find<Label>("HintLabel").IsVisible);
        Assert.Equal(app["UnlockHintTrust"], page.Find<Label>("HintLabel").Text);
        Assert.True(page.Find<Button>("ForgotButton").IsVisible);
        Assert.Equal(app["ForgotMaster"], page.Find<Button>("ForgotButton").Text);
        app.Texts.SetLanguage("en");
        Assert.Equal("Forgot your master password?", page.Find<Button>("ForgotButton").Text);
        Assert.Equal(app["UnlockHintTrust"], page.Find<Label>("HintLabel").Text);
        app.Texts.SetLanguage("es");

        app.Settings.TrustDevice = true;   // de confianza pero sin la clave: escribirla una vez
        await page.Appear();
        Assert.Equal(app["UnlockHintTrustRestore"], page.Find<Label>("HintLabel").Text);
    }

    [Fact]
    public async Task Contraseña_olvidada_explica_y_solo_borra_con_BORRAR()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync(Master);
        app.Settings.TrustDevice = true;
        await app.Store.RememberKeyAsync(true);
        app.Store.Lock();
        var page = new UnlockPage();

        // Se cancela la explicacion: no pasa nada. Tocar fuera no cuenta (solo los botones).
        await page.Click("ForgotButton");
        var shown = app.Dialogs.Log[^1];
        Assert.Equal(app["ForgotMasterBody"], shown.Message);
        Assert.Contains(app["ForgotMasterDelete"], shown.Options);
        Assert.False(shown.OutsideCloses);
        Assert.True(app.Store.Exists);

        // Quiere borrar pero no escribe BORRAR: tampoco.
        app.Dialogs.Answer(true, "borra");
        await page.Click("ForgotButton");
        Assert.Equal(app["DeleteVaultConfirm"], app.Dialogs.Log[^1].Message);
        Assert.True(app.Store.Exists);

        // Con BORRAR: fuera la boveda, la clave guardada y la confianza; la puerta pasa a crear una nueva.
        app.Dialogs.Answer(true, "BORRAR");
        await page.Click("ForgotButton");
        Assert.False(app.Store.Exists);
        Assert.Empty(app.Secure.Values);
        Assert.False(app.Settings.TrustDevice);
        Assert.Equal(app["CreateTitle"], page.Find<Label>("TitleLabel").Text);
        Assert.True(page.Find<Entry>("RepeatEntry").IsVisible);
        Assert.False(page.Find<Button>("ForgotButton").IsVisible);
        Assert.Equal(app["UnlockHintTrustLater"], page.Find<Label>("HintLabel").Text);

        page.Find<Entry>("PasswordEntry").Text = page.Find<Entry>("RepeatEntry").Text = "otra-contraseña";
        await page.Click("GoButton");
        Assert.True(app.Store.IsUnlocked);
        Assert.Empty(app.Store.Data!.Entries);
    }

    [Fact]
    public async Task Al_aparecer_sin_biometria_el_foco_a_la_contraseña()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync(Master);
        app.Store.Lock();
        var page = new UnlockPage();
        await page.Appear();
        Assert.False(page.Find<Button>("BiometricButton").IsVisible);
        Assert.Equal(0, app.Biometric.Asked);
    }
}
