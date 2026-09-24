using Credentials.Helpers;
using Credentials.Services;

namespace Credentials.Pages;

/// <summary>
/// Guia de configuracion paso a paso: una pantalla por paso con lo que hay que hacer, un boton que
/// lo hace (o abre la pantalla del sistema o del navegador donde se hace) y el estado del paso, que
/// se vuelve a mirar solo cada dos segundos y al volver a la aplicacion. En Windows: bandeja y
/// arranque, autocompletar de escritorio, la extension en cada navegador del PC y el gestor propio
/// del navegador; en Android: servicio de autocompletar, servicio preferido, cada navegador y la
/// huella. Sale sola tras el primer desbloqueo y se puede abrir cuando se quiera desde el menu.
/// </summary>
public class TutorialPage : ContentPage
{
    private sealed record Step(
        string Icon,
        string Title,
        string Body,
        Func<bool>? IsDone = null,
        string? ActionText = null,
        Func<Task>? Action = null,
        bool Optional = false);

    private readonly ILocalizationService _l;
    private readonly ISettingsService _settings;
    private readonly IToastService _toast;
    private List<Step> _steps = [];
    private int _index;
    private IDispatcherTimer? _timer;

    private readonly Label _progress = new() { FontSize = 13, HorizontalOptions = LayoutOptions.Center };
    private readonly HorizontalStackLayout _dots = new() { Spacing = 6, HorizontalOptions = LayoutOptions.Center };
    private readonly Image _icon = new() { WidthRequest = 36, HeightRequest = 36, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
    private readonly Label _title = new() { FontSize = 22, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
    private readonly Label _body = new() { FontSize = 15, LineHeight = 1.25 };
    private readonly Label _state = new() { FontSize = 14, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center };
    private readonly Button _action = new() { ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 8), LineBreakMode = LineBreakMode.WordWrap };
    private readonly Button _back = new() { ImageSource = "ic_back.png", ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 6), LineBreakMode = LineBreakMode.WordWrap };
    private readonly Button _next = new() { ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Right, 6), LineBreakMode = LineBreakMode.WordWrap };

    public TutorialPage()
    {
        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _settings = ServiceHelper.GetRequiredService<ISettingsService>();
        _toast = ServiceHelper.GetRequiredService<IToastService>();

        _action.Style = Res("PrimaryButton");
        _back.Style = Res("OutlineButton");
        _next.Style = Res("PrimaryButton");
        // Sin altura fija: con la letra del sistema en grande el texto parte de linea y el boton crece.
        foreach (var b in new[] { _action, _back, _next })
        {
            b.HeightRequest = -1;
            b.MinimumHeightRequest = 48;
            b.Padding = new Thickness(14, 10);
        }
        // El titulo con el color de texto del tema (sin estilo, Android lo pintaba gris).
        _title.SetAppThemeColor(Label.TextColorProperty, (Color)Application.Current!.Resources["TextPrimaryLight"], (Color)Application.Current.Resources["TextPrimaryDark"]);
        _progress.Style = Res("HintText");
        _body.Style = Res("BodyText");
        _action.Clicked += OnActionClicked;
        _back.Clicked += (_, _) => Go(_index - 1);
        _next.Clicked += OnNextClicked;

        var card = new Border
        {
            Style = Res("Card"),
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20, 24),
                Spacing = 16,
                Children =
                {
                    // El icono del paso, en un circulo de la marca.
                    new Border
                    {
                        WidthRequest = 72,
                        HeightRequest = 72,
                        HorizontalOptions = LayoutOptions.Center,
                        StrokeThickness = 0,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 36 },
                        BackgroundColor = Color.FromArgb("#263525CD"),
                        Content = _icon,
                    },
                    _title, _body, _state, _action,
                },
            },
        };
        var nav = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 12,
        };
        nav.Add(_back, 0, 0);
        nav.Add(_next, 1, 0);

        // Anterior / Siguiente fijos abajo: con la letra grande el texto del paso se desplaza, pero
        // los botones para avanzar siempre se ven.
        var root = new Grid { RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)] };
        root.Add(new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 16, 16, 8),
                Spacing = 14,
                MaximumWidthRequest = 640,
                Children = { _progress, _dots, card },
            },
        }, 0, 0);
        nav.Padding = new Thickness(16, 8, 16, 16);
        nav.MaximumWidthRequest = 640;
        root.Add(nav, 0, 1);
        Content = root;

        _l.LanguageChanged += (_, _) => Rebuild();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Title = _l["MenuTutorial"];
        Rebuild();
        _timer ??= Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(2);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
        // Al volver de una pantalla del sistema o del navegador, el temporizador recoge el cambio.
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop();
    }

    private void OnTick(object? sender, EventArgs e) => RefreshState();

    private static Style? Res(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true ? v as Style : null;

    private string F(string key, params object[] args) => string.Format(_l.CurrentCulture, _l[key], args);

    // ------------------------------------------------------------------ pasos

    private void Rebuild()
    {
        _steps = BuildSteps();
        if (_index >= _steps.Count)
            _index = 0;
        _back.Text = _l["TutBack"];
        Show();
    }

    private List<Step> BuildSteps()
    {
        var steps = new List<Step>();
#if WINDOWS
        steps.Add(new("ic_help.png", _l["TutWelcomeTitle"], _l["TutWelcomeWindows"]));
        steps.Add(new("ic_app.png", _l["TutTrayTitle"], _l["TutTrayBody"],
            () => _settings.TrayOnMinimize && Platforms.Windows.WindowsStartup.IsEnabled("sOCCredentials"),
            _l["TutTrayAction"],
            () =>
            {
                _settings.TrayOnMinimize = true;
                if (App.Tray is { } tray)
                    tray.MinimizeToTray = true;
                Platforms.Windows.WindowsStartup.Set("sOCCredentials", true);
                return Task.CompletedTask;
            }));
        steps.Add(new("ic_key.png", _l["TutDesktopTitle"], _l["TutDesktopBody"],
            () => _settings.DesktopAutofill,
            _l["TutDesktopAction"],
            () => { _settings.DesktopAutofill = true; return Task.CompletedTask; }));
        var browsers = Platforms.Windows.ExtensionInstaller.Available
            ? Platforms.Windows.ExtensionInstaller.Detected()
            : [];
        if (browsers.Count == 0)
            steps.Add(new("ic_web.png", _l["TutNoBrowsersTitle"], _l["TutNoBrowsersBody"]));
        foreach (var b in browsers)
        {
            var browser = b;
            steps.Add(new("ic_web.png", F("TutExtTitle", b.Name),
                F(b.StoreUrl is not null ? "TutExtBodyStore" : b.IsFirefox ? "TutExtBodyFirefox" : "TutExtBodyChromium", b.Name),
                () => _settings.ExtensionSeen(browser.Key) is not null,
                F("TutExtAction", b.Name),
                () => Platforms.Windows.ExtensionSetup.InstallAsync(this, browser, _l)));
        }
        if (browsers.Count > 0)
            steps.Add(new("ic_lock.png", _l["TutBrowserPmTitle"], _l["TutBrowserPmWindows"]));
#elif ANDROID
        steps.Add(new("ic_help.png", _l["TutWelcomeTitle"], _l["TutWelcomeAndroid"]));
        if (Platforms.Android.AutofillSetup.Supported)
            steps.Add(new("ic_key.png", _l["TutAutofillTitle"], _l["TutAutofillBody"],
                () => Platforms.Android.AutofillSetup.IsOurs,
                _l["AutofillUseThis"],
                () => { Platforms.Android.AutofillSetup.Request(); return Task.CompletedTask; }));
        if (Platforms.Android.AutofillSetup.HasPreferredService)
            steps.Add(new("ic_settings.png", _l["TutPreferredTitle"], _l["TutPreferredBody"],
                null,
                _l["AutofillPreferredOpen"],
                () =>
                {
                    if (!Platforms.Android.AutofillSetup.OpenPreferredService())
                        _toast.Show(_l["AutofillPreferredNoScreen"]);
                    return Task.CompletedTask;
                }));
        foreach (var (package, name) in Platforms.Android.AutofillSetup.InstalledBrowsers())
        {
            var pkg = package;
            var body = package == "org.mozilla.firefox" ? F("TutBrowserFirefox", name) : F("TutBrowserChromium", name);
            steps.Add(new("ic_web.png", F("TutBrowserTitle", name), body,
                null,
                F("TutBrowserAction", name),
                () =>
                {
                    if (!Platforms.Android.AutofillSetup.OpenBrowserSettings(pkg))
                        _toast.Show(_l["AutofillPreferredNoScreen"]);
                    return Task.CompletedTask;
                }));
        }
        steps.Add(new("ic_fingerprint.png", _l["TutBioTitle"], _l["TutBioBody"],
            () => _settings.Biometrics,
            _l["TutOpenSettings"],
            () => Shell.Current.GoToAsync("//SettingsPage"),
            Optional: true));
#endif
        steps.Add(new("ic_sync.png", _l["TutCloudTitle"], _l["TutCloudBody"],
            () => _settings.Storage != StorageMode.Local,
            _l["TutOpenSettings"],
            () => Shell.Current.GoToAsync("//SettingsPage"),
            Optional: true));
        steps.Add(new("ic_check.png", _l["TutEndTitle"], _l["TutEndBody"]));
        return steps;
    }

    // ------------------------------------------------------------------ pantalla

    private void Go(int index)
    {
        if (index < 0 || index >= _steps.Count)
            return;
        _index = index;
        Show();
    }

    private void Show()
    {
        var step = _steps[_index];
        var last = _index == _steps.Count - 1;
        _progress.Text = F("TutStepOf", _index + 1, _steps.Count);
        _icon.Source = step.Icon;
        _title.Text = step.Title;
        _body.Text = step.Body;
        _action.IsVisible = step.Action is not null;
        _action.Text = step.ActionText;
        _back.IsVisible = _index > 0;
        _next.Text = last ? _l["TutFinish"] : _l["TutNext"];
        _next.ImageSource = last ? "ic_check_w.png" : "ic_next_w.png";
        _dots.Clear();
        for (var i = 0; i < _steps.Count; i++)
        {
            _dots.Add(new BoxView
            {
                WidthRequest = i == _index ? 20 : 8,
                HeightRequest = 8,
                CornerRadius = 4,
                Color = (Color)Application.Current!.Resources[i <= _index ? "Primary" : "SeparatorLight"],
            });
        }
        RefreshState();
    }

    /// <summary>El estado del paso actual: hecho (en verde), pendiente, u opcional si se puede saltar.</summary>
    private void RefreshState()
    {
        if (_steps.Count == 0)
            return;
        var step = _steps[_index];
        bool done;
        try { done = step.IsDone?.Invoke() ?? false; }
        catch (Exception) { done = false; }
        if (step.IsDone is null)
        {
            _state.IsVisible = false;
        }
        else
        {
            _state.IsVisible = true;
            _state.Text = done ? "✓ " + _l["TutDone"] : (step.Optional ? _l["TutOptional"] : _l["TutPending"]);
            _state.TextColor = (Color)Application.Current!.Resources[done ? "Success" : (step.Optional ? "Primary" : "Danger")];
        }
        // Hecho el paso, su boton pasa a secundario: lo importante ya es seguir.
        _action.Style = Res(done ? "OutlineButton" : "PrimaryButton");
        _action.ImageSource = done ? "ic_open.png" : "ic_open_w.png";
    }

    private async void OnActionClicked(object? sender, EventArgs e)
    {
        var step = _steps[_index];
        if (step.Action is null)
            return;
        try { await step.Action(); }
        catch (Exception ex) { _toast.Show(ex.Message); }
        RefreshState();
    }

    private async void OnNextClicked(object? sender, EventArgs e)
    {
        if (_index < _steps.Count - 1)
        {
            Go(_index + 1);
            return;
        }
        _settings.TutorialDone = true;
        _index = 0;
        await Shell.Current.GoToAsync("//VaultPage");
    }
}
