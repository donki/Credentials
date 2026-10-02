using System.Diagnostics;
using System.Runtime.InteropServices;
using Credentials.Models;
using Credentials.Services;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WUX = Microsoft.UI.Xaml;
using WUC = Microsoft.UI.Xaml.Controls;
using WUM = Microsoft.UI.Xaml.Media;

namespace Credentials.Platforms.Windows;

/// <summary>
/// Autocompletar en las aplicaciones de Windows (las webs van por la extension del navegador).
/// Se vigila el foco del escritorio (SetWinEventHook, EVENT_OBJECT_FOCUS): cuando cae en un campo
/// de contraseña de otra aplicacion (por accesibilidad: ROLE_SYSTEM_TEXT + STATE_SYSTEM_PROTECTED),
/// se busca en la boveda que entradas casan con ese programa (nombre del ejecutable y titulo de la
/// ventana) y se enseña una lista pegada al campo. Al elegir una, se escribe el usuario en el campo
/// de texto anterior y la contraseña en el suyo, como si se tecleara (SendInput).
/// </summary>
/// <remarks>
/// La logica esta en DesktopAutofillEngine (PlatformLogic, con pruebas); aqui solo lo que toca
/// Windows: los avisos de foco, MSAA, SendInput y la ventanita.
/// La lista es una ventana que no se activa (WS_EX_NOACTIVATE): el foco se queda en la aplicacion
/// de destino, asi que si el usuario sigue tecleando no pasa nada raro y, al elegir, lo que se
/// «teclea» va directo al campo. Se esconde en cuanto el foco se va a otro sitio. No se lee nada de
/// los campos de otras aplicaciones: solo se escribe cuando el usuario elige.
/// Las entradas aprenden: la que se usa en un programa se queda con un campo «windows» = ejecutable,
/// y a partir de ahi sale la primera. Los navegadores se ignoran (los cubre la extension).
/// </remarks>
public sealed class DesktopAutofill : IDisposable
{
    private readonly DesktopAutofillEngine _engine;
    private readonly WinEventProc _proc;
    private readonly List<IntPtr> _hooks = [];

    public DesktopAutofill(VaultStore store, ISettingsService settings, ILocalizationService l)
    {
        _proc = (_, evt, hwnd, idObject, idChild, _, _) => _engine!.OnWinEvent(evt, hwnd, idObject, idChild);
        _engine = new DesktopAutofillEngine(store, settings, new Native(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()), new Popup(l));
        SetLog(_engine);
    }

    [Conditional("DEBUG")]
    private static void SetLog(DesktopAutofillEngine engine) => engine.Log = text =>
    {
        try { File.AppendAllText(Path.Combine(VaultStore.DataDirectory, "autofill.log"), $"{DateTime.Now:HH:mm:ss.fff} {text}" + Environment.NewLine); } catch (Exception) { }
    };

    /// <summary>Engancha los avisos de foco; hay que llamarlo desde el hilo de la interfaz (los avisos llegan a su bucle de mensajes).</summary>
    public void Start()
    {
        if (_hooks.Count > 0)
            return;
        const uint outOfContext = 0x0000, skipOwnProcess = 0x0002;
        foreach (var evt in new[] { DesktopAutofillEngine.EventObjectFocus, DesktopAutofillEngine.EventSystemForeground, DesktopAutofillEngine.EventSystemMinimizeStart })
            _hooks.Add(SetWinEventHook(evt, evt, IntPtr.Zero, _proc, 0, 0, outOfContext | skipOwnProcess));
        _engine.Start();
    }

    public void Dispose()
    {
        foreach (var h in _hooks)
            UnhookWinEvent(h);
        _hooks.Clear();
        _engine.Dispose();
    }

    /// <summary>Win32 de verdad.</summary>
    private sealed class Native(Microsoft.UI.Dispatching.DispatcherQueue ui) : IDesktopNative
    {
        public string ProcessName(IntPtr hwnd)
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            try { using var process = Process.GetProcessById((int)pid); return process.ProcessName; }
            catch (Exception) { return string.Empty; }
        }

        public string WindowTitle(IntPtr hwnd)
        {
            var top = GetAncestor(hwnd, 2 /* GA_ROOT */);
            var buffer = new System.Text.StringBuilder(512);
            GetWindowText(top == IntPtr.Zero ? hwnd : top, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        public IAccElement? FromEvent(IntPtr hwnd, int idObject, int idChild) =>
            AccessibleObjectFromEvent(hwnd, (uint)idObject, (uint)idChild, out var acc, out var child) == 0 && acc is not null ? new MsaaElement(acc, child, AccessibleChildren) : null;

        public IntPtr WindowOf(IAccElement element) => WindowFromAccessibleObject(((MsaaElement)element).Acc, out var hwnd) == 0 ? hwnd : IntPtr.Zero;
        public uint ThreadOf(IntPtr hwnd) => GetWindowThreadProcessId(hwnd, out _);
        public uint CurrentThread => GetCurrentThreadId();
        bool IDesktopNative.AttachThreadInput(uint attach, uint to, bool on) => AttachThreadInput(attach, to, on);
        void IDesktopNative.SetFocus(IntPtr hwnd) => SetFocus(hwnd);
        IntPtr IDesktopNative.GetFocus() => GetFocus();
        public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);
        public bool PostToUi(Action action) => ui.TryEnqueue(() => action());

        public void SendKeys(IReadOnlyList<(ushort Scan, uint Flags)> keys)
        {
            var inputs = keys.Select(k => new Input { type = 1, u = new InputUnion { ki = new KeybdInput { wScan = k.Scan, dwFlags = k.Flags } } }).ToArray();
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        }
    }

