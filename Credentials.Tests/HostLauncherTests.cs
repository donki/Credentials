using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Credentials.Host;
using Credentials.Launcher;

namespace Credentials.Tests;

/// <summary>El host de mensajeria nativa (marcos del navegador, el puente con la aplicacion) y el lanzador de un solo exe.</summary>
public class HostLauncherTests
{
    private static byte[] Frame(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        return [.. BitConverter.GetBytes(body.Length), .. body];
    }

    private static List<string> Frames(byte[] data)
    {
        var list = new List<string>();
        using var s = new MemoryStream(data);
        while (NativeMessaging.ReadFrame(s) is { } f)
            list.Add(f);
        return list;
    }

    [Fact]
    public void Frames_RoundTrip_AndRejectNonsense()
    {
        var output = new MemoryStream();
        NativeMessaging.WriteFrame(output, "{\"id\":1,\"texto\":\"contraseña ✓\"}");
        NativeMessaging.WriteFrame(output, "{}");
        Assert.Equal(["{\"id\":1,\"texto\":\"contraseña ✓\"}", "{}"], Frames(output.ToArray()));

        // Longitud cero, negativa o descomunal: el navegador no manda eso; se trata como fin.
        Assert.Null(NativeMessaging.ReadFrame(new MemoryStream(BitConverter.GetBytes(0))));
        Assert.Null(NativeMessaging.ReadFrame(new MemoryStream(BitConverter.GetBytes(-5))));
        Assert.Null(NativeMessaging.ReadFrame(new MemoryStream(BitConverter.GetBytes(NativeMessaging.MaxLength + 1))));
        // Cabecera o cuerpo cortados.
        Assert.Null(NativeMessaging.ReadFrame(new MemoryStream([1, 0])));
        Assert.Null(NativeMessaging.ReadFrame(new MemoryStream(Frame("{\"a\":1}")[..6])));
        Assert.Null(NativeMessaging.ReadFrame(new MemoryStream()));
    }

    [Fact]
    public void ReadExactly_JoinsPartialReads()
    {
        var data = Frame("{\"id\":7}");
        var s = new TrickleStream(data);
        Assert.Equal("{\"id\":7}", NativeMessaging.ReadFrame(s));
        Assert.True(s.Reads > 3);
    }

    [Theory]
    [InlineData("{\"id\": 42,\"type\":\"list\"}", "42")]
    [InlineData("{\"type\":\"list\",\"id\":7}", "7")]
    [InlineData("{\"type\":\"list\"}", "null")]
    [InlineData("{\"id\":\"texto\"}", "null")]
    [InlineData("{\"id\"", "null")]
    public void Error_KeepsNumericId(string request, string id)
    {
        var error = NativeMessaging.Error(request, "noapp", "linea 1\nla \"app\" en C:\\x");
        Assert.Equal("{\"id\":" + id + ",\"error\":\"noapp\",\"detail\":\"linea 1 la 'app' en C:\\\\x\"}", error);
        System.Text.Json.JsonDocument.Parse(error).Dispose();
    }

    [Fact]
    public void Relay_PassesLines_AndReconnectsOnce()
    {
        var app = new FakeHostPlatform();
        app.Script.Enqueue(r => "{\"id\":1,\"ok\":true}");
        var relay = new HostRelay(app);
        Assert.Equal("{\"id\":1,\"ok\":true}", relay.Relay("{\"id\":1}"));
        Assert.Equal(["{\"id\":1}"], app.Received);
        Assert.Equal(1, app.Connects);

        // Misma conexion mientras siga viva.
        app.Script.Enqueue(r => "dos");
        Assert.Equal("dos", relay.Relay("b"));
        Assert.Equal(1, app.Connects);

        // La aplicacion cierra (ReadLine null): se reintenta con una conexion nueva.
        app.Script.Enqueue(_ => null);
        app.Script.Enqueue(_ => "tres");
        Assert.Equal("tres", relay.Relay("c"));
        Assert.Equal(2, app.Connects);

        // Se corta la tuberia (IOException) la primera vez: tambien se reintenta.
        app.Script.Enqueue(_ => throw new IOException("rota"));
        app.Script.Enqueue(_ => "cuatro");
        Assert.Equal("cuatro", relay.Relay("d"));

        // Dos veces seguidas sin respuesta: error.
        app.Script.Enqueue(_ => null);
        app.Script.Enqueue(_ => null);
        Assert.Throws<IOException>(() => relay.Relay("e"));
        app.Script.Enqueue(_ => throw new IOException("1"));
        app.Script.Enqueue(_ => throw new IOException("2"));
        Assert.Equal("2", Assert.Throws<IOException>(() => relay.Relay("f")).Message);
        Assert.True(app.Disposed > 0);
    }

