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
/// La lista es una ventana que no se activa (WS_EX_NOACTIVATE): el foco se queda en la aplicacion
/// de destino, asi que si el usuario sigue tecleando no pasa nada raro y, al elegir, lo que se
/// «teclea» va directo al campo. Se esconde en cuanto el foco se va a otro sitio. No se lee nada de
/// los campos de otras aplicaciones: solo se escribe cuando el usuario elige.
/// Las entradas aprenden: la que se usa en un programa se queda con un campo «windows» = ejecutable,
/// y a partir de ahi sale la primera. Los navegadores se ignoran (los cubre la extension).
/// </remarks>
public sealed class DesktopAutofill : IDisposable
{
    private const uint EventObjectFocus = 0x8005;
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const int RoleSystemText = 0x2A;
    private const int StateSystemProtected = 0x20000000;
    private const int StateSystemReadOnly = 0x40;
    private const int SelFlagTakeFocus = 0x1;
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore", "chromium", "msedgewebview2" };

    private readonly VaultStore _store;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _l;
    private readonly WinEventProc _proc;
    private readonly List<IntPtr> _hooks = [];
    private Popup? _popup;
    private bool _disposed;
    // El trabajo con la accesibilidad de otros procesos (llamadas COM que pueden tardar o
    // quedarse colgadas si el otro programa esta ocupado) va en un hilo propio: el de la interfaz
    // solo encola el aviso de foco y enseña o esconde la lista. Cuenta solo el ultimo foco.
    private readonly Thread _worker;
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _gate = new();
    private (IntPtr Hwnd, int Object, int Child)? _pendingFocus;
    private Action? _pendingFill;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _ui;

