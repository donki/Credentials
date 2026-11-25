using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using Credentials.Platforms.Windows;
using Credentials.Services;

namespace Credentials.Windows.Tests;

/// <summary>
/// El resto del codigo de Windows: minimizar con el tamaño de vuelta, la identidad de la barra de
/// tareas, la instancia unica, «Arrancar con Windows», la entrada en la nube, los avisos, el gestor de
/// errores de WinUI, DesktopHost, la tuberia de los navegadores y el autocompletar de escritorio. Todo
/// sobre la ventana oculta de prueba, costuras o una tuberia con otro nombre.
/// </summary>
public sealed class WindowsCodeTests : IDisposable
{
    private readonly TestEnv _env = new();

    public void Dispose() => _env.Dispose();

    [Fact]
    public void Minimizar_dejando_el_tamaño_de_vuelta()
    {
        var calls = new List<string>();
        WindowPlacement.WINDOWPLACEMENT written = default;
        var show = WindowPlacement.ShowWindowCall;
        var get = WindowPlacement.GetPlacement;
        var set = WindowPlacement.SetPlacement;
        try
        {
            WindowPlacement.ShowWindowCall = (hwnd, command) => { calls.Add("show:" + command); return true; };
            WindowPlacement.GetPlacement = (IntPtr hwnd, ref WindowPlacement.WINDOWPLACEMENT wp) => { calls.Add("get"); wp.flags = 0x1; return true; };
            WindowPlacement.SetPlacement = (IntPtr hwnd, ref WindowPlacement.WINDOWPLACEMENT wp) => { calls.Add("set"); written = wp; return true; };

            WindowPlacement.MinimizeWithRestoreBounds(_env.Hwnd, new global::Windows.Graphics.RectInt32(100, 50, 800, 600), 0, 40, restoreMaximized: true);
            Assert.Equal(["show:6", "get", "set"], calls);
            Assert.Equal(2, written.showCmd);   // SW_SHOWMINIMIZED
            var (left, top, right, bottom) = PlacementMath.NormalPosition(new ScreenRect(100, 50, 800, 600), 0, 40);
            Assert.Equal((left, top, right, bottom), (written.rcNormalPosition.Left, written.rcNormalPosition.Top, written.rcNormalPosition.Right, written.rcNormalPosition.Bottom));
            Assert.Equal(PlacementMath.Flags(0x1, true), written.flags);

            // Si no se puede leer la colocacion, no se escribe nada.
            calls.Clear();
            WindowPlacement.GetPlacement = (IntPtr hwnd, ref WindowPlacement.WINDOWPLACEMENT wp) => { calls.Add("get"); return false; };
            WindowPlacement.MinimizeWithRestoreBounds(_env.Hwnd, new global::Windows.Graphics.RectInt32(0, 0, 10, 10), 0, 0, false);
            Assert.Equal(["show:6", "get"], calls);
        }
        finally
        {
            WindowPlacement.ShowWindowCall = show;
            WindowPlacement.GetPlacement = get;
            WindowPlacement.SetPlacement = set;
        }
    }

    [Fact]
    public void Identidad_de_la_barra_de_tareas_en_la_ventana_de_prueba()
    {
        // Con lanzador (y sin el): se escriben las propiedades en la ventana oculta; sin ventana, nada.
        var launcher = Path.Combine(_env.Root, "sOCCredentials.exe");
        File.WriteAllText(launcher, "lanzador");
        TaskbarIdentity.Apply(_env.Hwnd, "sOCratic.Pruebas", "sOC Pruebas", launcher);
        TaskbarIdentity.Apply(_env.Hwnd, "sOCratic.Pruebas", "sOC Pruebas", null);
        TaskbarIdentity.Apply(IntPtr.Zero, "sOCratic.Pruebas", "sOC Pruebas", null);
    }

