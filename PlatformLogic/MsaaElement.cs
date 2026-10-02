using System.Runtime.InteropServices;

namespace Credentials.Platforms.Windows;

/// <summary>La interfaz de accesibilidad de Windows (MSAA, oleacc). En las pruebas se implementa con objetos de mentira.</summary>
[ComImport, Guid("618736E0-3C3D-11CF-810C-00AA00389B71"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IAccessible
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

/// <summary>AccessibleChildren de oleacc: los hijos de un objeto (IAccessible o ids de hijo simple). 0 si va bien.</summary>
public delegate int MsaaChildrenReader(IAccessible parent, int start, int count, object[] children, out int obtained);

/// <summary>
/// Un elemento MSAA (un IAccessible y su id de hijo) visto como IAccElement para DesktopAutofillEngine.
/// Si leer el rol o el estado falla (la otra aplicacion esta ocupada o se ha ido), -1 y 0.
/// </summary>
public sealed class MsaaElement(IAccessible acc, object child, MsaaChildrenReader readChildren) : IAccElement
{
    public const int SelFlagTakeFocus = 0x1;

    public IAccessible Acc => acc;
    public object Child => child;
    public int Role => Number(() => acc.get_accRole(child), -1);
    public int State => Number(() => acc.get_accState(child), 0);
    public bool IsSimpleChild => child is int id && id != 0;

    public ScreenRect Location()
    {
        acc.accLocation(out var left, out var top, out var width, out var height, child);
        return new ScreenRect(left, top, width, height);
    }

    public IAccElement? Container() => IsSimpleChild ? new MsaaElement(acc, 0, readChildren) : Parent();
    public IAccElement? Parent() => acc.get_accParent() is IAccessible p ? new MsaaElement(p, 0, readChildren) : null;
    public int ChildCount() => acc.get_accChildCount();
    public void TakeFocus() => acc.accSelect(SelFlagTakeFocus, child);

    public IReadOnlyList<IAccElement>? Children(int count)
    {
        var children = new object[count];
        if (readChildren(acc, 0, count, children, out var got) != 0)
            return null;
        return children.Take(got).Select(s => s is IAccessible a ? new MsaaElement(a, 0, readChildren) : new MsaaElement(acc, s, readChildren)).ToList();
    }

    /// <summary>Los VARIANT de rol y estado llegan como int o uint segun quien los de.</summary>
    private static int Number(Func<object> read, int fallback)
    {
        try { return read() switch { int i => i, uint u => unchecked((int)u), _ => fallback }; }
        catch (Exception) { return fallback; }
    }
}