    /// <summary>La ventanita con la lista: sin marco, siempre encima y sin activarse nunca (se crea al enseñarla la primera vez).</summary>
    private sealed class Popup(ILocalizationService l) : IAutofillPopup
    {
        private WUX.Window? _window;
        private WUC.StackPanel? _panel;
        private IntPtr _hwnd;
        private bool _visible;

        private void Create()
        {
            _window = new WUX.Window();
            _panel = new WUC.StackPanel { Padding = new WUX.Thickness(6), Spacing = 2 };
            _window.Content = new WUC.Border
            {
                Child = _panel,
                CornerRadius = new WUX.CornerRadius(8),
                BorderThickness = new WUX.Thickness(1),
                BorderBrush = new WUM.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0x35, 0x25, 0xCD)),
                Background = (WUM.Brush)WUX.Application.Current.Resources["SolidBackgroundFillColorBaseBrush"],
            };
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            // Sin marco ni titulo, siempre encima; y NOACTIVATE para que el foco no se mueva.
            if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }
            _window.AppWindow.IsShownInSwitchers = false;
            var ex = GetWindowLongPtr(_hwnd, -20 /* GWL_EXSTYLE */).ToInt64() | 0x08000000 /* WS_EX_NOACTIVATE */ | 0x00000080 /* WS_EX_TOOLWINDOW */ | 0x00000008 /* WS_EX_TOPMOST */;
            SetWindowLongPtr(_hwnd, -20, new IntPtr(ex));
            _window.Closed += (_, _) => _visible = false;
        }

        public void Show(IReadOnlyList<Credential> entries, ScreenRect field, Action<Credential> pick)
        {
            if (_window is null)
                Create();
            _panel!.Children.Clear();
            _panel.Children.Add(new WUC.TextBlock { Text = "sOC Credentials", FontSize = 11, Opacity = 0.7, Margin = new WUX.Thickness(8, 2, 8, 4) });
            foreach (var e in entries)
            {
                var text = new WUC.StackPanel();
                text.Children.Add(new WUC.TextBlock { Text = e.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = WUX.TextTrimming.CharacterEllipsis });
                if (e.Username.Length > 0)
                    text.Children.Add(new WUC.TextBlock { Text = e.Username, FontSize = 12, Opacity = 0.75, TextTrimming = WUX.TextTrimming.CharacterEllipsis });
                var button = new WUC.Button
                {
                    Content = text,
                    HorizontalAlignment = WUX.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = WUX.HorizontalAlignment.Left,
                    Padding = new WUX.Thickness(10, 6, 10, 6),
                    Background = new WUM.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new WUX.Thickness(0),
                };
                var entry = e;
                button.Click += (_, _) => pick(entry);
                _panel.Children.Add(button);
            }
            _panel.Children.Add(new WUC.TextBlock { Text = l["DesktopFillHint"], FontSize = 11, Opacity = 0.6, Margin = new WUX.Thickness(8, 4, 8, 2), TextWrapping = WUX.TextWrapping.Wrap });

            // Que no se salga de la pantalla del campo.
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            var work = GetMonitorInfo(MonitorFromWindow(_hwnd, 2 /* MONITOR_DEFAULTTONEAREST */), ref info)
                ? new ScreenRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top)
                : (ScreenRect?)null;
            var r = DesktopAutofillEngine.PopupBounds(field, GetDpiForWindow(_hwnd) / 96.0, entries.Count, work);
            _window!.AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));
            if (!_visible)
            {
                _window.AppWindow.Show(false);
                _visible = true;
            }
            SetWindowPos(_hwnd, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /* NOSIZE | NOMOVE | NOACTIVATE */);
        }

        public void Hide()
        {
            if (!_visible)
                return;
            _visible = false;
            _window!.AppWindow.Hide();
        }
    }

    // ------------------------------------------------------------------ Win32 y MSAA

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public KeybdInput ki; [FieldOffset(0)] public MouseInputPad mi; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputPad { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int cbSize; public Rect rcMonitor; public Rect rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc proc, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool flag);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    [DllImport("oleacc.dll")] private static extern int WindowFromAccessibleObject(IAccessible acc, out IntPtr hwnd);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromEvent(IntPtr hwnd, uint idObject, uint idChild, out IAccessible? acc, [MarshalAs(UnmanagedType.Struct)] out object child);
    [DllImport("oleacc.dll")] private static extern int AccessibleChildren(IAccessible parent, int start, int count, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Struct, SizeParamIndex = 2)] object[] children, out int obtained);
}