    public DesktopAutofill(VaultStore store, ISettingsService settings, ILocalizationService l)
    {
        _store = store;
        _settings = settings;
        _l = l;
        _proc = OnWinEvent;
        _ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "sOC desktop autofill" };
        _worker.SetApartmentState(ApartmentState.MTA);
    }

    /// <summary>Engancha los avisos de foco; hay que llamarlo desde el hilo de la interfaz (los avisos llegan a su bucle de mensajes).</summary>
    public void Start()
    {
        if (_hooks.Count > 0)
            return;
        const uint outOfContext = 0x0000, skipOwnProcess = 0x0002;
        _hooks.Add(SetWinEventHook(EventObjectFocus, EventObjectFocus, IntPtr.Zero, _proc, 0, 0, outOfContext | skipOwnProcess));
        _hooks.Add(SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _proc, 0, 0, outOfContext | skipOwnProcess));
        _hooks.Add(SetWinEventHook(EventSystemMinimizeStart, EventSystemMinimizeStart, IntPtr.Zero, _proc, 0, 0, outOfContext | skipOwnProcess));
        _store.Locked += Hide;
        _worker.Start();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var h in _hooks)
            UnhookWinEvent(h);
        _hooks.Clear();
        _store.Locked -= Hide;
        Hide();
        _wake.Set();
    }

    // ------------------------------------------------------------------ foco

    [System.Diagnostics.Conditional("DEBUG")]
    private static void Log(string text)
    {
        try { File.AppendAllText(Path.Combine(VaultStore.DataDirectory, "autofill.log"), $"{DateTime.Now:HH:mm:ss.fff} {text}" + Environment.NewLine); } catch (Exception) { }
    }

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (_disposed || !_settings.DesktopAutofill)
                return;
            if (evt != EventObjectFocus || hwnd == IntPtr.Zero || !_store.IsUnlocked)
            {
                Hide();
                return;
            }
            // Los navegadores van por la extension, y a un proceso ajeno no se le pregunta nada desde este hilo.
            var (exe, _) = WindowContext(hwnd, withTitle: false);
            if (exe.Length == 0 || Browsers.Contains(exe))
            {
                Hide();
                return;
            }
            lock (_gate)
                _pendingFocus = (hwnd, idObject, idChild);
            _wake.Set();
        }
        catch (Exception ex)
        {
            Log("error: " + ex);
            Hide();
        }
    }

    private void WorkerLoop()
    {
        while (!_disposed)
        {
            _wake.WaitOne();
            if (_disposed)
                return;
            (IntPtr Hwnd, int Object, int Child)? focus;
            Action? fill;
            lock (_gate)
            {
                focus = _pendingFocus;
                fill = _pendingFill;
                _pendingFocus = null;
                _pendingFill = null;
            }
            try { fill?.Invoke(); } catch (Exception ex) { Log("fill error: " + ex); }
            if (focus is { } f)
                Inspect(f.Hwnd, f.Object, f.Child);
        }
    }

    /// <summary>En el hilo de trabajo: mira si el foco esta en un campo de contraseña y que entradas casan; el resultado va a la interfaz.</summary>
    private void Inspect(IntPtr hwnd, int idObject, int idChild)
    {
        try
        {
            Log($"focus hwnd={hwnd} obj={idObject} child={idChild}");
            if (AccessibleObjectFromEvent(hwnd, (uint)idObject, (uint)idChild, out var acc, out var child) != 0 || acc is null || !IsPasswordField(acc, child))
            {
                OnUi(Hide);
                return;
            }
            var (exe, title) = WindowContext(hwnd, withTitle: true);
            Log($"password field in {exe} '{title}'");
            var entries = _store.Data?.Entries;
            if (entries is null)
            {
                OnUi(Hide);
                return;
            }
            var candidates = MatchWindows(entries, exe, title);
            Log($"candidates={candidates.Count}");
            if (candidates.Count == 0)
            {
                OnUi(Hide);
                return;
            }
            var rect = Location(acc, child);
            Log($"rect={rect.X},{rect.Y},{rect.Width},{rect.Height}");
            OnUi(() =>
            {
                _store.Touch();
                Show(candidates, acc, child, exe, rect);
                Log("shown");
            });
        }
        catch (Exception ex)
        {
            Log("inspect error: " + ex);
            OnUi(Hide);
        }
    }

    private void OnUi(Action action)
    {
        if (!_ui.TryEnqueue(() => { try { action(); } catch (Exception) { } }))
            Log("ui queue closed");
    }

    private static bool IsPasswordField(IAccessible acc, object child)
    {
        var role = acc.get_accRole(child);
        var state = acc.get_accState(child);
        var roleValue = role is int r ? r : role is uint ur ? (int)ur : -1;
        var stateValue = state is int s ? s : state is uint us ? (int)us : 0;
        return roleValue == RoleSystemText && (stateValue & StateSystemProtected) != 0 && (stateValue & StateSystemReadOnly) == 0;
    }

    private static RectInt32 Location(IAccessible acc, object child)
    {
        acc.accLocation(out var left, out var top, out var width, out var height, child);
        return new RectInt32(left, top, width, height);
    }

    /// <summary>Nombre del ejecutable (sin .exe, en minusculas) y titulo de la ventana principal que tiene el campo.</summary>
    private static (string Exe, string Title) WindowContext(IntPtr hwnd, bool withTitle)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        var exe = string.Empty;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            exe = process.ProcessName.ToLowerInvariant();
        }
        catch (Exception) { }
        if (!withTitle)
            return (exe, string.Empty);
        var top = GetAncestor(hwnd, 2 /* GA_ROOT */);
        var buffer = new System.Text.StringBuilder(512);
        GetWindowText(top == IntPtr.Zero ? hwnd : top, buffer, buffer.Capacity);
        return (exe, buffer.ToString());
    }

    /// <summary>
    /// Entradas que casan con un programa de Windows: primero las que ya lo aprendieron (campo
    /// «windows»), luego por parecido entre el ejecutable o el titulo de la ventana y el titulo o el
    /// dominio de la entrada.
    /// </summary>
    public static List<Credential> MatchWindows(IEnumerable<Credential> entries, string exe, string windowTitle)
    {
        var live = entries.Where(e => !e.Deleted && e.Kind is EntryKind.Login or EntryKind.App && e.Password.Length > 0).ToList();
        var key = exe.ToLowerInvariant();
        var title = windowTitle.ToLowerInvariant();
        static string FirstLabel(string host)
        {
            var h = host.ToLowerInvariant();
            if (h.StartsWith("www.")) h = h[4..];
            var i = h.IndexOf('.');
            return i > 0 ? h[..i] : h;
        }
        var learned = live.Where(e => e.Fields.Any(f => f.Name.Equals("windows", StringComparison.OrdinalIgnoreCase) && f.Value.Trim().Equals(key, StringComparison.OrdinalIgnoreCase))).ToList();
        var similar = live.Except(learned).Where(e =>
        {
            var label = FirstLabel(e.Host);
            var t = e.Title.ToLowerInvariant();
            if (label.Length > 3 && (key.Contains(label) || label.Contains(key) || title.Contains(label)))
                return true;
            var words = t.Split([' ', '-', '_', '.', ':', '(', ')'], StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3).ToList();
            if (words.Any(w => key.Contains(w) || (w.Length > 4 && title.Contains(w))))
                return true;
            return t.Length > 3 && (key.Contains(t) || title.Contains(t));
        }).ToList();
        return learned.OrderByDescending(e => e.Favorite).ThenBy(e => e.Title)
            .Concat(similar.OrderByDescending(e => e.Favorite).ThenBy(e => e.Title))
            .Take(8).ToList();
    }

    // ------------------------------------------------------------------ la lista

    private void Show(List<Credential> candidates, IAccessible acc, object child, string exe, RectInt32 field)
    {
        _popup ??= new Popup(_l);
        _popup.Fill(candidates, entry =>
        {
            Hide();
            lock (_gate)
                _pendingFill = () => Fill(entry, acc, child, exe);
            _wake.Set();
        });
        _popup.ShowAt(field);
    }

    private void Hide()
    {
        try { _popup?.Hide(); } catch (Exception) { }
    }

    /// <summary>En el hilo de trabajo: usuario en su campo, contraseña en el suyo, y la entrada aprende el programa.</summary>
    private void Fill(Credential entry, IAccessible passwordField, object passwordChild, string exe)
    {
        try
        {
            if (entry.Username.Length > 0 && FindUsernameField(passwordField, passwordChild) is { } user)
            {
                if (Focus(user.Acc, user.Child))
                {
                    Thread.Sleep(80);
                    TypeText(entry.Username);
                    Thread.Sleep(80);
                    Focus(passwordField, passwordChild);
                    Thread.Sleep(80);
                }
            }
            TypeText(entry.Password);
        }
        catch (Exception) { /* la aplicacion de destino se cerro o no admite escritura: no pasa nada */ }
        // La entrada aprende el programa: la proxima vez sale la primera. (La boveda se toca en la interfaz.)
        OnUi(async () =>
        {
            try
            {
                if (!entry.Fields.Any(f => f.Name.Equals("windows", StringComparison.OrdinalIgnoreCase) && f.Value.Trim().Equals(exe, StringComparison.OrdinalIgnoreCase)))
                {
                    entry.Fields.Add(new CustomField { Name = "windows", Value = exe });
                    entry.ModifiedAt = DateTimeOffset.UtcNow;
                    await _store.SaveAsync();
                }
                _store.Touch();
            }
            catch (Exception) { }
        });
    }

    private const int RoleSystemWindow = 0x9;

    /// <summary>
    /// El campo de usuario: entre los hermanos de la contraseña, el campo de texto editable (no
    /// protegido) mas cercano por encima; si no hay ninguno encima, el mas cercano en general. Va por
    /// geometria porque el orden en que el sistema enumera los hijos no es el visual. En los
    /// controles Win32 cada campo es un objeto «ventana» con el texto como hijo, asi que se sube un
    /// nivel y se mira dentro de cada ventana hermana.
    /// </summary>
    private static (IAccessible Acc, object Child)? FindUsernameField(IAccessible password, object passwordChild)
    {
        try
        {
            IAccessible? parent;
            if (passwordChild is int id && id != 0)
                parent = password;   // elemento simple: sus hermanos son los hijos del mismo IAccessible
            else
                parent = password.get_accParent() as IAccessible;
            if (parent is null)
                return null;
            if (RoleOf(parent, 0) == RoleSystemWindow && parent.get_accParent() is IAccessible grand)
                parent = grand;
            password.accLocation(out var pl, out var pt, out _, out _, passwordChild);
            (IAccessible Acc, object Child)? best = null;
            var bestScore = double.MaxValue;
            foreach (var (acc, child) in TextFields(parent, 0))
            {
                try
                {
                    var st = StateOf(acc, child);
                    if ((st & StateSystemProtected) != 0 || (st & StateSystemReadOnly) != 0)
                        continue;
                    acc.accLocation(out var l, out var t, out var w, out var h, child);
                    if (w <= 0 || h <= 0 || (l == pl && t == pt))
                        continue;
                    // Encima y cerca puntua mejor; debajo o muy lejos, peor.
                    var dy = pt - t;
                    var score = dy >= 0 ? dy + Math.Abs(l - pl) * 0.2 : 10000 + (-dy) + Math.Abs(l - pl) * 0.2;
                    if (score < bestScore) { bestScore = score; best = (acc, child); }
                }
                catch (Exception) { }
            }
            return best;
        }
        catch (Exception ex) { Log("username: error " + ex); return null; }
    }

    /// <summary>Los campos de texto que cuelgan de un objeto: los hijos de tipo texto y, dentro de cada hijo «ventana», su texto.</summary>
    private static IEnumerable<(IAccessible Acc, object Child)> TextFields(IAccessible parent, int depth)
    {
        int count;
        try { count = parent.get_accChildCount(); } catch (Exception) { yield break; }
        if (count <= 0 || count > 500)
            yield break;
        var children = new object[count];
        if (AccessibleChildren(parent, 0, count, children, out var got) != 0)
            yield break;
        for (var i = 0; i < got; i++)
        {
            var s = children[i];
            IAccessible acc; object child;
            if (s is IAccessible a) { acc = a; child = 0; }
            else { acc = parent; child = s; }
            var role = RoleOf(acc, child);
            if (role == RoleSystemText)
                yield return (acc, child);
            else if (role == RoleSystemWindow && depth < 1 && s is IAccessible window)
                foreach (var inner in TextFields(window, depth + 1))
                    yield return inner;
        }
    }

    private static int RoleOf(IAccessible acc, object child)
    {
        try { return acc.get_accRole(child) is int r ? r : -1; } catch (Exception) { return -1; }
    }

    private static int StateOf(IAccessible acc, object child)
    {
        try { return acc.get_accState(child) is int st ? st : 0; } catch (Exception) { return 0; }
    }

    /// <summary>
    /// Lleva el foco a un campo de otra aplicacion. Los controles Win32 tienen ventana propia: con
    /// la entrada de hilos enlazada (AttachThreadInput) vale SetFocus; los demas (WPF, WinUI…) van
    /// por accesibilidad (accSelect TAKEFOCUS). Devuelve si el foco esta donde se pedia.
    /// </summary>
    private static bool Focus(IAccessible acc, object child)
    {
        var hwnd = IntPtr.Zero;
        try { WindowFromAccessibleObject(acc, out hwnd); } catch (Exception) { }
        var target = GetWindowThreadProcessId(hwnd, out _);
        var mine = GetCurrentThreadId();
        var attached = hwnd != IntPtr.Zero && target != 0 && target != mine && AttachThreadInput(mine, target, true);
        try
        {
            if (hwnd != IntPtr.Zero && (child is not int c || c == 0))
                SetFocus(hwnd);
            try { acc.accSelect(SelFlagTakeFocus, child); } catch (Exception) { }
            if (attached && hwnd != IntPtr.Zero)
                return GetFocus() == hwnd;
            return true;
        }
        catch (Exception) { return false; }
        finally
        {
            if (attached)
                AttachThreadInput(mine, target, false);
        }
    }

    /// <summary>Teclea el texto en el control con el foco (SendInput con KEYEVENTF_UNICODE: vale para cualquier caracter).</summary>
    private static void TypeText(string text)
    {
        if (text.Length == 0)
            return;
        var inputs = new List<Input>(text.Length * 2);
        foreach (var ch in text)
        {
            inputs.Add(new Input { type = 1, u = new InputUnion { ki = new KeybdInput { wScan = ch, dwFlags = 0x0004 /* KEYEVENTF_UNICODE */ } } });
            inputs.Add(new Input { type = 1, u = new InputUnion { ki = new KeybdInput { wScan = ch, dwFlags = 0x0004 | 0x0002 /* KEYUP */ } } });
        }
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
    }

    /// <summary>La ventanita con la lista: sin marco, siempre encima y sin activarse nunca.</summary>
    private sealed class Popup
    {
        private readonly WUX.Window _window;
        private readonly WUC.StackPanel _panel;
        private readonly ILocalizationService _l;
        private readonly IntPtr _hwnd;
        private bool _visible;

        public Popup(ILocalizationService l)
        {
            _l = l;
            _window = new WUX.Window();
            _panel = new WUC.StackPanel { Padding = new WUX.Thickness(6), Spacing = 2 };
            var border = new WUC.Border
            {
                Child = _panel,
                CornerRadius = new WUX.CornerRadius(8),
                BorderThickness = new WUX.Thickness(1),
                BorderBrush = new WUM.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0x35, 0x25, 0xCD)),
                Background = (WUM.Brush)WUX.Application.Current.Resources["SolidBackgroundFillColorBaseBrush"],
            };
            _window.Content = border;
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
            var ex = GetWindowLongPtr(_hwnd, -20 /* GWL_EXSTYLE */).ToInt64();
            ex |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x00000080 /* WS_EX_TOOLWINDOW */ | 0x00000008 /* WS_EX_TOPMOST */;
            SetWindowLongPtr(_hwnd, -20, new IntPtr(ex));
            _window.Closed += (_, _) => _visible = false;
        }

        public void Fill(List<Credential> entries, Action<Credential> pick)
        {
            _panel.Children.Clear();
            var header = new WUC.TextBlock { Text = "sOC Credentials", FontSize = 11, Opacity = 0.7, Margin = new WUX.Thickness(8, 2, 8, 4) };
            _panel.Children.Add(header);
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
            var hint = new WUC.TextBlock { Text = _l["DesktopFillHint"], FontSize = 11, Opacity = 0.6, Margin = new WUX.Thickness(8, 4, 8, 2), TextWrapping = WUX.TextWrapping.Wrap };
            _panel.Children.Add(hint);
        }

        public void ShowAt(RectInt32 field)
        {
            var scale = GetDpiForWindow(_hwnd) / 96.0;
            if (scale <= 0) scale = 1;
            var width = (int)(Math.Max(260, Math.Min(420, field.Width)) * scale);
            var entries = Math.Max(0, _panel.Children.Count - 2);   // menos la cabecera y la pista
            var height = (int)((26 + entries * 50 + 40 + 12) * scale);
            var x = field.X;
            var y = field.Y + field.Height + 2;
            // Que no se salga de la pantalla del campo.
            var monitor = MonitorFromWindow(_hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                if (x + width > info.rcWork.Right) x = Math.Max(info.rcWork.Left, info.rcWork.Right - width);
                if (y + height > info.rcWork.Bottom) y = Math.Max(info.rcWork.Top, field.Y - height - 2);
            }
            _window.AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
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
            _window.AppWindow.Hide();
        }
    }

    // ------------------------------------------------------------------ Win32 y MSAA

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [ComImport, Guid("618736E0-3C3D-11CF-810C-00AA00389B71"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IAccessible
    {
        [return: MarshalAs(UnmanagedType.IDispatch)] object? get_accParent();
        int get_accChildCount();
        [return: MarshalAs(UnmanagedType.IDispatch)] object? get_accChild([MarshalAs(UnmanagedType.Struct)] object childId);
        [return: MarshalAs(UnmanagedType.BStr)] string? get_accName([MarshalAs(UnmanagedType.Struct)] object childId);
        [return: MarshalAs(UnmanagedType.BStr)] string? get_accValue([MarshalAs(UnmanagedType.Struct)] object childId);
        [return: MarshalAs(UnmanagedType.BStr)] string? get_accDescription([MarshalAs(UnmanagedType.Struct)] object childId);
        [return: MarshalAs(UnmanagedType.Struct)] object get_accRole([MarshalAs(UnmanagedType.Struct)] object childId);
        [return: MarshalAs(UnmanagedType.Struct)] object get_accState([MarshalAs(UnmanagedType.Struct)] object childId);
        [return: MarshalAs(UnmanagedType.BStr)] string? get_accHelp([MarshalAs(UnmanagedType.Struct)] object childId);
        int get_accHelpTopic([MarshalAs(UnmanagedType.BStr)] out string? helpFile, [MarshalAs(UnmanagedType.Struct)] object childId);
        [return: MarshalAs(UnmanagedType.BStr)] string? get_accKeyboardShortcut([MarshalAs(UnmanagedType.Struct)] object childId);
        [return: MarshalAs(UnmanagedType.Struct)] object? get_accFocus();
        [return: MarshalAs(UnmanagedType.Struct)] object? get_accSelection();
        [return: MarshalAs(UnmanagedType.BStr)] string? get_accDefaultAction([MarshalAs(UnmanagedType.Struct)] object childId);
        void accSelect(int flagsSelect, [MarshalAs(UnmanagedType.Struct)] object childId);
        void accLocation(out int left, out int top, out int width, out int height, [MarshalAs(UnmanagedType.Struct)] object childId);
        [return: MarshalAs(UnmanagedType.Struct)] object? accNavigate(int navDir, [MarshalAs(UnmanagedType.Struct)] object start);
        [return: MarshalAs(UnmanagedType.Struct)] object? accHitTest(int left, int top);
        void accDoDefaultAction([MarshalAs(UnmanagedType.Struct)] object childId);
        void set_accName([MarshalAs(UnmanagedType.Struct)] object childId, [MarshalAs(UnmanagedType.BStr)] string name);
        void set_accValue([MarshalAs(UnmanagedType.Struct)] object childId, [MarshalAs(UnmanagedType.BStr)] string value);
    }

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
