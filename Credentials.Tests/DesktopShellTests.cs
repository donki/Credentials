using System.Net;
using System.Text;
using Credentials.Platforms.Windows;
using static Credentials.Platforms.Windows.TrayController;

namespace Credentials.Tests;

/// <summary>Las piezas pequeñas del escritorio: bandeja, una sola instancia, entrada por 127.0.0.1, barra de tareas y colocacion.</summary>
public class DesktopShellTests
{
    // ------------------------------------------------------------------ bandeja

    private sealed class FakeTray(uint showMessage, Action exit) : TrayController(showMessage, exit)
    {
        public List<string> Calls { get; } = [];
        protected override void AddIcon() => Calls.Add("add");
        protected override void RemoveIcon() => Calls.Add("remove");
        protected override void ShowWindow(int command) => Calls.Add("show " + command);
        protected override void BringToForeground() => Calls.Add("front");
        protected override void ShowMenu() => Calls.Add("menu");
        protected override void NotifyShown() => Calls.Add("notify");
    }

    [Fact]
    public void Tray_MinimizeHides_ClickRestores()
    {
        var exits = 0;
        var tray = new FakeTray(0xC123, () => exits++);
        Assert.False(tray.IsHidden);

        Assert.True(tray.Handle(WmSysCommand, ScMinimize | 0x2, 0));
        Assert.True(tray.IsHidden);
        Assert.Equal(["add", "show 0"], tray.Calls);

        // Clic izquierdo o doble clic en el icono: vuelve y se quita el icono.
        tray.Calls.Clear();
        Assert.True(tray.Handle(WmTray, 0, WmLButtonUp));
        Assert.False(tray.IsHidden);
        Assert.Equal(["show 9", "front", "remove"], tray.Calls);
        tray.HideToTray();
        tray.Calls.Clear();
        Assert.True(tray.Handle(WmTray, 0, 0x10000 | WmLButtonDblClk));
        Assert.Equal(["show 9", "front", "remove"], tray.Calls);

        // Boton derecho: el menu. Otros movimientos del raton: nada, pero son suyos.
        tray.Calls.Clear();
        Assert.True(tray.Handle(WmTray, 0, WmRButtonUp));
        Assert.True(tray.Handle(WmTray, 0, 0x0200));
        Assert.Equal(["menu"], tray.Calls);

        // «Abrir» y «Salir» del menu.
        tray.HideToTray();
        tray.Calls.Clear();
        Assert.True(tray.Handle(WmCommand, IdOpen, 0));
        Assert.Equal(["show 9", "front", "remove"], tray.Calls);
        tray.HideToTray();
        tray.Calls.Clear();
        Assert.True(tray.Handle(WmCommand, IdExit, 0));
        Assert.Equal(["remove"], tray.Calls);
        Assert.Equal(1, exits);
        Assert.False(tray.IsHidden);
        Assert.False(tray.Handle(WmCommand, 99, 0));
    }

    [Fact]
    public void Tray_MinimizeAsUsual_WhenTheUserSaysSo()
    {
        var tray = new FakeTray(0xC123, () => { }) { MinimizeToTray = false };
        Assert.False(tray.Handle(WmSysCommand, ScMinimize, 0));
        Assert.False(tray.Handle(WmSysCommand, 0xF120 /* SC_RESTORE */, 0));
        Assert.Empty(tray.Calls);

        // Esconder dos veces pone el icono una sola vez; enseñar sin estar escondida no lo quita.
        tray.Show();
        tray.HideToTray();
        tray.HideToTray();
        Assert.Equal(["show 9", "front", "add", "show 0", "show 0"], tray.Calls);
    }

