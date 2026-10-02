using Credentials.Models;
using Credentials.Platforms.Windows;
using static Credentials.Platforms.Windows.DesktopAutofillEngine;

namespace Credentials.Tests;

/// <summary>El autocompletar en las aplicaciones de Windows, con la accesibilidad, Win32 y la lista de mentira.</summary>
public class DesktopAutofillTests
{
    private const string Master = "Correcta Caballo Pila Grapa";

    private static Credential Entry(string title, string url = "", string user = "ana", string password = "p", bool fav = false) =>
        new() { Title = title, Url = url, Username = user, Password = password, Favorite = fav };

    // ------------------------------------------------------------------ que entradas casan

    [Fact]
    public void MatchWindows_LearnedFirst_ThenSimilar()
    {
        var learned = Entry("Cualquiera");
        learned.Fields.Add(new CustomField { Name = "Windows", Value = " keepass " });
        var entries = new List<Credential>
        {
            Entry("Zeta KeePass base"),                       // palabra del titulo en el exe
            Entry("Correo", "https://www.keepass.info"),      // etiqueta del dominio en el exe
            learned,
            Entry("Favorita KeePass", fav: true),
            Entry("Borrada KeePass").With(e => e.Deleted = true),
            Entry("Nota KeePass").With(e => e.Kind = EntryKind.Note),
            Entry("Sin clave KeePass", password: ""),
            Entry("Nada que ver", "https://otra.org"),
        };

        Assert.Equal(["Cualquiera", "Favorita KeePass", "Correo", "Zeta KeePass base"], MatchWindows(entries, "KeePass", "Base.kdbx - KeePass").Select(e => e.Title));
        Assert.Empty(MatchWindows(entries, "notepad", "Sin titulo - Bloc de notas"));
    }

    [Fact]
    public void MatchWindows_ByWindowTitle_AndAtMostEight()
    {
        var entries = new List<Credential>
        {
            Entry("Banco"),                                 // titulo entero (4 letras) en el de la ventana
            Entry("Mi Steam", "https://store.steampowered.com"), // etiqueta del dominio en el titulo
            Entry("Discord app"),                           // palabra de mas de 4 letras en el titulo
            Entry("App"),                                   // demasiado corta: no cuenta
        };
        Assert.Equal(["Banco", "Discord app"], MatchWindows(entries, "client", "Banco - Discord").Select(e => e.Title));
        Assert.Equal(["Mi Steam"], MatchWindows(entries, "launcher", "Steampowered login").Select(e => e.Title));
        // El exe contenido en la etiqueta del dominio tambien vale.
        Assert.Equal(["Mi Steam"], MatchWindows(entries, "steam", "").Select(e => e.Title));

        var many = Enumerable.Range(0, 12).Select(i => Entry($"Juego {i:00} steam")).ToList();
        Assert.Equal(8, MatchWindows(many, "steam", "").Count);
    }

    // ------------------------------------------------------------------ el campo de usuario

    [Fact]
    public void FindUsername_ClosestAbove_SkipsProtectedReadOnlyAndHidden()
    {
        var parent = new FakeElement("form");
        var password = parent.Add(new FakeElement("pass", RoleSystemText, StateSystemProtected, new(100, 200, 200, 20)));
        parent.Add(new FakeElement("lejos-arriba", RoleSystemText, 0, new(100, 20, 200, 20)));
        var near = parent.Add(new FakeElement("usuario", RoleSystemText, 0, new(110, 160, 200, 20)));
        parent.Add(new FakeElement("debajo", RoleSystemText, 0, new(100, 230, 200, 20)));
        parent.Add(new FakeElement("protegido", RoleSystemText, StateSystemProtected, new(100, 170, 200, 20)));
        parent.Add(new FakeElement("solo-lectura", RoleSystemText, StateSystemReadOnly, new(100, 175, 200, 20)));
        parent.Add(new FakeElement("escondido", RoleSystemText, 0, new(100, 180, 0, 20)));
        parent.Add(new FakeElement("mismo-sitio", RoleSystemText, 0, new(100, 200, 50, 20)));
        parent.Add(new FakeElement("boton", 0x2B, 0, new(100, 190, 50, 20)));
        parent.Add(new FakeElement("roto", RoleSystemText, 0, null));
        password.ContainerElement = parent;

        Assert.Same(near, FindUsernameField(password));

        // Sin nada encima, el mas cercano por debajo.
        var below = new FakeElement("form2");
        var pass2 = below.Add(new FakeElement("pass", RoleSystemText, StateSystemProtected, new(0, 100, 10, 10)));
        below.Add(new FakeElement("lejos", RoleSystemText, 0, new(0, 400, 10, 10)));
        var closer = below.Add(new FakeElement("cerca", RoleSystemText, 0, new(0, 120, 10, 10)));
        pass2.ContainerElement = below;
        Assert.Same(closer, FindUsernameField(pass2));
    }

