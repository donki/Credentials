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
