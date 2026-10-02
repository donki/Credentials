using Credentials.Models;
using Credentials.Services;

namespace Credentials.Platforms.Windows;

/// <summary>Un rectangulo de pantalla (en pixeles fisicos).</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>
/// Un elemento de accesibilidad de otra aplicacion (en MSAA, un IAccessible y un id de hijo). En la
/// aplicacion lo da DesktopAutofill (COM); en las pruebas, un arbol en memoria.
/// </summary>
public interface IAccElement
{
    /// <summary>Rol MSAA (ROLE_SYSTEM_*), o -1 si no se puede leer.</summary>
    int Role { get; }

    /// <summary>Estado MSAA (STATE_SYSTEM_*), o 0 si no se puede leer.</summary>
    int State { get; }

    /// <summary>Hijo simple (id de hijo distinto de 0): sus hermanos son los hijos del mismo objeto.</summary>
    bool IsSimpleChild { get; }

    /// <summary>Donde esta en pantalla (puede lanzar si la otra aplicacion ya no esta).</summary>
    ScreenRect Location();

    /// <summary>El objeto que lo contiene: para un hijo simple, su propio objeto; si no, el padre (accParent).</summary>
    IAccElement? Container();

    /// <summary>El padre del objeto (accParent), o null.</summary>
    IAccElement? Parent();

    /// <summary>Cuantos hijos tiene (puede lanzar).</summary>
    int ChildCount();

    /// <summary>Sus hijos (AccessibleChildren), o null si no se pueden leer.</summary>
    IReadOnlyList<IAccElement>? Children(int count);

    /// <summary>accSelect(TAKEFOCUS) (puede lanzar).</summary>
    void TakeFocus();
}

/// <summary>Lo que el autocompletar de escritorio necesita de Windows (Win32 y el hilo de la interfaz).</summary>
public interface IDesktopNative
{
    /// <summary>Nombre del proceso dueño de la ventana (sin .exe), o vacio.</summary>
    string ProcessName(IntPtr hwnd);

    /// <summary>Titulo de la ventana principal que tiene el campo.</summary>
    string WindowTitle(IntPtr hwnd);

    /// <summary>El elemento que acaba de recibir el foco (AccessibleObjectFromEvent), o null.</summary>
    IAccElement? FromEvent(IntPtr hwnd, int idObject, int idChild);

    /// <summary>La ventana Win32 del elemento (WindowFromAccessibleObject), o cero.</summary>
    IntPtr WindowOf(IAccElement element);

    uint ThreadOf(IntPtr hwnd);

    uint CurrentThread { get; }

    bool AttachThreadInput(uint attach, uint to, bool on);

    void SetFocus(IntPtr hwnd);

    IntPtr GetFocus();

    /// <summary>Teclea con SendInput las pulsaciones dadas (wScan, dwFlags).</summary>
    void SendKeys(IReadOnlyList<(ushort Scan, uint Flags)> keys);

    void Sleep(int milliseconds);

    /// <summary>Encola una accion en el hilo de la interfaz; false si la cola ya no admite nada.</summary>
    bool PostToUi(Action action);
}

/// <summary>La lista pegada al campo (una ventana que no se activa).</summary>
public interface IAutofillPopup
{
    void Show(IReadOnlyList<Credential> entries, ScreenRect field, Action<Credential> pick);

    void Hide();
}

/// <summary>
/// La logica del autocompletar en las aplicaciones de Windows (DesktopAutofill pone los avisos de
/// foco, la accesibilidad y la ventanita). Cuando el foco cae en un campo de contraseña de otra
/// aplicacion (ROLE_SYSTEM_TEXT + STATE_SYSTEM_PROTECTED) se buscan las entradas que casan con ese
/// programa y se enseña la lista; al elegir una se teclea el usuario en el campo de texto anterior
/// y la contraseña en el suyo, y la entrada aprende el programa.
/// </summary>
/// <remarks>
/// El trabajo con la accesibilidad de otros procesos (llamadas COM que pueden tardar o quedarse
/// colgadas si el otro programa esta ocupado) va en un hilo propio: el de la interfaz solo encola
/// el aviso de foco y enseña o esconde la lista. Cuenta solo el ultimo foco.
/// </remarks>
public sealed class DesktopAutofillEngine(VaultStore store, ISettingsService settings, IDesktopNative native, IAutofillPopup popup) : IDisposable
{
    public const uint EventObjectFocus = 0x8005;
    public const uint EventSystemForeground = 0x0003;
    public const uint EventSystemMinimizeStart = 0x0016;
    public const int RoleSystemText = 0x2A;
    public const int RoleSystemWindow = 0x9;
    public const int StateSystemProtected = 0x20000000;
    public const int StateSystemReadOnly = 0x40;
    public const uint KeyEventUnicode = 0x0004;
    public const uint KeyEventKeyUp = 0x0002;