    [Fact]
    public void Relay_StartsTheApp_WhenThePipeIsNotThere()
    {
        var app = new FakeHostPlatform { FailConnects = 3, AppPathValue = @"C:\x\sOCCredentials.exe" };
        app.Files.Add(@"C:\x\sOCCredentials.exe");
        app.Script.Enqueue(_ => "hola");
        Assert.Equal("hola", new HostRelay(app).Relay("{}"));
        Assert.Equal([(@"C:\x\sOCCredentials.exe", "--background", @"C:\x")], app.Started);
        Assert.Contains("la aplicacion no responde en la tuberia: se arranca", app.Logs);
        Assert.Equal(2, app.Sleeps);
        Assert.Equal([1500, 1000, 1000, 1000], app.Timeouts);
    }

    [Fact]
    public void Relay_GivesUp_AfterTheStartupWait()
    {
        var app = new FakeHostPlatform { FailConnects = int.MaxValue, AppPathValue = @"C:\x\a.exe", Tick = TimeSpan.FromSeconds(10) };
        app.Files.Add(@"C:\x\a.exe");
        Assert.Throws<TimeoutException>(() => new HostRelay(app) { StartupWait = TimeSpan.FromSeconds(25) }.Relay("{}"));
        Assert.Single(app.Started);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(@"C:\no\esta.exe")]
    public void Relay_WithoutTheApp_IsNoApp(string? appPath)
    {
        var app = new FakeHostPlatform { FailConnects = int.MaxValue, AppPathValue = appPath };
        var input = new MemoryStream(Frame("{\"id\":9,\"type\":\"list\"}"));
        var output = new MemoryStream();
        Assert.Equal(0, new HostRelay(app).Run(input, output));
        var reply = Assert.Single(Frames(output.ToArray()));
        Assert.StartsWith("{\"id\":9,\"error\":\"noapp\",\"detail\":\"sOC Credentials no esta instalada", reply);
        Assert.Empty(app.Started);
        Assert.Contains("el navegador cerro el puerto", app.Logs);
    }

    [Fact]
    public void Run_RelaysEveryFrame_AndReportsAppErrors()
    {
        var app = new FakeHostPlatform();
        app.Script.Enqueue(r => r.Replace("pide", "contesta"));
        app.Script.Enqueue(_ => throw new InvalidOperationException("se ha roto"));
        app.Script.Enqueue(r => r.Replace("pide", "contesta"));
        var input = new MemoryStream([.. Frame("{\"id\":1,\"pide\":1}"), .. Frame("{\"id\":2}"), .. Frame("{\"id\":3,\"pide\":3}")]);
        var output = new MemoryStream();
        Assert.Equal(0, new HostRelay(app).Run(input, output));
        Assert.Equal(["{\"id\":1,\"contesta\":1}", "{\"id\":2,\"error\":\"app\",\"detail\":\"se ha roto\"}", "{\"id\":3,\"contesta\":3}"], Frames(output.ToArray()));
        Assert.Contains("error: se ha roto", app.Logs);
    }

    [Fact]
    public void StreamConnection_ReadsAndWritesLines()
    {
        var duplex = new DuplexStream("respuesta 1\nrespuesta 2\n");
        var connected = true;
        using (var c = new StreamConnection(duplex, () => connected))
        {
            Assert.True(c.IsConnected);
            c.WriteLine("pregunta ñ");
            Assert.Equal("respuesta 1", c.ReadLine());
            Assert.Equal("respuesta 2", c.ReadLine());
            Assert.Null(c.ReadLine());
            connected = false;
            Assert.False(c.IsConnected);
        }
        Assert.Equal("pregunta ñ" + Environment.NewLine, duplex.Written);
        Assert.True(duplex.IsDisposed);
    }

