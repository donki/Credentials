using Credentials.Pages;
using Credentials.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel.Communication;

namespace Credentials.Ui.Tests;

public class TutorialPageTests
{
    private static T F<T>(TutorialPage page, string name) => Ui.Field<T>(page, name);

    [Fact]
    public async Task Pasos_sin_plataforma_nube_y_fin()
    {
        using var app = TestHost.Start();
        var page = new TutorialPage();
        await page.Appear();
        Assert.Equal(app["MenuTutorial"], page.Title);
        Assert.Equal(string.Format(app["TutStepOf"], 1, 2), F<Label>(page, "_progress").Text);
        Assert.Equal(2, F<HorizontalStackLayout>(page, "_dots").Count);
        Assert.Equal(app["TutCloudTitle"], F<Label>(page, "_title").Text);
        Assert.Equal(app["TutOptional"], F<Label>(page, "_state").Text);   // en local: opcional
        Assert.True(F<Button>(page, "_action").IsVisible);
        Assert.False(F<Button>(page, "_back").IsVisible);
        Assert.Equal(app["TutNext"], F<Button>(page, "_next").Text);
        Assert.False(page.StepBack());   // en el primero, atras no es de la guia

        // La accion lleva a Ajustes; hecho el paso (boveda en la nube), el estado pasa a hecho.
        await AsyncVoid.Run(F<Button>(page, "_action").SendClicked);
        Assert.Equal("//SettingsPage", app.Navigation.Routes[^1]);
        app.Settings.Storage = StorageMode.GoogleDrive;
        app.Dispatcher.Timers[0].Fire();
        Assert.Equal("✓ " + app["TutDone"], F<Label>(page, "_state").Text);
        Assert.Same(app.App.Resources["OutlineButton"], F<Button>(page, "_action").Style);

        // Si la accion falla, se avisa y la guia sigue.
        app.Navigation.Fail = new InvalidOperationException("no se puede");
        await AsyncVoid.Run(F<Button>(page, "_action").SendClicked);
        Assert.Equal("no se puede", app.Toast.Shown[^1]);
        app.Navigation.Fail = null;

        // Si mirar el estado falla, cuenta como pendiente.
        app.Prefs.Broken.Add("storage");
        app.Dispatcher.Timers[0].Fire();
        Assert.Equal(app["TutOptional"], F<Label>(page, "_state").Text);
        app.Prefs.Broken.Clear();

        await AsyncVoid.Run(F<Button>(page, "_next").SendClicked);
        Assert.Equal(app["TutEndTitle"], F<Label>(page, "_title").Text);
        Assert.Equal(app["TutFinish"], F<Button>(page, "_next").Text);
        Assert.False(F<Label>(page, "_state").IsVisible);
        Assert.False(F<Button>(page, "_action").IsVisible);
        Assert.True(F<Button>(page, "_back").IsVisible);
        await Ui.Call(page, "OnActionClicked");   // el ultimo no tiene accion
        Assert.Single(app.Navigation.Routes);

        // Cambiar de idioma rehace los pasos sin perder el sitio.
        app.Texts.SetLanguage("en");
        Assert.Equal(app["TutEndTitle"], F<Label>(page, "_title").Text);

        Assert.True(page.StepBack());
        Assert.Equal(app["TutCloudTitle"], F<Label>(page, "_title").Text);
        await AsyncVoid.Run(F<Button>(page, "_next").SendClicked);
        await AsyncVoid.Run(F<Button>(page, "_back").SendClicked);
        Assert.Equal(app["TutCloudTitle"], F<Label>(page, "_title").Text);
        await AsyncVoid.Run(F<Button>(page, "_next").SendClicked);

        // Terminar: queda hecha, vuelve al principio y a la boveda.
        await AsyncVoid.Run(F<Button>(page, "_next").SendClicked);
        Assert.True(app.Settings.TutorialDone);
        Assert.Equal("//VaultPage", app.Navigation.Routes[^1]);
        Assert.Equal(0, F<int>(page, "_index"));

        await page.Disappear();
        Assert.False(app.Dispatcher.Timers[0].IsRunning);
        await page.Appear();
        Assert.Single(app.Dispatcher.Timers);
    }