    [Fact]
    public void Instancia_unica_arranque_entrada_y_avisos()
    {
        // El mensaje «enseñate» es el mismo para todas las instancias (sin mandarlo: lo veria la app abierta).
        Assert.NotEqual(0u, SingleInstance.ShowMessage);
        Assert.Equal(SingleInstance.ShowMessage, SingleInstance.ShowMessage);

        Assert.Equal(Environment.ProcessPath, WindowsStartup.ExecutablePath);
        Assert.NotNull(WindowsStartup.Registration);

        // La vuelta de la entrada en la nube: un puerto libre de 127.0.0.1.
        var redirect = new Uri(new OAuthBrowser().RedirectUri("Google", "x"));
        Assert.Equal("127.0.0.1", redirect.Host);
        Assert.Equal("/auth/", redirect.AbsolutePath);
        Assert.True(redirect.Port > 0);

        // El aviso en Windows es una franja de la pagina: se pide en el hilo de la interfaz.
        var shown = new List<string>();
        void OnToast(string text) { lock (shown) shown.Add(text); }
        ToastService.ToastRequested += OnToast;
        try
        {
            WinUIHost.Run(() => { new ToastService().Show("hecho"); return 0; });
            SpinWait.SpinUntil(() => { lock (shown) return shown.Count > 0; }, TimeSpan.FromSeconds(10));
            Assert.Equal(["hecho"], shown);
        }
        finally
        {
            ToastService.ToastRequested -= OnToast;
        }
    }

    [Fact]
    public void Gestor_de_errores_de_WinUI()
    {
        // Instalarlo engancha tambien lo de Windows (aqui sin Application de WinUI).
        CrashReporting.Install();
        // Sin Application de WinUI no hay nada que enganchar; si preguntarlo falla, se apunta y sigue.
        CrashReporting.HookWinUI(() => null);
        CrashReporting.HookWinUI(() => throw new InvalidOperationException("sin WinUI"));
        Assert.True(File.Exists(SocShared.CrashGuard.LogPath));
        Assert.Contains("CrashReporting.Install (Windows)", File.ReadAllText(SocShared.CrashGuard.LogPath));
        Assert.DoesNotContain("sin WinUI", File.ReadAllText(SocShared.CrashGuard.LogPath));   // sin el mensaje
    }

    [Fact]
    public void DesktopHost_sin_ventana_nativa_no_hace_nada()
    {
        var host = new DesktopHost();
        Assert.Equal(Environment.GetCommandLineArgs(), host.CommandLine);
        Assert.False(host.IsPackaged);
        Assert.Same(ExtensionInstaller.Core, host.Extensions);
        Assert.EndsWith("sOCCredentials", host.Extensions.Root);
        Assert.Same(WindowsStartup.Registration, host.Startup);
        Assert.NotNull(host.SystemIdle());

        // Una ventana de MAUI todavia sin la de WinUI debajo.
        var window = new Window();
        Assert.Null(DesktopHost.Native(window));
        Assert.Null(DesktopHost.Native(null));
        Assert.Null(host.CreateTray(window, k => k));
        var ran = false;
        host.Post(window, () => ran = true);
        Assert.False(ran);
        host.ApplyTaskbarIdentity(window);
        host.BringToFront(window);
        host.BringToFront(null);
        Assert.Null(host.FrameOf(window));
        Assert.Null(host.FrameOf(null));
    }

    [Fact]
    public void La_ventana_de_WinUI_vista_desde_DesktopHost()
    {
        // Una ventana de WinUI que nunca se activa (no se ve): su sitio, su escala y su pantalla, y
        // moverla. Minimizar va por las costuras de WindowPlacement (si no, se veria).
        var calls = new List<string>();
        var show = WindowPlacement.ShowWindowCall;
        var get = WindowPlacement.GetPlacement;
        var set = WindowPlacement.SetPlacement;
        WindowPlacement.ShowWindowCall = (_, command) => { calls.Add("show:" + command); return true; };
        WindowPlacement.GetPlacement = (IntPtr _, ref WindowPlacement.WINDOWPLACEMENT _) => { calls.Add("get"); return true; };
        WindowPlacement.SetPlacement = (IntPtr _, ref WindowPlacement.WINDOWPLACEMENT _) => { calls.Add("set"); return true; };
        try
        {
            var (bounds, moved, client, scale, area, maximized) = WinUIHost.Run(() =>
            {
                var native = new Microsoft.UI.Xaml.Window();
                try
                {
                    var frame = new DesktopHost.WinUIFrame(native);
                    var before = frame.Bounds;
                    frame.MoveAndResize(new FrameRect(10, 20, 300, 200));
                    frame.MinimizeWithRestoreBounds(new FrameRect(10, 20, 300, 200), restoreMaximized: false);
                    return (before, frame.Bounds, frame.ClientHeight, frame.Scale, frame.WorkArea, frame.IsMaximized);
                }
                finally
                {
                    native.Close();
                }
            });
            Assert.True(bounds.Width > 0);
            Assert.Equal(new FrameRect(10, 20, 300, 200), moved);
            Assert.InRange(client, 1, 200);
            Assert.Equal(1.0, scale);   // sin contenido, la de MAUI por defecto
            Assert.True(area.Width > 0 && area.Height > 0);
            Assert.False(maximized);
            Assert.Equal(["show:6", "get", "set"], calls);
        }
        finally
        {
            WindowPlacement.ShowWindowCall = show;
            WindowPlacement.GetPlacement = get;
            WindowPlacement.SetPlacement = set;
        }
    }

