using System.Reflection;
using Credentials.Services;
using Microsoft.Maui.Animations;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Dispatching;

namespace Credentials.Ui.Tests;

// Dobles en memoria de lo que en la aplicacion de verdad pone el sistema: hilo de la interfaz,
// temporizadores, Preferences, SecureStorage, carpeta de datos, portapapeles, compartir, navegador,
// correo, informacion de la aplicacion y del dispositivo, dialogos, navegacion del Shell, lector QR
// y biometria. Nada sale del proceso ni toca lo del usuario.

/// <summary>El hilo de la interfaz: todo se ejecuta al momento, en el hilo de la prueba.</summary>
internal sealed class TestDispatcher : IDispatcher
{
    public List<TestTimer> Timers { get; } = [];

    /// <summary>Si es true, como si se llamara desde otro hilo: lo despachado queda en <see cref="Queued"/>.</summary>
    public bool Required { get; set; }
    public List<Action> Queued { get; } = [];

    public bool IsDispatchRequired => Required;

    public bool Dispatch(Action action)
    {
        if (Required)
            Queued.Add(action);
        else
            action();
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
    {
        action();
        return true;
    }

    public IDispatcherTimer CreateTimer()
    {
        var timer = new TestTimer();
        Timers.Add(timer);
        return timer;
    }
}

/// <summary>Temporizador que solo late cuando la prueba lo dice (<see cref="Fire"/>).</summary>
internal sealed class TestTimer : IDispatcherTimer
{
    public TimeSpan Interval { get; set; }
    public bool IsRepeating { get; set; } = true;
    public bool IsRunning { get; private set; }
    public event EventHandler? Tick;
    public void Start() => IsRunning = true;
    public void Stop() => IsRunning = false;
    public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
}

internal sealed class TestDispatcherProvider(TestDispatcher dispatcher) : IDispatcherProvider
{
    public IDispatcher? GetForCurrentThread() => dispatcher;
}

internal sealed class FakePreferences : IPreferences
{
    public Dictionary<string, object?> Values { get; } = [];

    private static string K(string key, string? shared) => (shared ?? string.Empty) + "|" + key;

    public bool ContainsKey(string key, string? sharedName = null) => Values.ContainsKey(K(key, sharedName));
    public void Remove(string key, string? sharedName = null) => Values.Remove(K(key, sharedName));
    public void Clear(string? sharedName = null) => Values.Clear();
    public void Set<T>(string key, T value, string? sharedName = null) => Values[K(key, sharedName)] = value;
    public T Get<T>(string key, T defaultValue, string? sharedName = null) =>
        Broken.Contains(key) ? throw new InvalidOperationException("Preferences roto: " + key)
        : Values.TryGetValue(K(key, sharedName), out var v) && v is T t ? t : defaultValue;

