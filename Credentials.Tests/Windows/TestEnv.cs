using System.Reflection;
using System.Runtime.InteropServices;
using Credentials.Services;

// Lo estatico (las costuras de Win32, Essentials, ServiceHelper) es de todas: una prueba detras de otra.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Credentials.Windows.Tests;

/// <summary>
/// Lo que necesitan las pruebas del codigo de Windows: una carpeta temporal (tambien como SOC_SANDBOX),
/// Preferences, SecureStorage y la carpeta de datos en memoria (con los puntos de inyeccion de MAUI),
/// los servicios de la aplicacion de verdad sobre eso, y una ventana Win32 oculta de prueba que no se
/// enseña nunca. Se crea con <c>using var env = new TestEnv();</c>.
/// </summary>
internal sealed class TestEnv : IDisposable
{
    public TestEnv()
    {
        // Lo primero, MAUI en el hilo de XAML: si el primer elemento de MAUI se crea en otro hilo, su
        // inicializacion falla y ya no se puede usar en todo el proceso.
        WinUIHost.Ensure();
        Root = Path.Combine(Path.GetTempPath(), "soccred-win-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("SOC_SANDBOX", Path.Combine(Root, "data"));
        Use(typeof(Preferences), "SetDefault", Prefs);
        Use(typeof(SecureStorage), "SetDefault", Secure);
        Use(typeof(FileSystem), "SetCurrent", new Files(Root));
        Settings = new SettingsService { Language = "es" };
        Texts = new LocalizationService(Settings, Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalizationService>.Instance);
        Store = new VaultStore(Settings, new NoBrowser());
        Hwnd = CreateWindowEx(0x80 /* WS_EX_TOOLWINDOW */, "STATIC", "sOC prueba", 0x80000000 /* WS_POPUP, sin WS_VISIBLE */, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, Hwnd);
    }

    public string Root { get; }
    public MemoryPreferences Prefs { get; } = new();
    public MemorySecureStorage Secure { get; } = new();
    public SettingsService Settings { get; }
    public LocalizationService Texts { get; }
    public VaultStore Store { get; }

    /// <summary>Ventana «STATIC» oculta de este hilo: nunca se enseña.</summary>
    public IntPtr Hwnd { get; }

    public void Dispose()
    {
        Store.Lock();
        DestroyWindow(Hwnd);
        Environment.SetEnvironmentVariable("SOC_SANDBOX", null);
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public static void Use(Type type, string method, object? implementation) =>
        (type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static) ?? throw new MissingMethodException(type.Name, method)).Invoke(null, [implementation]);

    /// <summary>Atiende los mensajes que esperan en la cola de este hilo (los que manda el menu con PostMessage).</summary>
    public void Pump()
    {
        while (PeekMessage(out var msg, Hwnd, 0, 0, 1 /* PM_REMOVE */))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    public static IntPtr Send(IntPtr hwnd, uint msg, long wParam = 0, long lParam = 0) => SendMessage(hwnd, msg, new IntPtr(wParam), new IntPtr(lParam));

    internal sealed class MemoryPreferences : IPreferences
    {
        public Dictionary<string, object?> Values { get; } = [];
        private static string K(string key, string? shared) => (shared ?? string.Empty) + "|" + key;
        public bool ContainsKey(string key, string? sharedName = null) => Values.ContainsKey(K(key, sharedName));
        public void Remove(string key, string? sharedName = null) => Values.Remove(K(key, sharedName));
        public void Clear(string? sharedName = null) => Values.Clear();
        public void Set<T>(string key, T value, string? sharedName = null) => Values[K(key, sharedName)] = value;
        public T Get<T>(string key, T defaultValue, string? sharedName = null) => Values.TryGetValue(K(key, sharedName), out var v) && v is T t ? t : defaultValue;
    }

    internal sealed class MemorySecureStorage : ISecureStorage
    {
        public Dictionary<string, string> Values { get; } = [];
        public Task<string?> GetAsync(string key) => Task.FromResult(Values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value) { Values[key] = value; return Task.CompletedTask; }
        public bool Remove(string key) => Values.Remove(key);
        public void RemoveAll() => Values.Clear();
    }

    private sealed class Files(string root) : IFileSystem
    {
        public string CacheDirectory { get; } = Directory.CreateDirectory(Path.Combine(root, "cache")).FullName;
        public string AppDataDirectory { get; } = Directory.CreateDirectory(Path.Combine(root, "appdata")).FullName;
        public Task<Stream> OpenAppPackageFileAsync(string filename) => throw new FileNotFoundException(filename);
        public Task<bool> AppPackageFileExistsAsync(string filename) => Task.FromResult(false);
    }

    private sealed class NoBrowser : IOAuthBrowser
    {
        public string RedirectUri(string providerName, string clientId) => "http://127.0.0.1/auth/";
        public Task<Uri> AuthenticateAsync(Uri authorizeUrl, Uri callback, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Msg msg);
}
