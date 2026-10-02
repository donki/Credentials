using Credentials.Pages;
using Credentials.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Animations;

namespace Credentials.Ui.Tests;

/// <summary>Los dialogos de verdad (ModernDialog, una capa dentro de la pagina), pulsando sus botones.</summary>
public class DialogTests
{
    /// <summary>Lo minimo para que MAUI anime sin pantalla: un contexto con su gestor de animaciones en la App.</summary>
    private sealed class AppHandler(IMauiContext context) : IElementHandler
    {
        public void SetMauiContext(IMauiContext c) => MauiContext = c;
        public void SetVirtualView(IElement view) => VirtualView = view;
        public void UpdateValue(string property) { }
        public void Invoke(string command, object? args = null) { }
        public void DisconnectHandler() { }
        public object? PlatformView => null;
        public IElement? VirtualView { get; private set; }
        public IMauiContext? MauiContext { get; private set; } = context;
    }

    private static TestHost Start()
    {
        var app = TestHost.Start();
        var services = new ServiceCollection().AddSingleton<IAnimationManager>(new AnimationManager(new Ticker())).BuildServiceProvider();
        app.App.Handler = new AppHandler(new MauiContext(services));
        return app;
    }

    /// <summary>Los botones del dialogo que esta a la vista en la pagina.</summary>
    private static List<Button> Buttons(ContentPage page)
    {
        var overlay = ((Grid)page.Content!).Children.OfType<Grid>().Last();
        return overlay.Children.OfType<Border>().Single().GetVisualTreeDescendants().OfType<Button>().ToList();
    }

    private static Entry Box(ContentPage page) =>
        ((Grid)page.Content!).Children.OfType<Grid>().Last().GetVisualTreeDescendants().OfType<Entry>().Single();

    [Fact]
    public async Task Aviso_confirmacion_lista_y_casilla()
    {
        using var app = Start();
        var dialogs = new ModernDialogService();
        var page = new ContentPage { Content = new Label { Text = "pagina" } };

        var ok = dialogs.AlertAsync(page, "Titulo", "Mensaje", "Si", "No");
        Buttons(page).Single(b => b.Text == "Si").SendClicked();
        Assert.True(await ok.WaitAsync(TimeSpan.FromSeconds(10)));

        var no = dialogs.AlertAsync(page, "Titulo", "Mensaje", "Si", "No");
        Buttons(page).Single(b => b.Text == "No").SendClicked();
        Assert.False(await no.WaitAsync(TimeSpan.FromSeconds(10)));

        var sheet = dialogs.ActionSheetAsync(page, "Elige", "Cancelar", "Uno", "Dos");
        Buttons(page).Single(b => b.Text == "Dos").SendClicked();
        Assert.Equal("Dos", await sheet.WaitAsync(TimeSpan.FromSeconds(10)));

        var prompt = dialogs.PromptAsync(page, "Clave", "Escribela", "Vale", "Cancelar", isPassword: true);
        Assert.True(Box(page).IsPassword);
        Box(page).Text = "secreto";
        Buttons(page).Single(b => b.Text == "Vale").SendClicked();
        Assert.Equal("secreto", await prompt.WaitAsync(TimeSpan.FromSeconds(10)));

        // Cerrado el ultimo, la pagina se queda solo con lo suyo.
        await Ui.Until(() => ((Grid)page.Content!).Children.Count == 1);
    }

    [Fact]
    public async Task El_aviso_de_error_con_los_textos_de_la_app()
    {
        using var app = Start();
        CrashReporting.Install();
        var page = new ContentPage { Content = new Label() };
        var shown = SocShared.CrashGuard.Alert!(page, "t", "m", "ok");
        var card = ((Grid)page.Content!).Children.OfType<Grid>().Last().GetVisualTreeDescendants().OfType<Label>().Select(l => l.Text).ToList();
        Assert.Contains(app["CrashTitle"], card);
        Assert.Contains(app["CrashText"], card);
        Buttons(page).Single(b => b.Text == app["Ok"]).SendClicked();
        await shown.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