    [Fact]
    public void FindUsername_Win32Controls_LookInsideSiblingWindows()
    {
        // Cada campo Win32 es un objeto «ventana» con el texto dentro: se sube al abuelo y se mira en cada ventana.
        var dialog = new FakeElement("dialogo");
        var userWindow = dialog.Add(new FakeElement("ventana-usuario", RoleSystemWindow));
        var user = userWindow.Add(new FakeElement("texto-usuario", RoleSystemText, 0, new(10, 10, 100, 20)));
        var passWindow = dialog.Add(new FakeElement("ventana-pass", RoleSystemWindow) { ParentElement = dialog });
        var password = passWindow.Add(new FakeElement("texto-pass", RoleSystemText, StateSystemProtected, new(10, 50, 100, 20)));
        password.ContainerElement = passWindow;
        // Una ventana simple (hijo sin objeto propio) no se recorre; una ventana dentro de otra, tampoco (un nivel).
        dialog.Add(new FakeElement("ventana-simple", RoleSystemWindow) { IsSimpleChild = true }).Add(new FakeElement("t", RoleSystemText, 0, new(10, 45, 10, 10)));
        var nested = userWindow.Add(new FakeElement("anidada", RoleSystemWindow));
        nested.Add(new FakeElement("muy-dentro", RoleSystemText, 0, new(10, 49, 10, 10)));

        Assert.Same(user, FindUsernameField(password));
    }

    [Fact]
    public void FindUsername_SimpleChild_UsesItsOwnObject()
    {
        var obj = new FakeElement("objeto");
        var user = obj.Add(new FakeElement("u", RoleSystemText, 0, new(0, 0, 10, 10)) { IsSimpleChild = true });
        var password = obj.Add(new FakeElement("p", RoleSystemText, StateSystemProtected, new(0, 30, 10, 10)) { IsSimpleChild = true, ContainerElement = obj });
        Assert.Same(user, FindUsernameField(password));
    }

    [Fact]
    public void FindUsername_NothingToFind()
    {
        var log = new List<string>();
        var orphan = new FakeElement("p", RoleSystemText, StateSystemProtected, new(0, 0, 1, 1));
        Assert.Null(FindUsernameField(orphan));

        var parent = new FakeElement("f");
        var p = parent.Add(new FakeElement("p", RoleSystemText, StateSystemProtected, null));
        p.ContainerElement = parent;
        Assert.Null(FindUsernameField(p, log.Add));
        Assert.StartsWith("username: error", Assert.Single(log));

        Assert.Empty(TextFields(new FakeElement("vacio"), 0));
        Assert.Empty(TextFields(new FakeElement("roto") { ChildCountOverride = () => throw new InvalidOperationException() }, 0));
        Assert.Empty(TextFields(new FakeElement("enorme") { ChildCountOverride = () => 501 }, 0));
        var noChildren = new FakeElement("sin-hijos") { ChildrenFail = true };
        noChildren.Add(new FakeElement("t", RoleSystemText));
        Assert.Empty(TextFields(noChildren, 0));
    }

    // ------------------------------------------------------------------ foco y tecleo