    [Fact]
    public async Task La_tuberia_de_los_navegadores_contesta_y_pide_la_boveda()
    {
        // La de DesktopHost (con otro nombre, para no cruzarse con la aplicacion abierta) acepta conexiones.
        var pipe = "sOCCredentials.Pruebas." + Guid.NewGuid().ToString("N");
        new DesktopHost { PipeName = pipe }.StartExtensionServer(_env.Store, _env.Settings, () => { }, _ => { });
        using (var probe = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
            await probe.ConnectAsync(10_000);

        // Un servidor con su tuberia, atendiendo en el hilo de la prueba: saludo, «show» y Stop.
        var unlocks = 0;
        var connected = new List<string>();
        var server = new ExtensionServer(_env.Store, _env.Settings, () => Interlocked.Increment(ref unlocks), pipe + ".2", work => work());
        void OnConnected(string b) { lock (connected) connected.Add(b); }
        server.BrowserConnected += OnConnected;
        server.Start();
        using var client = new NamedPipeClientStream(".", pipe + ".2", PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(10_000);
        using var reader = new StreamReader(client, new UTF8Encoding(false), false, 1024, leaveOpen: true);
        await using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

        await writer.WriteLineAsync("{\"id\":1,\"type\":\"hello\",\"browser\":\"edge\"}");
        var hello = JsonNode.Parse((await reader.ReadLineAsync())!)!;
        Assert.Equal(1, (int)hello["id"]!);
        Assert.True((bool)hello["locked"]!);
        Assert.Equal(["edge"], connected);

        // «show» con la boveda cerrada: trae la ventana y pide desbloquear.
        await writer.WriteLineAsync("{\"id\":2,\"type\":\"show\"}");
        Assert.Equal(2, (int)JsonNode.Parse((await reader.ReadLineAsync())!)!["id"]!);
        Assert.Equal(1, unlocks);
        server.BrowserConnected -= OnConnected;
        server.Stop();
    }

    [Fact]
    public async Task Autocompletar_de_escritorio_sobre_la_ventana_de_prueba()
    {
        // Sin enganchar los avisos del foco (Start): con ellos rellenaria en las aplicaciones del PC.
        using (new DesktopAutofill(_env.Store, _env.Settings, _env.Texts))
        {
        }

        var native = new DesktopAutofill.Native(WinUIHost.Queue);
        IDesktopNative n = native;
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        Assert.Equal(me.ProcessName, native.ProcessName(_env.Hwnd));
        _ = native.ProcessName(new IntPtr(1));   // una ventana que no existe: no lanza
        Assert.Equal("sOC prueba", native.WindowTitle(_env.Hwnd));
        Assert.NotEqual(0u, native.ThreadOf(_env.Hwnd));
        Assert.Equal(native.ThreadOf(_env.Hwnd), native.CurrentThread);
        _ = n.GetFocus();
        n.SetFocus(IntPtr.Zero);
        Assert.False(n.AttachThreadInput(native.CurrentThread, native.CurrentThread, false));
        native.Sleep(0);

        // La accesibilidad de la ventana de prueba (la de su area cliente) y de vuelta su ventana.
        var element = native.FromEvent(_env.Hwnd, -4 /* OBJID_CLIENT */, 0);
        Assert.NotNull(element);
        Assert.Equal(_env.Hwnd, native.WindowOf(element!));
        Assert.Null(native.FromEvent(IntPtr.Zero, -4, 0));

        // Lo que se manda a la interfaz llega a su hilo.
        var done = new TaskCompletionSource<int>();
        Assert.True(native.PostToUi(() => done.SetResult(Environment.CurrentManagedThreadId)));
        Assert.NotEqual(Environment.CurrentManagedThreadId, await done.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
