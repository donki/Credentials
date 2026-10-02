using System.IO.Pipes;
using Credentials.Host;
using Credentials.Platforms.Windows;

namespace Credentials.Tests;

/// <summary>
/// Las piezas con objetos con nombre del sistema (tuberia, mutex, evento) de verdad, pero con nombres
/// propios de cada prueba: nada que ver con los de la aplicacion abierta.
/// </summary>
public class PipeAndInstanceTests
{
    private static string Name(string what) => "soccred-test-" + what + "-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Host_TalksToTheApp_OverARealPipe()
    {
        using var box = Sandbox.Create();
        var pipe = Name("pipe");
        var handler = new ExtensionRequestHandler(box.Store, box.Settings, work => work(), () => { });
        using var cts = new CancellationTokenSource();
        var server = new PipeServerLoop(() => new NamedPipeServerStream(pipe, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous), handler);
        var running = server.RunAsync(cts.Token);

        // Dos conexiones a la vez, como dos navegadores.
        using (var first = PipeConnector.Connect(pipe, 5000))
        using (var second = PipeConnector.Connect(pipe, 5000))
        {
            Assert.True(first.IsConnected);
            first.WriteLine("{\"id\":1,\"type\":\"show\"}");
            second.WriteLine("{\"id\":2,\"type\":\"hello\",\"browser\":\"edge\"}");
            Assert.Equal("{\"id\":1,\"ok\":true}", first.ReadLine());
            Assert.Equal("{\"id\":2,\"ok\":true,\"locked\":true}", second.ReadLine());
            first.WriteLine("no es json");
            Assert.Equal("{\"error\":\"badjson\"}", first.ReadLine());
        }

        // Y el host entero (marcos del navegador) contra la misma tuberia.
        var relay = new HostRelay(new PipePlatform(pipe));
        Assert.Equal("{\"id\":3,\"error\":\"unknown\"}", relay.Relay("{\"id\":3,\"type\":\"nada\"}"));

        cts.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Connect_WithoutServer_TimesOut()
    {
        Assert.Throws<TimeoutException>(() => PipeConnector.Connect(Name("nadie"), 50));
    }

    [Fact]
    public async Task ServerLoop_RetriesWhenThePipeCannotBeCreated_AndStopsOnCancel()
    {
        using var box = Sandbox.Create();
        var handler = new ExtensionRequestHandler(box.Store, box.Settings, work => work(), () => { });
        var attempts = 0;
        using var cts = new CancellationTokenSource();
        var loop = new PipeServerLoop(() =>
        {
            if (Interlocked.Increment(ref attempts) >= 3)
                cts.Cancel();
            throw new IOException("la tiene otra instancia");
        }, handler) { RetryDelay = TimeSpan.FromMilliseconds(5) };
        await loop.RunAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, attempts);

        // Cancelado ya: ni lo intenta.
        await new PipeServerLoop(() => throw new InvalidOperationException(), handler).RunAsync(new CancellationToken(true));

        // Cancelado durante la espera larga entre intentos.
        using var late = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await new PipeServerLoop(() => throw new IOException(), handler) { RetryDelay = TimeSpan.FromMinutes(5) }.RunAsync(late.Token).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class PipePlatform(string pipe) : IHostPlatform
    {
        public IHostConnection Connect(int timeoutMs) => PipeConnector.Connect(pipe, timeoutMs);
        public string? AppPath() => null;
        public bool FileExists(string path) => false;
        public void Start(string path, string arguments, string? workingDirectory) => throw new InvalidOperationException();
        public DateTime UtcNow => DateTime.UtcNow;
        public void Sleep(int milliseconds) { }
        public void Log(string line) { }
    }

    // ------------------------------------------------------------------ una sola instancia

    private static SystemInstances Instances(string mutex, string shown, List<string>? calls = null, string process = "soccred-no-existe", string? path = null) =>
        new(mutex, shown, process, path, () => calls?.Add("allow"), () => calls?.Add("broadcast"));

    [Fact]
    public void Mutex_FirstClaims_SecondDoesNot_ThenTakesOver()
    {
        var mutex = Name("mutex");
        var shown = Name("shown");
        // Cada «instancia» en su hilo, como dos procesos (el mutex es de quien lo crea).
        SystemInstances? first = null;
        var gotIt = false;
        var release = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            first = Instances(mutex, shown);
            gotIt = first.TryClaimMutex();
            release.Wait();
            first.Mutex!.ReleaseMutex();
            first.Mutex.Dispose();
        });
        t.Start();
        Assert.True(SpinWait.SpinUntil(() => first?.Mutex is not null, TimeSpan.FromSeconds(10)));
        Assert.True(gotIt);

        var calls = new List<string>();
        var second = Instances(mutex, shown, calls);
        Assert.False(second.TryClaimMutex());
        Assert.Null(second.Mutex);
        second.TakeOverMutex();
        Assert.NotNull(second.Mutex);
        second.AllowForeground();
        second.BroadcastShow();
        Assert.Equal(["allow", "broadcast"], calls);
        release.Set();
        t.Join();
        second.Mutex!.Dispose();
    }

    [Fact]
    public void ShownSignal_CrossesInstances()
    {
        var shown = Name("shown");
        var waiting = Instances(Name("mutex"), shown);
        using var signal = waiting.OpenShownSignal();
        signal.Reset();
        Assert.False(signal.Wait(TimeSpan.FromMilliseconds(10)));
        SystemInstances.NotifyShown(shown);
        Assert.True(signal.Wait(TimeSpan.FromSeconds(5)));
        // Un nombre imposible no lanza.
        SystemInstances.NotifyShown("Global\\" + new string('x', 300) + "\\\\");
    }

    [Fact]
    public void Processes_AndVersions()
    {
        var none = Instances(Name("m"), Name("s"));
        Assert.Empty(none.OtherInstances());
        Assert.False(none.OtherInstanceAlive());
        Assert.Null(none.MyVersion());

        // Con el nombre de este proceso: nunca sale el propio (solo se miran, no se cierra nada).
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        var mine = Instances(Name("m"), Name("s"), process: me.ProcessName, path: Environment.ProcessPath);
        Assert.NotNull(mine.MyVersion());
        var others = mine.OtherInstances().ToList();
        foreach (var o in others)
        {
            try { _ = o.Version; } catch (Exception) { }
            o.Dispose();
        }
        _ = mine.OtherInstanceAlive();

        Assert.Null(SystemInstances.VersionOf(null));
        Assert.Null(SystemInstances.VersionOf(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe")));
        var text = Path.GetTempFileName();
        try { Assert.Null(SystemInstances.VersionOf(text)); }
        finally { File.Delete(text); }
    }
}
