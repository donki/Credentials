// Dobles en memoria de las piezas de MAUI que usan los ficheros enlazados. Cada prueba tiene los
// suyos (AsyncLocal): nada toca las preferencias, la boveda del sistema ni la carpeta de datos de
// verdad, y las pruebas pueden correr en paralelo.

namespace Microsoft.Maui.Storage
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static class FileSystem
    {
        private static readonly AsyncLocal<string?> Dir = new();

        public static string AppDataDirectory
        {
            get => Dir.Value ?? throw new InvalidOperationException("La prueba no ha preparado carpeta de datos (Sandbox.Create).");
            set => Dir.Value = value;
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed class SecureStorage
    {
        private static readonly AsyncLocal<SecureStorage?> Current = new();

        public Dictionary<string, string> Values { get; } = [];

        /// <summary>Si es true, leer falla como cuando la boveda del sistema no responde.</summary>
        public bool Broken { get; set; }

        public static SecureStorage Default
        {
            get => Current.Value ??= new SecureStorage();
            set => Current.Value = value;
        }

        public Task<string?> GetAsync(string key) =>
            Broken ? throw new InvalidOperationException("SecureStorage roto") : Task.FromResult(Values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }

        public bool Remove(string key) => Values.Remove(key);
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static class Preferences
    {
        private static readonly AsyncLocal<Dictionary<string, object>?> Current = new();

        public static Dictionary<string, object> Values
        {
            get => Current.Value ??= [];
            set => Current.Value = value;
        }

        public static List<string?> SharedNames { get; } = [];

        private static string K(string key, string? shared)
        {
            lock (SharedNames) { SharedNames.Add(shared); }
            return (shared ?? string.Empty) + "|" + key;
        }

        public static string Get(string key, string defaultValue, string? sharedName) =>
            Values.TryGetValue(K(key, sharedName), out var v) ? (string)v : defaultValue;

        public static int Get(string key, int defaultValue, string? sharedName) =>
            Values.TryGetValue(K(key, sharedName), out var v) ? (int)v : defaultValue;

        public static bool Get(string key, bool defaultValue, string? sharedName) =>
            Values.TryGetValue(K(key, sharedName), out var v) ? (bool)v : defaultValue;

        public static void Set(string key, string value, string? sharedName) => Values[K(key, sharedName)] = value;

        public static void Set(string key, int value, string? sharedName) => Values[K(key, sharedName)] = value;

        public static void Set(string key, bool value, string? sharedName) => Values[K(key, sharedName)] = value;
    }
}

namespace Microsoft.Maui.ApplicationModel
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static class MainThread
    {
        public static void BeginInvokeOnMainThread(Action action) => action();
    }
}

namespace Credentials.Services
{
    /// <summary>
    /// Sustituye al registro de la aplicacion: el de verdad escribe en %LOCALAPPDATA%\sOCCredentials,
    /// que es el del usuario. Aqui se queda en memoria para poder comprobar que se apunta.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static class AppLog
    {
        private static readonly AsyncLocal<List<string>?> Current = new();

        public static List<string> Lines => Current.Value ??= [];

        public static void Write(string message) => Lines.Add(message);

        public static void Error(string where, Exception ex) =>
            Write($"[{where}] {ex.GetType().Name}: {(ex is CloudException c ? c.Detail : ex.Message)}");
    }
}