    [Fact]
    public async Task Ir_fuera_de_los_pasos_no_hace_nada()
    {
        using var app = TestHost.Start();
        var page = new TutorialPage();
        await Ui.Call(page, "Rebuild");
        var go = typeof(TutorialPage).GetMethod("Go", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        go.Invoke(page, [5]);
        go.Invoke(page, [-1]);
        Assert.Equal(0, F<int>(page, "_index"));

        // Un indice que ya no existe (menos pasos tras reconstruir) vuelve al primero.
        Ui.SetField(page, "_index", 7);
        await Ui.Call(page, "Rebuild");
        Assert.Equal(0, F<int>(page, "_index"));

        // Sin pasos todavia, el latido no hace nada.
        var fresh = new TutorialPage();
        await Ui.Call(fresh, "RefreshState");
    }
}

public class AboutPageTests
{
    [Fact]
    public async Task Textos_volver_y_escribir()
    {
        using var app = TestHost.Start();
        var page = new AboutPage();
        await page.Appear();
        Assert.Equal(app["AboutTitle"], page.Title);
        Assert.Equal(string.Format(app["AboutVersion"], "2026.10.01.00"), page.Find<Label>("VersionLabel").Text);
        Assert.Equal("jsoladelarosa@gmail.com", page.Find<Button>("ContactButton").Text);
        Assert.Equal(app["AboutWarning"], page.Find<Label>("WarningText").Text);

        await page.Click("BackButton");
        Assert.Equal("//VaultPage", app.Navigation.Routes.Single());

        await Ui.Call(page, "OnContactEmailClicked");
        var mail = app.Email.Sent.Single();
        Assert.Equal(["jsoladelarosa@gmail.com"], mail.To);
        Assert.Equal(app["EmailSubject"], mail.Subject);

        app.Email.Fail = new FeatureNotSupportedException();
        await Ui.Call(page, "OnContactEmailClicked");
        Assert.Equal(app["ErrorEmailNotAvailable"], app.Dialogs.Log[^1].Message);

        app.Email.Fail = new InvalidOperationException("sin cliente");
        await Ui.Call(page, "OnContactEmailClicked");
        Assert.Equal($"{app["ErrorEmail"]}: sin cliente", app.Dialogs.Log[^1].Message);
    }
}

public class ScanPageTests
{
    [Fact]
    public async Task Sin_permiso_de_camara_avisa()
    {
        using var app = TestHost.Start();
        CameraQrScanner.CameraAllowed = () => Task.FromResult(false);
        var owner = new ContentPage();
        var scanner = new CameraQrScanner(app.Texts, app.Dialogs);
        Assert.Null(await scanner.ScanAsync(owner));
        Assert.Equal(app["CameraDenied"], app.Dialogs.Log.Single().Message);
        Assert.Empty(owner.Navigation.ModalStack);
    }

    private static async Task<(TestHost App, ContentPage Owner, ScanPage Page, Task<string?> Result)> Start()
    {
        var app = TestHost.Start();
        CameraQrScanner.CameraAllowed = () => Task.FromResult(true);
        var owner = new ContentPage();
        var result = new CameraQrScanner(app.Texts, app.Dialogs).ScanAsync(owner);
        await Ui.Until(() => owner.Navigation.ModalStack.Count > 0);
        var nav = Assert.IsType<NavigationPage>(owner.Navigation.ModalStack[0]);
        return (app, owner, Assert.IsType<ScanPage>(nav.RootPage), result);
    }

    [Fact]
    public async Task Lee_el_primer_QR_con_texto()
    {
        var (app, _, page, result) = await Start();
        using var _app = app;
        Assert.Equal(app["ScanTitle"], page.Title);
        page.Detected(null);
        page.Detected("");
        Assert.False(result.IsCompleted);
        await AsyncVoid.Run(() => page.Detected("otpauth://totp/X?secret=JBSWY3DPEHPK3PXP"));
        page.Detected("otro");   // ya leido: no cuenta
        Assert.Equal("otpauth://totp/X?secret=JBSWY3DPEHPK3PXP", await result);
    }

    [Fact]
    public async Task Cancelar_devuelve_nada()
    {
        var (app, _, page, result) = await Start();
        using var _app = app;
        var cancel = ((Grid)page.Content).Children.OfType<Button>().Single();
        Assert.Equal(app["Cancel"], cancel.Text);
        await AsyncVoid.Run(cancel.SendClicked);
        Assert.Null(await result);
    }

    [Fact]
    public async Task Atras_devuelve_nada()
    {
        var (app, _, page, result) = await Start();
        using var _app = app;
        page.Back();
        Assert.Null(await result);
        page.Detected("tarde");   // cerrado ya: no cuenta
    }
}
