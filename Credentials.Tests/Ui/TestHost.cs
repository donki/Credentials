using System.Reflection;
using Credentials.Helpers;
using Credentials.Models;
using Credentials.Services;
using Credentials.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Dispatching;

// Las pruebas comparten lo estatico de MAUI (Application.Current, el despachador, Preferences...):
// una detras de otra.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Credentials.Ui.Tests;

/// <summary>
/// La aplicacion montada para una prueba: carpeta temporal propia (tambien como SOC_SANDBOX, asi que
/// la boveda, los ajustes y el registro van ahi), dobles de todo lo del sistema, el contenedor de
/// servicios con los de verdad (ajustes, textos, boveda con una nube de mentira) y la App con sus
/// recursos. Se crea con <c>using var app = TestHost.Start();</c>.
/// </summary>
internal sealed class TestHost : IDisposable
{
    private TestHost(string language)
    {
        Root = Path.Combine(Path.GetTempPath(), "soccred-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Files = new FakeFileSystem(Root);
        Environment.SetEnvironmentVariable("SOC_SANDBOX", Files.AppDataDirectory);

        Essentials.Use(typeof(Preferences), "SetDefault", Prefs);
        Essentials.Use(typeof(SecureStorage), "SetDefault", Secure);
        Essentials.Use(typeof(FileSystem), "SetCurrent", Files);
        Essentials.Use(typeof(AppInfo), "SetCurrent", AppInfo);
        Essentials.Use(typeof(DeviceInfo), "SetCurrent", Device);
        DispatcherProvider.SetCurrent(new TestDispatcherProvider(Dispatcher));
        // La comprobacion de inactividad de la boveda, en el hilo de la prueba (sin MainThread de MAUI).
        VaultStore.OnMainThread = action => action();

        Settings = new SettingsService { Language = language };
        Texts = new LocalizationService(Settings, Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalizationService>.Instance);
        Store = new VaultStore(Settings, new FakeOAuthBrowser(), Http.Client());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISettingsService>(Settings);
        services.AddSingleton<ILocalizationService>(Texts);
        services.AddSingleton(Store);
        services.AddSingleton<IToastService>(Toast);
        services.AddSingleton<IBiometric>(Biometric);
        services.AddSingleton<IDialogService>(Dialogs);
        services.AddSingleton<INavigationService>(Navigation);
        services.AddSingleton<IQrScanner>(Scanner);
        services.AddSingleton<ITextFilePicker>(Picker);
        services.AddSingleton<IClipboard>(Clipboard);
        services.AddSingleton<IShare>(Share);
        services.AddSingleton<IBrowser>(Browser);
        services.AddSingleton<IEmail>(Email);
        services.AddSingleton<IAppInfo>(AppInfo);
        services.AddSingleton<IDeviceInfo>(Device);
        services.AddSingleton<IFileSystem>(Files);
        Services = services.BuildServiceProvider();
        ServiceHelper.Initialize(Services);

        App = new App();
        Application.Current = App;
    }

    public static TestHost Start(string language = "es") => new(language);

    public string Root { get; }
    public FakeFileSystem Files { get; }
    public FakePreferences Prefs { get; } = new();
    public FakeSecureStorage Secure { get; } = new();
    public FakeAppInfo AppInfo { get; } = new();
    public FakeDeviceInfo Device { get; } = new();
    public TestDispatcher Dispatcher { get; } = new();
    public FakeHttp Http { get; } = new();
    public FakeToast Toast { get; } = new();
    public FakeBiometric Biometric { get; } = new();
    public FakeDialogs Dialogs { get; } = new();
    public FakeNavigation Navigation { get; } = new();
    public FakeScanner Scanner { get; } = new();
    public FakeFilePicker Picker { get; } = new();
    public FakeClipboard Clipboard { get; } = new();
    public FakeShare Share { get; } = new();
    public FakeBrowser Browser { get; } = new();
    public FakeEmail Email { get; } = new();
    public SettingsService Settings { get; }
    public LocalizationService Texts { get; }
    public VaultStore Store { get; }
    public IServiceProvider Services { get; }
    public App App { get; }

    public string this[string key] => Texts[key];

    /// <summary>Crea la boveda (abierta) con la contraseña dada y, si se pasan, unas entradas.</summary>
    public async Task<VaultData> CreateVaultAsync(string password = "contraseña-larga", params Credential[] entries)
    {
        await Store.CreateAsync(password);
        Store.Data!.Entries.AddRange(entries);
        await Store.SaveAsync(upload: false);
        return Store.Data;
    }

    public void Dispose()
    {
        Store.Lock();
        // Al bloquear, las paginas que siguen vivas sacan la puerta y se quedan esperandola: fuera,
        // para que la siguiente prueba empiece sin puerta.
        typeof(Pages.Gate).GetField("_showing", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);
        typeof(Pages.Gate).GetProperty(nameof(Pages.Gate.Current))!.SetValue(null, null);
        Application.Current = null;
        Environment.SetEnvironmentVariable("SOC_SANDBOX", null);
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>El navegador de la entrada en la nube: vuelve al momento con un codigo, sin abrir nada.</summary>
internal sealed class FakeOAuthBrowser : IOAuthBrowser
{
    public string Callback { get; set; } = "http://127.0.0.1:5000/auth/?code=codigo-1&state={state}";
    public Exception? Fail { get; set; }

    public string RedirectUri(string providerName, string clientId) => "http://127.0.0.1:5000/auth/";

    public Task<Uri> AuthenticateAsync(Uri authorizeUrl, Uri callback, CancellationToken cancellationToken)
    {
        if (Fail is not null)
            throw Fail;
        var state = System.Web.HttpUtility.ParseQueryString(authorizeUrl.Query)["state"] ?? string.Empty;
        return Task.FromResult(new Uri(Callback.Replace("{state}", state)));
    }
}

/// <summary>
/// Para los manejadores «async void» de las paginas (los que engancha el XAML): los ejecuta y espera
/// a que acaben de verdad, con todo lo que esperan dentro. Si uno lanza, la prueba falla con su error.
/// </summary>
internal sealed class AsyncVoid : SynchronizationContext
{
    private int _pending;
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _error;

    public override void OperationStarted() => Interlocked.Increment(ref _pending);

    public override void OperationCompleted()
    {
        if (Interlocked.Decrement(ref _pending) == 0)
            _done.TrySetResult();
    }

    public override void Post(SendOrPostCallback d, object? state) => ThreadPool.QueueUserWorkItem(_ =>
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try { d(state); }
        catch (Exception ex) { _error ??= ex; _done.TrySetResult(); }
        finally { SetSynchronizationContext(previous); }
    });

    public override void Send(SendOrPostCallback d, object? state) => d(state);

    /// <summary>Ejecuta <paramref name="action"/> y espera a todos los «async void» que arranque.</summary>
    public static async Task Run(Action action, int seconds = 30)
    {
        var context = new AsyncVoid();
        var previous = Current;
        SetSynchronizationContext(context);
        try { action(); }
        finally { SetSynchronizationContext(previous); }
        if (Volatile.Read(ref context._pending) > 0)
            await context._done.Task.WaitAsync(TimeSpan.FromSeconds(seconds));
        if (context._error is not null)
            throw new InvalidOperationException("Un manejador async void ha fallado.", context._error);
    }
}

/// <summary>Atajos para tocar las paginas como el usuario.</summary>
internal static class Ui
{
    public static T Find<T>(this Element page, string name) where T : Element =>
        page.FindByName<T>(name) ?? throw new InvalidOperationException($"No hay «{name}» en {page.GetType().Name}.");

    /// <summary>Pulsa un boton (su manejador Clicked de verdad) y espera a que acabe.</summary>
    public static Task Click(this Element page, string name) => AsyncVoid.Run(() => page.Find<Button>(name).SendClicked());

    public static Task ClickImage(this Element page, string name) => AsyncVoid.Run(() => page.Find<ImageButton>(name).SendClicked());

    /// <summary>Llama a un manejador privado por su nombre (los botones sin nombre del XAML) y espera a que acabe.</summary>
    public static Task Call(object target, string method, object? sender = null, EventArgs? args = null)
    {
        MethodInfo? m = null;
        for (var t = target.GetType(); m is null && t is not null; t = t.BaseType)
            m = t.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
        if (m is null)
            throw new MissingMethodException(target.GetType().Name, method);
        var parameters = m.GetParameters();
        object?[] values = parameters.Length switch
        {
            0 => [],
            2 => [sender, args ?? EventArgs.Empty],
            _ => throw new NotSupportedException(method),
        };
        object? result = null;
        return AsyncVoid.Run(() => result = m.Invoke(target, values)).ContinueWith(async t =>
        {
            await t;
            if (result is Task task)
                await task;
        }).Unwrap();
    }

    /// <summary>El valor de un campo privado (para comprobar el estado interno que la pantalla no enseña).</summary>
    public static T Field<T>(object target, string name) =>
        (T)(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(target.GetType().Name, name)).GetValue(target)!;

    public static void SetField(object target, string name, object? value) =>
        (target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(target.GetType().Name, name)).SetValue(target, value);

    /// <summary>La puerta de desbloqueo que la pagina ha sacado encima (modal), cuando sale.</summary>
    public static async Task<Pages.UnlockPage> GateOn(Page owner)
    {
        await Until(() => owner.Navigation.ModalStack.Count > 0);
        var gate = Assert.IsType<Pages.UnlockPage>(owner.Navigation.ModalStack[^1]);
        await Show(gate);
        return gate;
    }

    /// <summary>La pagina sale en pantalla (su Appearing de verdad, con su OnAppearing).</summary>
    public static Task Show(Page page) => Call(page, "OnAppearing");

    /// <summary>Quita la puerta como lo hace la ventana al cerrarse el modal (su Disappearing).</summary>
    public static void Dismiss(Page owner, Page modal)
    {
        if (owner.Navigation.ModalStack.Contains(modal))
            owner.Navigation.PopModalAsync().GetAwaiter().GetResult();
        // Sin ventana, MAUI no lanza Appearing/Disappearing: se lanza a mano, como lo haria ella.
        var handler = (EventHandler?)typeof(Page).GetField("Disappearing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(modal);
        handler?.Invoke(modal, EventArgs.Empty);
    }

    /// <summary>Abre la boveda desde la puerta que ha salido, como el usuario, y la quita.</summary>
    public static async Task UnlockThrough(Page owner, string password)
    {
        var gate = await GateOn(owner);
        gate.Find<Entry>("PasswordEntry").Text = password;
        await gate.Click("GoButton");
        Dismiss(owner, gate);
        await Until(() => !GateShowing);
    }

    /// <summary>Si la puerta de desbloqueo sigue a la vista (Gate).</summary>
    public static bool GateShowing => (bool)typeof(Pages.Gate).GetField("_showing", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    /// <summary>Lanza el ciclo de vida de la pagina como al enseñarla o quitarla.</summary>
    public static Task Appear(this Page page) => Call(page, "OnAppearing");

    public static Task Disappear(this Page page) => Call(page, "OnDisappearing");

    /// <summary>Atras del sistema sobre la pagina: lo que devuelve OnBackButtonPressed.</summary>
    public static bool Back(this Page page)
    {
        var m = page.GetType().GetMethod("OnBackButtonPressed", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            ?? typeof(Page).GetMethod("OnBackButtonPressed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var handled = false;
        AsyncVoid.Run(() =>
        {
            try { handled = (bool)m.Invoke(page, null)!; }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerException);
            }
        }).GetAwaiter().GetResult();
        return handled;
    }

    /// <summary>Espera a que se cumpla algo que pasa en segundo plano (como mucho unos segundos).</summary>
    public static async Task Until(Func<bool> condition, int seconds = 15)
    {
        var limit = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > limit)
                throw new TimeoutException("No se cumplio la condicion a tiempo.");
            await Task.Delay(20);
        }
    }
}