    [Fact]
    public void Focus_Win32_AttachesThreads_AndChecksTheFocus()
    {
        var native = new FakeNative { Window = 77, WindowThread = 5 };
        var engine = new DesktopAutofillEngine(null!, null!, native, new FakePopup());
        var field = new FakeElement("campo", RoleSystemText);

        native.FocusResult = 77;
        Assert.True(engine.Focus(field));
        Assert.Equal(["attach 1->5 True", "setfocus 77", "attach 1->5 False"], native.Calls);
        Assert.True(field.Focused);

        native.Calls.Clear();
        native.FocusResult = 12;
        Assert.False(engine.Focus(field));
        Assert.Equal("attach 1->5 False", native.Calls[^1]);

        // Hijo simple: sin SetFocus (no tiene ventana propia), solo accesibilidad.
        native.Calls.Clear();
        native.FocusResult = 77;
        Assert.True(engine.Focus(new FakeElement("simple", RoleSystemText) { IsSimpleChild = true }));
        Assert.DoesNotContain(native.Calls, c => c.StartsWith("setfocus"));

        // SetFocus falla: false, y se suelta la entrada de hilos igual.
        native.Calls.Clear();
        native.SetFocusFails = true;
        Assert.False(engine.Focus(field));
        Assert.Equal("attach 1->5 False", native.Calls[^1]);
    }

    [Fact]
    public void Focus_WithoutWindow_OrSameThread_OnlyAccessibility()
    {
        var native = new FakeNative { WindowFails = true };
        var engine = new DesktopAutofillEngine(null!, null!, native, new FakePopup());
        var field = new FakeElement("campo", RoleSystemText) { TakeFocusFails = true };
        Assert.True(engine.Focus(field));
        Assert.Empty(native.Calls);

        native.WindowFails = false;
        native.Window = 9;
        native.WindowThread = native.CurrentThread;   // del mismo hilo: sin AttachThreadInput
        Assert.True(engine.Focus(field));
        Assert.Equal(["setfocus 9"], native.Calls);
    }

    [Fact]
    public void KeyStrokes_UnicodeDownAndUp()
    {
        Assert.Equal([('a', KeyEventUnicode), ('a', KeyEventUnicode | KeyEventKeyUp), ('ñ', KeyEventUnicode), ('ñ', KeyEventUnicode | KeyEventKeyUp)], KeyStrokes("añ"));
        Assert.Empty(KeyStrokes(""));
    }

    // ------------------------------------------------------------------ la lista

    [Fact]
    public void PopupBounds_BelowTheField_InsideTheScreen()
    {
        var work = new ScreenRect(0, 0, 1920, 1040);
        Assert.Equal(new ScreenRect(100, 222, 300, 26 + 2 * 50 + 52), PopupBounds(new ScreenRect(100, 200, 300, 20), 1, 2, work));
        // Estrecho: 260 como poco; ancho: 420 como mucho; con escala 1,5.
        Assert.Equal(new ScreenRect(10, 32, 390, (int)(128 * 1.5)), PopupBounds(new ScreenRect(10, 10, 50, 20), 1.5, 1, work));
        Assert.Equal(420, PopupBounds(new ScreenRect(0, 0, 900, 20), 1, 0, work).Width);
        // Pegado al borde derecho y abajo: se mete dentro y sube encima del campo.
        var r = PopupBounds(new ScreenRect(1800, 1000, 300, 20), 1, 3, work);
        Assert.Equal(new ScreenRect(1620, 1000 - 228 - 2, 300, 228), r);
        // Sin sitio ni arriba: arriba del todo de la zona de trabajo.
        Assert.Equal(0, PopupBounds(new ScreenRect(0, 50, 300, 20), 1, 8, new ScreenRect(0, 0, 800, 300)).Y);
        // Sin pantalla conocida y escala rara: tal cual, escala 1.
        Assert.Equal(new ScreenRect(5000, 5022, 260, 78), PopupBounds(new ScreenRect(5000, 5000, 10, 20), 0, -1, null));
        Assert.Equal(5320, PopupBounds(new ScreenRect(5000, 5000, 320, 20), 0, 0, null).Right);
    }

    // ------------------------------------------------------------------ el recorrido entero

    private sealed class World
    {
        public World(Sandbox box)
        {
            Box = box;
            Engine = new DesktopAutofillEngine(box.Store, box.Settings, Native, Popup) { StepDelayMs = 0, Log = Logs.Add };
            Form = new FakeElement("form");
            User = Form.Add(new FakeElement("usuario", RoleSystemText, 0, new(100, 100, 200, 20)));
            Password = Form.Add(new FakeElement("clave", RoleSystemText, StateSystemProtected, new(100, 140, 200, 20)) { ContainerElement = Form });
            Native.Elements[(42, -4, 0)] = Password;
            Native.Elements[(42, -4, 1)] = User;
            Native.Processes[42] = "KeePass";
            Native.Titles[42] = "Base - KeePass";
        }