    /// <summary>Claves cuya lectura falla (como un almacen del sistema estropeado).</summary>
    public HashSet<string> Broken { get; } = [];
}

internal sealed class FakeSecureStorage : ISecureStorage
{
    public Dictionary<string, string> Values { get; } = [];
    public Task<string?> GetAsync(string key) => Task.FromResult(Values.GetValueOrDefault(key));
    public Task SetAsync(string key, string value)
    {
        Values[key] = value;
        return Task.CompletedTask;
    }
    public bool Remove(string key) => Values.Remove(key);
    public void RemoveAll() => Values.Clear();
}

internal sealed class FakeFileSystem(string root) : IFileSystem
{
    public string CacheDirectory { get; } = Directory.CreateDirectory(Path.Combine(root, "cache")).FullName;
    public string AppDataDirectory { get; } = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
    public Task<Stream> OpenAppPackageFileAsync(string filename) => throw new FileNotFoundException(filename);
    public Task<bool> AppPackageFileExistsAsync(string filename) => Task.FromResult(false);
}

internal sealed class FakeClipboard : IClipboard
{
    public string? Text { get; set; }
    public List<string?> History { get; } = [];
    public bool HasText => !string.IsNullOrEmpty(Text);
    public event EventHandler<EventArgs>? ClipboardContentChanged;
    public Task SetTextAsync(string? text)
    {
        Text = text;
        History.Add(text);
        ClipboardContentChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
    public bool FailRead { get; set; }
    public Task<string?> GetTextAsync() => FailRead ? throw new InvalidOperationException("portapapeles ocupado") : Task.FromResult(Text);
}

internal sealed class FakeShare : IShare
{
    public List<ShareFileRequest> Files { get; } = [];
    public Task RequestAsync(ShareTextRequest request) => Task.CompletedTask;
    public Task RequestAsync(ShareFileRequest request)
    {
        Files.Add(request);
        return Task.CompletedTask;
    }
    public Task RequestAsync(ShareMultipleFilesRequest request) => Task.CompletedTask;
}

internal sealed class FakeBrowser : IBrowser
{
    public List<Uri> Opened { get; } = [];
    public Exception? Fail { get; set; }
    public Task<bool> OpenAsync(Uri uri, BrowserLaunchOptions options)
    {
        if (Fail is not null)
            throw Fail;
        Opened.Add(uri);
        return Task.FromResult(true);
    }
}

internal sealed class FakeEmail : IEmail
{
    public List<EmailMessage> Sent { get; } = [];
    public Exception? Fail { get; set; }
    public bool IsComposeSupported => true;
    public Task ComposeAsync(EmailMessage? message)
    {
        if (Fail is not null)
            throw Fail;
        Sent.Add(message!);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAppInfo : IAppInfo
{
    public string PackageName => "com.socratic.credentials";
    public string Name => "sOC Credentials";
    public string VersionString => "2026.10.01.00";
    public Version Version => new(2026, 10, 1, 0);
    public string BuildString => "2026100100";
    public void ShowSettingsUI() { }
    public AppTheme RequestedTheme => AppTheme.Light;
    public AppPackagingModel PackagingModel => AppPackagingModel.Unpackaged;
    public LayoutDirection RequestedLayoutDirection => LayoutDirection.LeftToRight;
}

internal sealed class FakeDeviceInfo : IDeviceInfo
{
    public DevicePlatform Platform { get; set; } = DevicePlatform.Android;
    public string Model => "Prueba";
    public string Manufacturer => "Socratic";
    public string Name => "Banco de pruebas";
    public string VersionString => "1.0";
    public Version Version => new(1, 0);
    public DeviceIdiom Idiom => DeviceIdiom.Phone;
    public DeviceType DeviceType => DeviceType.Virtual;
}

internal sealed class FakeToast : IToastService
{
    public List<string> Shown { get; } = [];
    public void Show(string message)
    {
        lock (Shown)
            Shown.Add(message);
    }
}

internal sealed class FakeBiometric : IBiometric
{
    public bool Available { get; set; }
    public bool Pass { get; set; }
    public int Asked { get; private set; }
    public bool Fail { get; set; }
    public Task<bool> IsAvailableAsync() => Fail ? throw new InvalidOperationException("sin sensor") : Task.FromResult(Available);
    public Task<bool> AuthenticateAsync(string title, string reason)
    {
        Asked++;
        return Task.FromResult(Pass);
    }
}

internal sealed class FakeNavigation : INavigationService
{
    public List<string> Routes { get; } = [];
    public List<Page> Pushed { get; } = [];
    public Exception? Fail { get; set; }
    public Task GoToAsync(string route)
    {
        if (Fail is not null)
            throw Fail;
        Routes.Add(route);
        return Task.CompletedTask;
    }
    public Task PushAsync(Page page)
    {
        Pushed.Add(page);
        return Task.CompletedTask;
    }
}

internal sealed class FakeScanner : IQrScanner
{
    public Queue<string?> Results { get; } = new();
    public Task<string?> ScanAsync(Page owner) => Task.FromResult(Results.Count > 0 ? Results.Dequeue() : null);
}

internal sealed class FakeFilePicker : ITextFilePicker
{
    public Queue<Func<string?>> Results { get; } = new();
    public Task<string?> PickTextAsync(string title) => Task.FromResult(Results.Count > 0 ? Results.Dequeue()() : null);
}

/// <summary>
/// El usuario delante de los dialogos: cada pregunta saca la siguiente respuesta de la cola (si no
/// hay, cancela) y queda apuntada para comprobar que se pregunto lo que tocaba.
/// </summary>
internal sealed class FakeDialogs : IDialogService
{
    public sealed record Shown(string Kind, string? Title, string? Message, string[] Options);

    public List<Shown> Log { get; } = [];
    public Queue<object?> Answers { get; } = new();

    public FakeDialogs Answer(params object?[] answers)
    {
        foreach (var a in answers)
            Answers.Enqueue(a);
        return this;
    }

    private object? Next() => Answers.Count > 0 ? Answers.Dequeue() : null;

    public Task<bool> AlertAsync(Page page, string title, string message, string accept, string? cancel = null)
    {
        Log.Add(new("alert", title, message, cancel is null ? [accept] : [accept, cancel]));
        // Sin boton de cancelar no hay nada que decidir; con el, lo dice la cola (por defecto, no).
        return Task.FromResult(cancel is null || Next() is true);
    }

    public Task<string?> ActionSheetAsync(Page page, string? title, string cancel, params string[] options)
    {
        Log.Add(new("sheet", title, null, options));
        return Task.FromResult(Next() as string);
    }

    public Task<string?> PromptAsync(Page page, string title, string? message, string accept, string cancel, bool isPassword = false)
    {
        Log.Add(new("prompt", title, message, [accept, cancel]));
        return Task.FromResult(Next() as string);
    }
}

/// <summary>
/// Las piezas de MAUI Essentials que leen los servicios de la aplicacion (SettingsService con
/// Preferences, VaultStore con SecureStorage y la carpeta de datos) se cambian por los dobles con
/// los puntos de inyeccion que MAUI tiene para sus propias pruebas (SetDefault/SetCurrent).
/// </summary>
internal static class Essentials
{
    public static void Use(Type type, string method, object implementation)
    {
        var m = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(type.Name, method);
        m.Invoke(null, [implementation]);
    }
}