    /// <summary>Los navegadores van por la extension.</summary>
    public static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore", "chromium", "msedgewebview2" };

    private readonly AutoResetEvent _wake = new(false);
    private readonly object _gate = new();
    private (IntPtr Hwnd, int Object, int Child)? _pendingFocus;
    private Action? _pendingFill;
    private Thread? _worker;
    private volatile bool _disposed;

    /// <summary>Registro de lo que pasa (solo en Debug, a un fichero).</summary>
    public Action<string>? Log { get; set; }

    /// <summary>La pausa entre pasos al teclear (el otro programa tiene que ver el cambio de foco).</summary>
    public int StepDelayMs { get; init; } = 80;

    public bool IsDisposed => _disposed;

    /// <summary>Arranca el hilo de trabajo y esconde la lista al cerrarse la boveda.</summary>
    public void Start()
    {
        if (_worker is not null)
            return;
        store.Locked += Hide;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "sOC desktop autofill" };
        if (OperatingSystem.IsWindows())
            _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        store.Locked -= Hide;
        Hide();
        _wake.Set();
    }

    /// <summary>Espera a que termine el hilo de trabajo (tras Dispose).</summary>
    public bool Join(TimeSpan timeout) => _worker?.Join(timeout) ?? true;

    // ------------------------------------------------------------------ foco

    /// <summary>Un aviso de foco (en el hilo de la interfaz): o se esconde la lista o se encola para el hilo de trabajo.</summary>
    public void OnWinEvent(uint evt, IntPtr hwnd, int idObject, int idChild)
    {
        try
        {
            if (_disposed || !settings.DesktopAutofill)
                return;
            if (evt != EventObjectFocus || hwnd == IntPtr.Zero || !store.IsUnlocked)
            {
                Hide();
                return;
            }
            // Los navegadores van por la extension, y a un proceso ajeno no se le pregunta nada desde este hilo.
            var exe = native.ProcessName(hwnd).ToLowerInvariant();
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
            Log?.Invoke("error: " + ex);
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
            ProcessPending();
        }
    }

    /// <summary>En el hilo de trabajo: lo pendiente (rellenar lo elegido y mirar el ultimo foco).</summary>
    public void ProcessPending()
    {
        (IntPtr Hwnd, int Object, int Child)? focus;
        Action? fill;
        lock (_gate)
        {
            focus = _pendingFocus;
            fill = _pendingFill;
            _pendingFocus = null;
            _pendingFill = null;
        }
        try { fill?.Invoke(); } catch (Exception ex) { Log?.Invoke("fill error: " + ex); }
        if (focus is { } f)
            Inspect(f.Hwnd, f.Object, f.Child);
    }

    /// <summary>En el hilo de trabajo: mira si el foco esta en un campo de contraseña y que entradas casan; el resultado va a la interfaz.</summary>
    public void Inspect(IntPtr hwnd, int idObject, int idChild)
    {
        try
        {
            Log?.Invoke($"focus hwnd={hwnd} obj={idObject} child={idChild}");
            var field = native.FromEvent(hwnd, idObject, idChild);
            if (field is null || !IsPasswordField(field))
            {
                OnUi(Hide);
                return;
            }
            var exe = native.ProcessName(hwnd).ToLowerInvariant();
            var title = native.WindowTitle(hwnd);
            Log?.Invoke($"password field in {exe} '{title}'");
            var entries = store.Data?.Entries;
            var candidates = entries is null ? [] : MatchWindows(entries, exe, title);
            Log?.Invoke($"candidates={candidates.Count}");
            if (candidates.Count == 0)
            {
                OnUi(Hide);
                return;
            }
            var rect = field.Location();
            OnUi(() =>
            {
                store.Touch();
                popup.Show(candidates, rect, entry => Pick(entry, field, exe));
            });
        }
        catch (Exception ex)
        {
            Log?.Invoke("inspect error: " + ex);
            OnUi(Hide);
        }
    }

    /// <summary>El usuario ha elegido una entrada: se esconde la lista y se rellena en el hilo de trabajo.</summary>
    private void Pick(Credential entry, IAccElement field, string exe)
    {
        Hide();
        lock (_gate)
            _pendingFill = () => Fill(entry, field, exe);
        _wake.Set();
    }

    private void OnUi(Action action)
    {
        if (!native.PostToUi(() => { try { action(); } catch (Exception) { } }))
            Log?.Invoke("ui queue closed");
    }

    public void Hide()
    {
        try { popup.Hide(); } catch (Exception) { }
    }

    /// <summary>Campo de texto protegido (contraseña) y que se puede escribir.</summary>
    public static bool IsPasswordField(IAccElement field)
    {
        var state = field.State;
        return field.Role == RoleSystemText && (state & StateSystemProtected) != 0 && (state & StateSystemReadOnly) == 0;
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
        var learned = live.Where(e => Learned(e, key)).ToList();
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

    /// <summary>La entrada ya se uso en ese programa (campo «windows» = ejecutable).</summary>
    public static bool Learned(Credential entry, string exe) =>
        entry.Fields.Any(f => f.Name.Equals("windows", StringComparison.OrdinalIgnoreCase) && f.Value.Trim().Equals(exe, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ rellenar

    /// <summary>En el hilo de trabajo: usuario en su campo, contraseña en el suyo, y la entrada aprende el programa.</summary>
    public void Fill(Credential entry, IAccElement passwordField, string exe)
    {
        try
        {
            if (entry.Username.Length > 0 && FindUsernameField(passwordField, Log) is { } user && Focus(user))
            {
                native.Sleep(StepDelayMs);
                native.SendKeys(KeyStrokes(entry.Username));
                native.Sleep(StepDelayMs);
                Focus(passwordField);
                native.Sleep(StepDelayMs);
            }
            var keys = KeyStrokes(entry.Password);
            if (keys.Count > 0)
                native.SendKeys(keys);
        }
        catch (Exception) { /* la aplicacion de destino se cerro o no admite escritura: no pasa nada */ }
        // La entrada aprende el programa: la proxima vez sale la primera. (La boveda se toca en la interfaz.)
        OnUi(async () =>
        {
            try
            {
                if (!Learned(entry, exe))
                {
                    entry.Fields.Add(new CustomField { Name = "windows", Value = exe });
                    entry.ModifiedAt = DateTimeOffset.UtcNow;
                    await store.SaveAsync();
                }
                store.Touch();
            }
            catch (Exception) { }
        });
    }

    /// <summary>Las pulsaciones de SendInput para teclear un texto (KEYEVENTF_UNICODE: vale para cualquier caracter).</summary>
    public static List<(ushort Scan, uint Flags)> KeyStrokes(string text)
    {
        var keys = new List<(ushort, uint)>(text.Length * 2);
        foreach (var ch in text)
        {
            keys.Add((ch, KeyEventUnicode));
            keys.Add((ch, KeyEventUnicode | KeyEventKeyUp));
        }
        return keys;
    }

    /// <summary>
    /// Lleva el foco a un campo de otra aplicacion. Los controles Win32 tienen ventana propia: con
    /// la entrada de hilos enlazada (AttachThreadInput) vale SetFocus; los demas (WPF, WinUI…) van
    /// por accesibilidad (accSelect TAKEFOCUS). Devuelve si el foco esta donde se pedia.
    /// </summary>
    public bool Focus(IAccElement element)
    {
        var hwnd = IntPtr.Zero;
        try { hwnd = native.WindowOf(element); } catch (Exception) { }
        var target = native.ThreadOf(hwnd);
        var mine = native.CurrentThread;
        var attached = hwnd != IntPtr.Zero && target != 0 && target != mine && native.AttachThreadInput(mine, target, true);
        try
        {
            if (hwnd != IntPtr.Zero && !element.IsSimpleChild)
                native.SetFocus(hwnd);
            try { element.TakeFocus(); } catch (Exception) { }
            if (attached)
                return native.GetFocus() == hwnd;
            return true;
        }
        catch (Exception) { return false; }
        finally
        {
            if (attached)
                native.AttachThreadInput(mine, target, false);
        }
    }

    /// <summary>
    /// El campo de usuario: entre los hermanos de la contraseña, el campo de texto editable (no
    /// protegido) mas cercano por encima; si no hay ninguno encima, el mas cercano en general. Va por
    /// geometria porque el orden en que el sistema enumera los hijos no es el visual. En los
    /// controles Win32 cada campo es un objeto «ventana» con el texto como hijo, asi que se sube un
    /// nivel y se mira dentro de cada ventana hermana.
    /// </summary>
    public static IAccElement? FindUsernameField(IAccElement password, Action<string>? log = null)
    {
        try
        {
            var parent = password.Container();
            if (parent is null)
                return null;
            if (parent.Role == RoleSystemWindow && parent.Parent() is { } grand)
                parent = grand;
            var p = password.Location();
            IAccElement? best = null;
            var bestScore = double.MaxValue;
            foreach (var candidate in TextFields(parent, 0))
            {
                try
                {
                    var st = candidate.State;
                    if ((st & StateSystemProtected) != 0 || (st & StateSystemReadOnly) != 0)
                        continue;
                    var r = candidate.Location();
                    if (r.Width <= 0 || r.Height <= 0 || (r.X == p.X && r.Y == p.Y))
                        continue;
                    // Encima y cerca puntua mejor; debajo o muy lejos, peor.
                    var dy = p.Y - r.Y;
                    var score = dy >= 0 ? dy + Math.Abs(r.X - p.X) * 0.2 : 10000 + (-dy) + Math.Abs(r.X - p.X) * 0.2;
                    if (score < bestScore) { bestScore = score; best = candidate; }
                }
                catch (Exception) { }
            }
            return best;
        }
        catch (Exception ex) { log?.Invoke("username: error " + ex); return null; }
    }

    /// <summary>Los campos de texto que cuelgan de un objeto: los hijos de tipo texto y, dentro de cada hijo «ventana», su texto.</summary>
    public static IEnumerable<IAccElement> TextFields(IAccElement parent, int depth)
    {
        int count;
        try { count = parent.ChildCount(); } catch (Exception) { yield break; }
        if (count <= 0 || count > 500)
            yield break;
        if (parent.Children(count) is not { } children)
            yield break;
        foreach (var child in children)
        {
            var role = child.Role;
            if (role == RoleSystemText)
                yield return child;
            else if (role == RoleSystemWindow && depth < 1 && !child.IsSimpleChild)
                foreach (var inner in TextFields(child, depth + 1))
                    yield return inner;
        }
    }

    // ------------------------------------------------------------------ la lista

    /// <summary>
    /// Donde va la lista: debajo del campo, de 260 a 420 de ancho (escalado), con alto para las
    /// entradas, la cabecera y la pista, sin salirse de la zona de trabajo de la pantalla (si no cabe
    /// debajo, encima).
    /// </summary>
    public static ScreenRect PopupBounds(ScreenRect field, double scale, int entries, ScreenRect? work)
    {
        if (scale <= 0) scale = 1;
        var width = (int)(Math.Max(260, Math.Min(420, field.Width)) * scale);
        var height = (int)((26 + Math.Max(0, entries) * 50 + 40 + 12) * scale);
        var x = field.X;
        var y = field.Bottom + 2;
        if (work is { } w)
        {
            if (x + width > w.Right) x = Math.Max(w.X, w.Right - width);
            if (y + height > w.Bottom) y = Math.Max(w.Y, field.Y - height - 2);
        }
        return new ScreenRect(x, y, width, height);
    }
}