        public Sandbox Box { get; }
        public FakeNative Native { get; } = new() { Window = 42, WindowThread = 5, FocusResult = 42 };
        public FakePopup Popup { get; } = new();
        public DesktopAutofillEngine Engine { get; }
        public FakeElement Form { get; }
        public FakeElement User { get; }
        public FakeElement Password { get; }
        public List<string> Logs { get; } = [];

        public async Task<Credential> UnlockWith(params Credential[] entries)
        {
            await Box.Store.CreateAsync(Master);
            Box.Store.Data!.Entries.AddRange(entries);
            return entries.FirstOrDefault()!;
        }

        public void Focus(int hwnd, int obj, int child)
        {
            Engine.OnWinEvent(EventObjectFocus, hwnd, obj, child);
            Engine.ProcessPending();
        }
    }

    [Fact]
    public async Task PasswordField_ShowsTheList_PickFills_AndLearns()
    {
        using var box = Sandbox.Create();
        var w = new World(box);
        var entry = await w.UnlockWith(Entry("KeePass", user: "ana", password: "s3"), Entry("Otra cosa"));

        w.Focus(42, -4, 0);
        var shown = Assert.Single(w.Popup.Shown);
        Assert.Equal(["KeePass"], shown.Entries.Select(e => e.Title));
        Assert.Equal(new ScreenRect(100, 140, 200, 20), shown.Field);
        Assert.Contains("password field in keepass 'Base - KeePass'", w.Logs);

        // Elegir: se esconde la lista y, en el hilo de trabajo, se teclea usuario y contraseña.
        shown.Pick(entry);
        Assert.Equal(1, w.Popup.Hides);
        w.Engine.ProcessPending();
        Assert.Equal("ana", w.Native.Typed[0]);
        Assert.Equal("s3", w.Native.Typed[1]);
        Assert.True(w.User.Focused);
        Assert.True(w.Password.Focused);
        Assert.Equal("keepass", entry.Fields.Single(f => f.Name == "windows").Value);

        // La segunda vez sale primera y no se apunta otra vez.
        var modified = entry.ModifiedAt;
        w.Focus(42, -4, 0);
        w.Popup.Shown[^1].Pick(entry);
        w.Engine.ProcessPending();
        Assert.Single(entry.Fields);
        Assert.Equal(modified, entry.ModifiedAt);
    }

    [Fact]
    public async Task Fill_WithoutUsername_OnlyThePassword_EvenIfTypingFails()
    {
        using var box = Sandbox.Create();
        var w = new World(box);
        var entry = await w.UnlockWith(Entry("KeePass", user: "", password: "solo"));
        w.Engine.Fill(entry, w.Password, "keepass");
        Assert.Equal(["solo"], w.Native.Typed);
        Assert.False(w.User.Focused);

        // Si la otra aplicacion se cierra a medias, la entrada aprende igual.
        var other = Entry("Otra", user: "ana", password: "x");
        w.Native.SendFails = true;
        w.Engine.Fill(other, w.Password, "otra");
        Assert.Equal("otra", other.Fields.Single().Value);

        // Sin campo de usuario a la vista: solo la contraseña.
        w.Native.SendFails = false;
        w.Native.Typed.Clear();
        w.Engine.Fill(Entry("Suelta", user: "ana", password: "x"), new FakeElement("huerfano", RoleSystemText, StateSystemProtected, new(0, 0, 1, 1)), "x");
        Assert.Equal(["x"], w.Native.Typed);
    }

