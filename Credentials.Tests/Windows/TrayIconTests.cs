using Credentials.Platforms.Windows;
using Microsoft.UI.Xaml.Automation.Peers;
using Xaml = Microsoft.UI.Xaml;

namespace Credentials.Windows.Tests;

/// <summary>
/// El icono de bandeja de verdad (TrayIcon) sobre una ventana oculta de prueba: el procedimiento de
/// ventana que caza los mensajes, el icono (sin ponerlo en el area de notificacion: lo apunta la
/// costura), y el menu de WinUI construido y pulsado sin enseñarse.
/// </summary>
public sealed class TrayIconTests : IDisposable
{
    private readonly TestEnv _env = new();
    private readonly List<string> _calls = [];
    private readonly Func<int, TrayIcon.NotifyIconData, bool> _notify = TrayIcon.NotifyIcon;
    private readonly Func<IntPtr, int, bool> _show = TrayIcon.ShowWindowCall;
    private readonly Func<IntPtr, bool> _front = TrayIcon.SetForegroundCall;
    private int _exits;
    private readonly List<TrayIcon> _trays = [];   // vivos hasta cerrar la ventana: su procedimiento la atiende

    public TrayIconTests()
    {
        TrayIcon.NotifyIcon = (message, data) =>
        {
            Assert.Equal(_env.Hwnd, data.hWnd);
            Assert.Equal(1, data.uID);
            _calls.Add(message == 0 ? $"add:{data.uFlags}:{data.uCallbackMessage}:{data.szTip}:{data.hIcon != IntPtr.Zero}" : "delete:" + message);
            return true;
        };
        TrayIcon.ShowWindowCall = (hwnd, command) =>
        {
            Assert.Equal(_env.Hwnd, hwnd);
            _calls.Add("show:" + command);
            return true;
        };
        TrayIcon.SetForegroundCall = hwnd =>
        {
            Assert.Equal(_env.Hwnd, hwnd);
            _calls.Add("front");
            return true;
        };
    }

    public void Dispose()
    {
        TrayIcon.NotifyIcon = _notify;
        TrayIcon.ShowWindowCall = _show;
        TrayIcon.SetForegroundCall = _front;
        _env.Dispose();
        GC.KeepAlive(_trays);
    }

    private TrayIcon NewTray()
    {
        var tray = new TrayIcon(_env.Hwnd, key => "«" + key + "»", () => _exits++);
        _trays.Add(tray);
        return tray;
    }

    [Fact]
    public void Los_mensajes_de_la_ventana_pasan_por_la_bandeja()
    {
        var tray = NewTray();

        // Minimizar: a la bandeja (icono con su mensaje, el icono del exe y la pista) y la ventana fuera.
        TestEnv.Send(_env.Hwnd, TrayController.WmSysCommand, TrayController.ScMinimize);
        Assert.Equal([$"add:7:{TrayController.WmTray}:sOC Credentials:True", "show:0"], _calls);
        Assert.True(tray.IsHidden);

        // Clic en el icono: vuelve, al frente, y fuera el icono.
        _calls.Clear();
        TestEnv.Send(_env.Hwnd, TrayController.WmTray, 0, TrayController.WmLButtonUp);
        Assert.Equal(["show:9", "front", "delete:2"], _calls);
        Assert.False(tray.IsHidden);

        // Con «minimizar a la bandeja» apagado, minimizar no es cosa suya (se pregunta sin mandarlo:
        // el procedimiento de STATIC minimizaria de verdad la ventana de prueba).
        tray.MinimizeToTray = false;
        Assert.False(tray.Handle(TrayController.WmSysCommand, TrayController.ScMinimize, 0));

        // El bloqueo de la sesion de Windows avisa y deja seguir al procedimiento de antes.
        var locked = 0;
        tray.SessionLocked += () => locked++;
        TestEnv.Send(_env.Hwnd, TrayController.WmWtsSessionChange, TrayController.WtsSessionLock);
        Assert.Equal(1, locked);

        // Esconder y Salir: fuera el icono y la aplicacion se cierra.
        tray.HideToTray();
        _calls.Clear();
        TestEnv.Send(_env.Hwnd, TrayController.WmCommand, TrayController.IdExit);
        Assert.Equal(["delete:2"], _calls);
        Assert.Equal(1, _exits);

        // Un mensaje cualquiera llega a la ventana como siempre.
        Assert.Equal(IntPtr.Zero, TestEnv.Send(_env.Hwnd, 0x0400 /* WM_USER */));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void El_menu_de_WinUI_con_sus_elementos_y_al_pulsar_manda_la_orden(bool dark)
    {
        var tray = NewTray();
        tray.HideToTray();
        _calls.Clear();

        var (texts, separators, open) = WinUIHost.Run(() =>
        {
            var menu = tray.BuildMenu(dark);
            Assert.False(menu.ShouldConstrainToRootBounds);
            Assert.NotNull(menu.MenuFlyoutPresenterStyle);
            var items = menu.Items.OfType<Xaml.Controls.MenuFlyoutItem>().ToList();
            Assert.All(items, i => Assert.IsType<Xaml.Controls.FontIcon>(i.Icon));
            Assert.True(items[0].Resources.ContainsKey("MenuFlyoutItemBackgroundPointerOver"));
            // Pulsar «Abrir» (como el usuario) manda WM_COMMAND a la ventana principal.
            new MenuFlyoutItemAutomationPeer(items[0]).Invoke();
            return (items.Select(i => i.Text).ToList(), menu.Items.OfType<Xaml.Controls.MenuFlyoutSeparator>().Count(), true);
        });
        Assert.Equal(["«TrayOpen»", "«TrayExit»"], texts);
        Assert.Equal(1, separators);
        Assert.True(open);

        // La orden llega por la cola de la ventana: «Abrir» la saca de la bandeja.
        _env.Pump();
        Assert.Equal(["show:9", "front", "delete:2"], _calls);
    }

    [Fact]
    public void Los_colores_del_menu_salen_de_la_aplicacion_si_los_tiene()
    {
        var tray = NewTray();
        var previous = Application.Current;
        try
        {
            Application.Current = new Application();
            Application.Current.Resources["CardBackgroundLight"] = Microsoft.Maui.Graphics.Colors.Red;
            var background = WinUIHost.Run(() =>
            {
                var menu = tray.BuildMenu(false);
                var setter = menu.MenuFlyoutPresenterStyle.Setters.OfType<Xaml.Setter>().First(s => s.Property == Xaml.Controls.Control.BackgroundProperty);
                return ((Xaml.Media.SolidColorBrush)setter.Value).Color;
            });
            Assert.Equal(global::Windows.UI.Color.FromArgb(255, 255, 0, 0), background);
        }
        finally
        {
            Application.Current = previous;
        }
    }

    [Fact]
    public void Inactividad_icono_y_primer_plano_del_sistema()
    {
        Assert.NotNull(TrayIcon.SystemIdle());
        Assert.NotEqual(IntPtr.Zero, TrayIcon.LoadAppIcon());
        // La ventana de delante no es de las pruebas (y preguntarlo no cambia nada).
        _ = TrayIcon.ForegroundIsOurs();
    }
}