    [Fact]
    public void Tray_Session_AndOtherInstance()
    {
        var tray = new FakeTray(0xC123, () => { });
        var events = new List<string>();
        tray.SessionLocked += () => events.Add("lock");
        tray.SessionUnlocked += () => events.Add("unlock");

        // Los de sesion se avisan y siguen al procedimiento de antes.
        Assert.False(tray.Handle(WmWtsSessionChange, WtsSessionLock, 0));
        Assert.False(tray.Handle(WmWtsSessionChange, WtsSessionUnlock, 0));
        Assert.False(tray.Handle(WmWtsSessionChange, 0x5, 0));
        Assert.Equal(["lock", "unlock"], events);

        // «Enseñate» de otra instancia: se enseña y lo confirma.
        Assert.True(tray.Handle(0xC123, 0, 0));
        Assert.Equal(["show 9", "front", "notify"], tray.Calls);
        Assert.False(tray.Handle(0x000F /* WM_PAINT */, 0, 0));

        var quiet = new FakeTray(1, () => { });
        Assert.False(quiet.Handle(WmWtsSessionChange, WtsSessionLock, 0));
    }

    [Fact]
    public void IdleTime_SurvivesTheTickWrap()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), IdleTime(15_000, 10_000));
        Assert.Equal(TimeSpan.FromMilliseconds(20), IdleTime(10, uint.MaxValue - 9));
        Assert.Equal(TimeSpan.Zero, IdleTime(7, 7));
    }

    // ------------------------------------------------------------------ una sola instancia

    private sealed class FakeInstances : ISingleInstanceNative
    {
        public Func<bool> Claim { get; set; } = () => true;
        public Queue<bool> Answers { get; } = new();
        public Func<bool> Alive { get; set; } = () => true;
        public Version? Mine { get; set; } = new(2, 0);
        public List<FakeOther> Others { get; } = [];
        public List<string> Calls { get; } = [];
        public bool SignalFails, TakeOverFails, OthersFail;

        public bool TryClaimMutex() => Claim();

        public void TakeOverMutex()
        {
            Calls.Add("takeover");
            if (TakeOverFails) throw new UnauthorizedAccessException();
        }

        public IShownSignal OpenShownSignal() => SignalFails ? throw new UnauthorizedAccessException() : new Signal(this);
        public void AllowForeground() => Calls.Add("allow");
        public void BroadcastShow() => Calls.Add("broadcast");
        public bool OtherInstanceAlive() => Alive();
        public Version? MyVersion() => Mine;
        public IEnumerable<IOtherInstance> OtherInstances() => OthersFail ? throw new InvalidOperationException() : Others;

        private sealed class Signal(FakeInstances owner) : IShownSignal
        {
            public void Reset() => owner.Calls.Add("reset");
            public bool Wait(TimeSpan timeout) => owner.Answers.Count > 0 && owner.Answers.Dequeue();
            public void Dispose() => owner.Calls.Add("dispose");
        }
    }

    private sealed class FakeOther(Version? version, bool unreadable = false) : IOtherInstance
    {
        public bool Killed, Waited, Disposed;
        public Version? Version => unreadable ? throw new System.ComponentModel.Win32Exception() : version;
        public void Kill() => Killed = true;
        public void WaitForExit(int milliseconds) => Waited = milliseconds == 5000;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void SingleInstance_FirstOneRuns()
    {
        var native = new FakeInstances();
        Assert.True(new SingleInstanceCore(native).Claim());
        Assert.Empty(native.Calls);

        // Sin poder crear el mutex se arranca igual (mejor dos ventanas que ninguna).
        native.Claim = () => throw new UnauthorizedAccessException();
        Assert.True(new SingleInstanceCore(native).Claim());
    }

    [Fact]
    public void SingleInstance_TheOtherAnswers_ThisOneLeaves()
    {
        var native = new FakeInstances { Claim = () => false };
        native.Answers.Enqueue(false);
        native.Answers.Enqueue(true);
        Assert.False(new SingleInstanceCore(native) { WaitPerAttempt = TimeSpan.Zero }.Claim());
        Assert.Equal(["reset", "allow", "broadcast", "broadcast", "dispose"], native.Calls);
    }

    [Fact]
    public void SingleInstance_NoAnswer_RunsAnyway()
    {
        // La otra se ha ido: se deja de insistir.
        var gone = new FakeInstances { Claim = () => false, Alive = () => false };
        Assert.True(new SingleInstanceCore(gone).Claim());
        Assert.Equal(["reset", "allow", "broadcast", "dispose", "takeover"], gone.Calls);

        // Viva pero colgada: se insiste las veces que toca y se arranca.
        var hung = new FakeInstances { Claim = () => false };
        Assert.True(new SingleInstanceCore(hung) { Attempts = 3, WaitPerAttempt = TimeSpan.Zero }.Claim());
        Assert.Equal(3, hung.Calls.Count(c => c == "broadcast"));

        // Ni el aviso ni el mutex se dejan: se arranca igual.
        var broken = new FakeInstances { Claim = () => false, SignalFails = true, TakeOverFails = true };
        Assert.True(new SingleInstanceCore(broken).Claim());
        Assert.Equal(["takeover"], broken.Calls);
    }

    [Fact]
    public void SingleInstance_ClosesOlderVersionsOnly()
    {
        var older = new FakeOther(new Version(1, 9));
        var same = new FakeOther(new Version(2, 0));
        var newer = new FakeOther(new Version(3, 0));
        var unknown = new FakeOther(null);
        var foreign = new FakeOther(null, unreadable: true);
        var native = new FakeInstances();
        native.Others.AddRange([older, same, newer, unknown, foreign]);

        new SingleInstanceCore(native).CloseOlderVersions();
        Assert.True(older.Killed && older.Waited);
        Assert.False(same.Killed || newer.Killed || unknown.Killed || foreign.Killed);
        Assert.All(native.Others, o => Assert.True(o.Disposed));

        // Sin version propia no se toca nada; y si listar falla, tampoco pasa nada.
        var again = new FakeOther(new Version(1, 0));
        new SingleInstanceCore(new FakeInstances { Mine = null, Others = { again } }).CloseOlderVersions();
        Assert.False(again.Killed);
        new SingleInstanceCore(new FakeInstances { OthersFail = true }).CloseOlderVersions();

        Assert.Equal(new Version(2026, 10, 1, 0), SingleInstanceCore.ParseVersion("2026.10.1.0"));
        Assert.Null(SingleInstanceCore.ParseVersion(null));
        Assert.Null(SingleInstanceCore.ParseVersion("no"));
    }

    // ------------------------------------------------------------------ entrar con el navegador

    private sealed class FakeListener : ILoopbackListener
    {
        public Queue<Func<ILoopbackRequest>> Requests { get; } = new();
        public List<string> Calls { get; } = [];
        private readonly TaskCompletionSource<ILoopbackRequest> _never = new();

        public void Start() => Calls.Add("start");

        public Task<ILoopbackRequest> NextAsync()
        {
            if (Requests.Count == 0)
                return _never.Task;
            try { return Task.FromResult(Requests.Dequeue()()); }
            catch (Exception ex) { return Task.FromException<ILoopbackRequest>(ex); }
        }

        public void Abort()
        {
            Calls.Add("abort");
            _never.TrySetException(new HttpListenerException(995));
        }

        public void Stop() => Calls.Add("stop");
        public void Dispose() => Calls.Add("dispose");
    }

    private sealed class FakeRequest(string? url, bool error = false, bool responseFails = false) : ILoopbackRequest
    {
        public Uri? Url => url is null ? null : new Uri(url);
        public bool HasError => error;
        public int? Status;
        public string? Html;

        public void Respond(int statusCode) => Status = statusCode;

        public Task RespondHtmlAsync(byte[] body)
        {
            if (responseFails) throw new HttpListenerException(64);
            Html = Encoding.UTF8.GetString(body);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Loopback_IgnoresOtherPaths_AndReturnsTheCallback()
    {
        var callback = new Uri("http://127.0.0.1:5123/auth/");
        var listener = new FakeListener();
        var favicon = new FakeRequest("http://127.0.0.1:5123/favicon.ico");
        var back = new FakeRequest("http://127.0.0.1:5123/AUTH/?code=abc&state=xyz");
        listener.Requests.Enqueue(() => favicon);
        listener.Requests.Enqueue(() => back);
        var opened = 0;

        var url = await LoopbackOAuth.AuthenticateAsync(listener, callback, () => opened++, CancellationToken.None);
        Assert.Equal("?code=abc&state=xyz", url.Query);
        Assert.Equal(1, opened);
        Assert.Equal(404, favicon.Status);
        Assert.Equal(LoopbackOAuth.Page(true), back.Html);
        Assert.Contains("Ya puedes volver", back.Html);
        Assert.Equal(["start", "stop"], listener.Calls);
    }

    [Fact]
    public async Task Loopback_ErrorPage_And_BrokenResponse()
    {
        var callback = new Uri("http://127.0.0.1:5123/auth/");
        var refused = new FakeRequest("http://127.0.0.1:5123/auth/?error=access_denied", error: true);
        var listener = new FakeListener();
        listener.Requests.Enqueue(() => refused);
        await LoopbackOAuth.AuthenticateAsync(listener, callback, () => { }, CancellationToken.None);
        Assert.Contains("no se ha completado", refused.Html);

        // El navegador cierra antes de la pagina de cortesia: da igual. Sin URL: la de vuelta.
        var rude = new FakeListener();
        rude.Requests.Enqueue(() => new FakeRequest(null, responseFails: true));
        Assert.Equal(callback, await LoopbackOAuth.AuthenticateAsync(rude, callback, () => { }, CancellationToken.None));
    }

    [Fact]
    public async Task Loopback_Cancelled_Or_Failing()
    {
        var callback = new Uri("http://127.0.0.1:5123/auth/");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var listener = new FakeListener();
        await Assert.ThrowsAsync<OperationCanceledException>(() => LoopbackOAuth.AuthenticateAsync(listener, callback, () => { }, cts.Token));
        Assert.Contains("abort", listener.Calls);

        // Un fallo sin cancelar se deja pasar tal cual.
        var broken = new FakeListener();
        broken.Requests.Enqueue(() => throw new HttpListenerException(5));
        await Assert.ThrowsAsync<HttpListenerException>(() => LoopbackOAuth.AuthenticateAsync(broken, callback, () => { }, CancellationToken.None));
    }

    // ------------------------------------------------------------------ barra de tareas y colocacion

    [Fact]
    public void Taskbar_RelaunchOnlyWithTheLauncher()
    {
        Assert.Equal([(TaskbarProperties.Id, "sOCratic.sOCCredentials")], TaskbarProperties.For("sOCratic.sOCCredentials", "sOC Credentials", null, _ => true));
        Assert.Single(TaskbarProperties.For("id", "n", @"C:\no.exe", _ => false));
        Assert.Equal(
        [
            (TaskbarProperties.Id, "id"),
            (TaskbarProperties.RelaunchCommand, "\"C:\\sOC\\sOCCredentials.exe\""),
            (TaskbarProperties.RelaunchDisplayNameResource, "sOC Credentials"),
            (TaskbarProperties.RelaunchIconResource, "C:\\sOC\\sOCCredentials.exe,0"),
        ], TaskbarProperties.For("id", "sOC Credentials", @"C:\sOC\sOCCredentials.exe", _ => true));
    }

    [Fact]
    public void Placement_InWorkAreaCoordinates()
    {
        Assert.Equal((90, 160, 890, 760), PlacementMath.NormalPosition(new ScreenRect(100, 200, 800, 600), 10, 40));
        Assert.Equal((-1920, 0, -640, 1000), PlacementMath.NormalPosition(new ScreenRect(-1920, 0, 1280, 1000), 0, 0));
        Assert.Equal(0x3u, PlacementMath.Flags(0x1, true));
        Assert.Equal(0x1u, PlacementMath.Flags(0x3, false));
        Assert.Equal(0x2u, PlacementMath.Flags(0x2, true));
        var r = new ScreenRect(1, 2, 3, 4);
        Assert.Equal((4, 6), (r.Right, r.Bottom));
    }
}