    [Fact]
    public async Task Focus_ElsewhereHides_AndNothingWhenItDoesNotApply()
    {
        using var box = Sandbox.Create();
        var w = new World(box);

        // Boveda cerrada: se esconde y no se mira nada.
        w.Engine.OnWinEvent(EventObjectFocus, 42, -4, 0);
        Assert.Equal(1, w.Popup.Hides);
        await w.UnlockWith(Entry("KeePass"));

        // Otros avisos (primer plano, minimizar) o sin ventana: esconder.
        w.Engine.OnWinEvent(EventSystemForeground, 42, 0, 0);
        w.Engine.OnWinEvent(EventObjectFocus, 0, 0, 0);
        Assert.Equal(3, w.Popup.Hides);

        // Navegadores (van por la extension) y procesos que no se pueden leer.
        w.Native.Processes[43] = "MSEdge";
        w.Engine.OnWinEvent(EventObjectFocus, 43, -4, 0);
        w.Engine.OnWinEvent(EventObjectFocus, 44, -4, 0);
        Assert.Equal(5, w.Popup.Hides);
        w.Engine.ProcessPending();
        Assert.Empty(w.Popup.Shown);

        // El campo de usuario no es de contraseña; el elemento no se puede leer; un programa sin entradas.
        w.Focus(42, -4, 1);
        w.Native.Elements.Remove((42, -4, 0));
        w.Focus(42, -4, 0);
        w.Native.Elements[(42, -4, 0)] = w.Password;
        w.Native.Processes[42] = "notepad";
        w.Native.Titles[42] = "Bloc de notas";
        w.Focus(42, -4, 0);
        Assert.Empty(w.Popup.Shown);
        Assert.Contains("candidates=0", w.Logs);
        Assert.Equal(8, w.Popup.Hides);

        // Desactivado en Ajustes: ni se mira.
        box.Settings.DesktopAutofill = false;
        w.Engine.OnWinEvent(EventObjectFocus, 42, 0, 0);
        Assert.Equal(8, w.Popup.Hides);
    }

    [Fact]
    public async Task Errors_HideTheList()
    {
        using var box = Sandbox.Create();
        var w = new World(box);
        await w.UnlockWith(Entry("KeePass"));

        w.Native.ProcessFails = true;
        w.Engine.OnWinEvent(EventObjectFocus, 42, -4, 0);
        Assert.Contains(w.Logs, l => l.StartsWith("error: "));
        Assert.Equal(1, w.Popup.Hides);

        // La otra aplicacion se va mientras se mira donde esta el campo.
        w.Native.ProcessFails = false;
        w.Password.Rect = null;
        w.Focus(42, -4, 0);
        Assert.Contains(w.Logs, l => l.StartsWith("inspect error: "));
        Assert.Equal(2, w.Popup.Hides);

        // La lista no se deja esconder: no pasa nada.
        w.Popup.HideFails = true;
        w.Engine.Hide();

        // La cola de la interfaz ya esta cerrada.
        w.Native.UiClosed = true;
        w.Focus(42, -4, 1);
        Assert.Contains("ui queue closed", w.Logs);

        // Rellenar que lanza (en el hilo de trabajo) no tumba el hilo: se apunta y sigue.
        w.Popup.HideFails = false;
        w.Native.UiClosed = false;
        w.Password.Rect = new(100, 140, 200, 20);
        w.Focus(42, -4, 0);
        w.Popup.Shown[^1].Pick(w.Box.Store.Data!.Entries[0]);
        w.Native.PostThrows = true;
        w.Engine.ProcessPending();
        Assert.Contains(w.Logs, l => l.StartsWith("fill error: "));
        Assert.Equal(["ana", "p"], w.Native.Typed);
    }

    [Fact]
    public async Task Worker_RunsInTheBackground_AndStops()
    {
        using var box = Sandbox.Create();
        var w = new World(box);
        await w.UnlockWith(Entry("KeePass"));
        w.Engine.Start();
        w.Engine.Start();   // dos veces no arranca dos hilos

        w.Engine.OnWinEvent(EventObjectFocus, 42, -4, 0);
        Assert.True(SpinWait.SpinUntil(() => w.Popup.ShownCount > 0, TimeSpan.FromSeconds(10)));

        // Al cerrar la boveda se esconde la lista.
        var hides = w.Popup.Hides;
        box.Store.Lock();
        Assert.Equal(hides + 1, w.Popup.Hides);

        w.Engine.Dispose();
        w.Engine.Dispose();
        Assert.True(w.Engine.IsDisposed);
        Assert.True(w.Engine.Join(TimeSpan.FromSeconds(10)));
        w.Engine.OnWinEvent(EventObjectFocus, 42, -4, 0);
        Assert.True(new DesktopAutofillEngine(box.Store, box.Settings, w.Native, w.Popup).Join(TimeSpan.Zero));
    }