    [Fact]
    public void HostLog_AppendsAndTrims()
    {
        var dir = Path.Combine(Path.GetTempPath(), "soccred-host-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTime(2026, 10, 2, 9, 30, 15);
            HostLog.Append(dir, "arranca", now, 1234);
            HostLog.Append(dir, "otra", now, 1234);
            var file = Path.Combine(dir, "host.log");
            Assert.Equal($"2026-10-02 09:30:15 [1234] arranca{Environment.NewLine}2026-10-02 09:30:15 [1234] otra{Environment.NewLine}", File.ReadAllText(file));

            File.WriteAllText(file, new string('x', (int)HostLog.MaxBytes + 1));
            HostLog.Append(dir, "nueva", now, 1);
            Assert.Equal($"2026-10-02 09:30:15 [1] nueva{Environment.NewLine}", File.ReadAllText(file));

            // Si no se puede escribir, no pasa nada.
            using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
                HostLog.Append(dir, "bloqueado", now, 1);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ------------------------------------------------------------------ lanzador

    private static Stream Zip(params (string Name, string Content)[] files)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in files)
                using (var w = new StreamWriter(zip.CreateEntry(name).Open()))
                    w.Write(content);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Launcher_UnpacksOnce_StartsWithArgs_AndDropsOldVersions()
    {
        var root = Path.Combine(Path.GetTempPath(), "soccred-launcher-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "2026.1.1.0"));
            Directory.CreateDirectory(Path.Combine(root, "2026.10.2.0.nuevo"));   // un desempaquetado a medias
            var host = new FakeLauncherHost(root, "2026.10.2.0\r\n", () => Zip(("Credentials.exe", "exe"), ("sub/a.dll", "dll")));

            Assert.Equal(0, LauncherCore.Run(host, ["--tray", "con espacio"]));
            var target = Path.Combine(root, "2026.10.2.0");
            Assert.Equal("exe", File.ReadAllText(Path.Combine(target, "Credentials.exe")));
            Assert.True(File.Exists(Path.Combine(target, "sub", "a.dll")));
            Assert.True(File.Exists(Path.Combine(target, LauncherCore.CompleteMark)));
            Assert.Equal([target], Directory.GetDirectories(root));
            var info = Assert.Single(host.Started);
            Assert.Equal(Path.Combine(target, "Credentials.exe"), info.FileName);
            Assert.Equal(target, info.WorkingDirectory);
            Assert.False(info.UseShellExecute);
            Assert.Equal(["--tray", "con espacio"], info.ArgumentList);
            Assert.Equal(@"C:\lanzador\sOCCredentials.exe", info.Environment["SOC_LAUNCHER"]);

            // Ya desempaquetada: no se vuelve a abrir el zip.
            host.Zips = 0;
            Assert.Equal(0, LauncherCore.Run(host, []));
            Assert.Equal(0, host.Zips);

            // Sin la marca de completa (se corto a medias): se desempaqueta otra vez.
            File.Delete(Path.Combine(target, LauncherCore.CompleteMark));
            Assert.True(LauncherCore.NeedsUnpack(target));
            Assert.Equal(0, LauncherCore.Run(host, []));
            Assert.Equal(1, host.Zips);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Launcher_ReportsMissingResources()
    {
        var root = Path.Combine(Path.GetTempPath(), "soccred-launcher-" + Guid.NewGuid().ToString("N"));
        try
        {
            var noVersion = new FakeLauncherHost(root, null, () => Zip());
            Assert.Equal(1, LauncherCore.Run(noVersion, []));
            Assert.Equal("No se ha podido arrancar sOC Credentials:\nFalta version.txt dentro del exe.", Assert.Single(noVersion.Errors));

            var noZip = new FakeLauncherHost(root, "1.0", () => null);
            Assert.Equal(1, LauncherCore.Run(noZip, []));
            Assert.EndsWith("Falta app.zip dentro del exe.", Assert.Single(noZip.Errors));
            Assert.Empty(noZip.Started);

            var info = LauncherCore.StartInfo(@"C:\app", null, []);
            Assert.Equal(string.Empty, info.Environment["SOC_LAUNCHER"]);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Launcher_KeepsOldVersionsInUse()
    {
        var root = Path.Combine(Path.GetTempPath(), "soccred-launcher-" + Guid.NewGuid().ToString("N"));
        try
        {
            var old = Path.Combine(root, "0.9");
            Directory.CreateDirectory(old);
            using (new FileStream(Path.Combine(old, "en-uso.dll"), FileMode.Create, FileAccess.Write, FileShare.None))
                LauncherCore.Unpack(() => Zip(("Credentials.exe", "x")), root, Path.Combine(root, "1.0"), DateTime.Now);
            Assert.True(Directory.Exists(old));
        }
        finally { Directory.Delete(root, true); }
    }

    // ------------------------------------------------------------------ dobles

    private sealed class FakeHostPlatform : IHostPlatform
    {
        private DateTime _now = new(2026, 10, 2);
        public Queue<Func<string, string?>> Script { get; } = new();
        public List<string> Received { get; } = [];
        public List<string> Logs { get; } = [];
        public List<(string, string, string?)> Started { get; } = [];
        public HashSet<string> Files { get; } = [];
        public List<int> Timeouts { get; } = [];
        public int FailConnects { get; set; }
        public string? AppPathValue { get; set; }
        public TimeSpan Tick { get; set; } = TimeSpan.FromSeconds(1);
        public int Connects, Sleeps, Disposed;

        public IHostConnection Connect(int timeoutMs)
        {
            Timeouts.Add(timeoutMs);
            if (FailConnects-- > 0)
                throw new TimeoutException();
            Connects++;
            return new Connection(this);
        }

        public string? AppPath() => AppPathValue;
        public bool FileExists(string path) => Files.Contains(path);
        public void Start(string path, string arguments, string? workingDirectory) => Started.Add((path, arguments, workingDirectory));
        public DateTime UtcNow => _now += Tick;
        public void Sleep(int milliseconds) => Sleeps++;
        public void Log(string line) => Logs.Add(line);

        private sealed class Connection(FakeHostPlatform app) : IHostConnection
        {
            private string? _last;
            private bool _closed;
            public bool IsConnected => !_closed;

            public void WriteLine(string line)
            {
                app.Received.Add(line);
                _last = line;
            }

            public string? ReadLine()
            {
                var answer = app.Script.Dequeue()(_last!);
                if (answer is null)
                    _closed = true;
                return answer;
            }

            public void Dispose() => app.Disposed++;
        }
    }

    private sealed class FakeLauncherHost(string root, string? version, Func<Stream?> zip) : ILauncherHost
    {
        public int Zips;
        public List<ProcessStartInfo> Started { get; } = [];
        public List<string> Errors { get; } = [];
        public string AppRoot => root;
        public string? ProcessPath => @"C:\lanzador\sOCCredentials.exe";
        public void Start(ProcessStartInfo info) => Started.Add(info);
        public void ShowError(string message) => Errors.Add(message);

        public Stream? Resource(string name)
        {
            if (name == "version.txt")
                return version is null ? null : new MemoryStream(Encoding.UTF8.GetBytes(version));
            Zips++;
            return zip();
        }
    }
}

/// <summary>Un flujo que entrega los bytes de dos en dos (como una tuberia lenta).</summary>
internal sealed class TrickleStream(byte[] data) : MemoryStream(data)
{
    public int Reads;

    public override int Read(byte[] buffer, int offset, int count)
    {
        Reads++;
        return base.Read(buffer, offset, Math.Min(2, count));
    }
}

/// <summary>Un flujo de ida y vuelta en memoria: se lee lo preparado y se apunta lo escrito.</summary>
internal sealed class DuplexStream(string input) : Stream
{
    private readonly MemoryStream _in = new(Encoding.UTF8.GetBytes(input));
    private readonly MemoryStream _out = new();
    public bool IsDisposed;
    public string Written => Encoding.UTF8.GetString(_out.ToArray());
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => _in.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => _out.Write(buffer, offset, count);

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}