    // ------------------------------------------------------------------ dobles

    internal sealed class FakeElement(string name, int role = -1, int state = 0, ScreenRect? rect = null) : IAccElement
    {
        private readonly List<FakeElement> _children = [];
        public string Name => name;
        public int Role => role;
        public int State => state;
        public ScreenRect? Rect { get; set; } = rect;
        public bool IsSimpleChild { get; init; }
        public FakeElement? ContainerElement { get; set; }
        public FakeElement? ParentElement { get; set; }
        public Func<int>? ChildCountOverride { get; init; }
        public bool ChildrenFail { get; init; }
        public bool TakeFocusFails { get; init; }
        public bool Focused { get; private set; }

        public FakeElement Add(FakeElement child)
        {
            _children.Add(child);
            return child;
        }

        public ScreenRect Location() => Rect ?? throw new InvalidOperationException("ya no esta");
        public IAccElement? Container() => ContainerElement;
        public IAccElement? Parent() => ParentElement;
        public int ChildCount() => ChildCountOverride?.Invoke() ?? _children.Count;
        public IReadOnlyList<IAccElement>? Children(int count) => ChildrenFail ? null : _children.Take(count).ToList();

        public void TakeFocus()
        {
            Focused = true;
            if (TakeFocusFails) throw new InvalidOperationException();
        }

        public override string ToString() => name;
    }

    internal sealed class FakeNative : IDesktopNative
    {
        public Dictionary<(nint, int, int), FakeElement> Elements { get; } = [];
        public Dictionary<nint, string> Processes { get; } = [];
        public Dictionary<nint, string> Titles { get; } = [];
        public List<string> Calls { get; } = [];
        public List<string> Typed { get; } = [];
        public nint Window { get; set; }
        public uint WindowThread { get; set; }
        public nint FocusResult { get; set; }
        public bool WindowFails, SetFocusFails, SendFails, ProcessFails, UiClosed, PostThrows;

        public string ProcessName(nint hwnd) => ProcessFails ? throw new InvalidOperationException() : Processes.GetValueOrDefault(hwnd, "");
        public string WindowTitle(nint hwnd) => Titles.GetValueOrDefault(hwnd, "");
        public IAccElement? FromEvent(nint hwnd, int idObject, int idChild) => Elements.GetValueOrDefault((hwnd, idObject, idChild));
        public nint WindowOf(IAccElement element) => WindowFails ? throw new InvalidOperationException() : Window;
        public uint ThreadOf(nint hwnd) => hwnd == 0 ? 0 : WindowThread;
        public uint CurrentThread => 1;

        public bool AttachThreadInput(uint attach, uint to, bool on)
        {
            Calls.Add($"attach {attach}->{to} {on}");
            return true;
        }

        public void SetFocus(nint hwnd)
        {
            if (SetFocusFails) throw new InvalidOperationException();
            Calls.Add($"setfocus {hwnd}");
        }

        public nint GetFocus() => FocusResult;

        public void SendKeys(IReadOnlyList<(ushort Scan, uint Flags)> keys)
        {
            if (SendFails) throw new InvalidOperationException();
            Typed.Add(new string(keys.Where(k => (k.Flags & KeyEventKeyUp) == 0).Select(k => (char)k.Scan).ToArray()));
        }

        public void Sleep(int milliseconds) { }

        public bool PostToUi(Action action)
        {
            if (PostThrows) throw new InvalidOperationException("sin interfaz");
            if (UiClosed) return false;
            action();
            return true;
        }
    }

    internal sealed class FakePopup : IAutofillPopup
    {
        private int _shown;
        public List<(IReadOnlyList<Credential> Entries, ScreenRect Field, Action<Credential> Pick)> Shown { get; } = [];
        public int ShownCount => Volatile.Read(ref _shown);
        public int Hides;
        public bool HideFails;

        public void Show(IReadOnlyList<Credential> entries, ScreenRect field, Action<Credential> pick)
        {
            lock (Shown) Shown.Add((entries, field, pick));
            Interlocked.Increment(ref _shown);
        }

        public void Hide()
        {
            Interlocked.Increment(ref Hides);
            if (HideFails) throw new InvalidOperationException();
        }
    }
}
