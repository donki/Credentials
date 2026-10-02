using System.Net;
using Credentials.Platforms.Windows;

namespace Credentials.Tests;

/// <summary>El envoltorio de MSAA (con objetos de accesibilidad de mentira) y el servidor local de la entrada con el navegador (en 127.0.0.1).</summary>
public class MsaaAndLoopbackTests
{
    /// <summary>Un objeto de accesibilidad de mentira: rol y estado por id de hijo, posicion, padre e hijos.</summary>
    private sealed class FakeAcc : IAccessible
    {
        public Dictionary<int, object> Roles { get; } = [];
        public Dictionary<int, object> States { get; } = [];
        public object? ParentObject { get; set; }
        public object[] Kids { get; set; } = [];
        public List<string> Selected { get; } = [];
        public bool Broken { get; set; }

        private static int Id(object child) => child is int i ? i : -99;

        public object get_accRole(object childId) => Broken ? throw new InvalidOperationException() : Roles.GetValueOrDefault(Id(childId), "texto raro");
        public object get_accState(object childId) => Broken ? throw new InvalidOperationException() : States.GetValueOrDefault(Id(childId), 0);
        public object? get_accParent() => ParentObject;
        public int get_accChildCount() => Kids.Length;
        public void accSelect(int flagsSelect, object childId) => Selected.Add($"{flagsSelect}:{childId}");

        public void accLocation(out int left, out int top, out int width, out int height, object childId)
        {
            var i = Id(childId);
            (left, top, width, height) = (i * 10, i * 20, 100, 25);
        }

        public object? get_accChild(object childId) => throw new NotSupportedException();
        public string? get_accName(object childId) => throw new NotSupportedException();
        public string? get_accValue(object childId) => throw new NotSupportedException();
        public string? get_accDescription(object childId) => throw new NotSupportedException();
        public string? get_accHelp(object childId) => throw new NotSupportedException();
        public int get_accHelpTopic(out string? helpFile, object childId) => throw new NotSupportedException();
        public string? get_accKeyboardShortcut(object childId) => throw new NotSupportedException();
        public object? get_accFocus() => throw new NotSupportedException();
        public object? get_accSelection() => throw new NotSupportedException();
        public string? get_accDefaultAction(object childId) => throw new NotSupportedException();
        public object? accNavigate(int navDir, object start) => throw new NotSupportedException();
        public object? accHitTest(int left, int top) => throw new NotSupportedException();
        public void accDoDefaultAction(object childId) => throw new NotSupportedException();
        public void set_accName(object childId, string name) => throw new NotSupportedException();
        public void set_accValue(object childId, string value) => throw new NotSupportedException();
    }

    private static int ReadKids(IAccessible parent, int start, int count, object[] children, out int obtained)
    {
        var kids = ((FakeAcc)parent).Kids;
        if (kids.Length == 1 && kids[0] is "falla")
        {
            obtained = 0;
            return unchecked((int)0x80004005);
        }
        obtained = Math.Min(count, kids.Length);
        Array.Copy(kids, start, children, 0, obtained);
        return 0;
    }

    [Fact]
    public void Element_RoleStateAndLocation()
    {
        var acc = new FakeAcc();
        acc.Roles[0] = 0x2A;
        acc.States[0] = unchecked((uint)0x20000000);
        acc.Roles[3] = 0x9u;
        var whole = new MsaaElement(acc, 0, ReadKids);
        Assert.Equal(0x2A, whole.Role);
        Assert.Equal(0x20000000, whole.State);
        Assert.False(whole.IsSimpleChild);
        Assert.True(DesktopAutofillEngine.IsPasswordField(whole));
        Assert.Same(acc, whole.Acc);

        var simple = new MsaaElement(acc, 3, ReadKids);
        Assert.True(simple.IsSimpleChild);
        Assert.Equal(0x9, simple.Role);
        Assert.Equal(new ScreenRect(30, 60, 100, 25), simple.Location());
        Assert.Equal(-1, new MsaaElement(acc, 5, ReadKids).Role);   // un VARIANT que no es numero
        Assert.Equal(3, simple.Child);
        Assert.False(new MsaaElement(acc, "x", ReadKids).IsSimpleChild);

        acc.Broken = true;
        Assert.Equal(-1, whole.Role);
        Assert.Equal(0, whole.State);

        simple.TakeFocus();
        Assert.Equal(["1:3"], acc.Selected);
    }

    [Fact]
    public void Element_ParentContainerAndChildren()
    {
        var grand = new FakeAcc();
        var parent = new FakeAcc { ParentObject = grand };
        var childObject = new FakeAcc();
        parent.Kids = [childObject, 4, 5];
        var whole = new MsaaElement(parent, 0, ReadKids);

        Assert.Same(grand, ((MsaaElement)whole.Parent()!).Acc);
        Assert.Same(grand, ((MsaaElement)whole.Container()!).Acc);
        Assert.Null(new MsaaElement(grand, 0, ReadKids).Parent());
        // Un hijo simple vive en su propio objeto.
        var simple = (MsaaElement)new MsaaElement(parent, 4, ReadKids).Container()!;
        Assert.Same(parent, simple.Acc);
        Assert.Equal(0, simple.Child);

        Assert.Equal(3, whole.ChildCount());
        var kids = whole.Children(3)!.Cast<MsaaElement>().ToList();
        Assert.Same(childObject, kids[0].Acc);
        Assert.Equal(0, kids[0].Child);
        Assert.False(kids[0].IsSimpleChild);
        Assert.Same(parent, kids[1].Acc);
        Assert.Equal(4, kids[1].Child);
        Assert.Equal(5, kids[2].Child);
        Assert.Equal(2, whole.Children(2)!.Count);

        parent.Kids = ["falla"];
        Assert.Null(whole.Children(1));
    }

    // ------------------------------------------------------------------ servidor local

    [Fact]
    public async Task Loopback_RealListener_OnlyTheCallbackPath()
    {
        var port = HttpLoopbackListener.FreePort();
        Assert.InRange(port, 1, 65535);
        var callback = new Uri($"http://127.0.0.1:{port}/auth/");
        using var listener = new HttpLoopbackListener(callback);
        using var http = new HttpClient();
        Task<HttpResponseMessage>? favicon = null;
        var back = new TaskCompletionSource<HttpResponseMessage>();

        var url = await LoopbackOAuth.AuthenticateAsync(listener, callback, () =>
        {
            favicon = http.GetAsync($"http://127.0.0.1:{port}/auth/favicon.ico/x");
            _ = Task.Run(async () =>
            {
                await favicon;
                back.SetResult(await http.GetAsync($"http://127.0.0.1:{port}/auth/?code=c0d1go&state=s"));
            });
        }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("?code=c0d1go&state=s", url.Query);
        Assert.Equal(HttpStatusCode.NotFound, (await favicon!).StatusCode);
        var page = await back.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal("text/html; charset=utf-8", page.Content.Headers.ContentType!.ToString());
        Assert.Equal(LoopbackOAuth.Page(true), await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Loopback_RealListener_ErrorAndCancel()
    {
        var port = HttpLoopbackListener.FreePort();
        var callback = new Uri($"http://127.0.0.1:{port}/auth/");
        using var http = new HttpClient();
        Task<string>? refused = null;
        using (var listener = new HttpLoopbackListener(callback))
            await LoopbackOAuth.AuthenticateAsync(listener, callback, () => refused = http.GetStringAsync($"http://127.0.0.1:{port}/auth/?error=access_denied"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(LoopbackOAuth.Page(false), await refused!);

        // Nadie vuelve: al vencer el tiempo se cancela (Abort tumba la espera).
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var idleCallback = new Uri($"http://127.0.0.1:{HttpLoopbackListener.FreePort()}/auth/");
        using var idle = new HttpLoopbackListener(idleCallback);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopbackOAuth.AuthenticateAsync(idle, idleCallback, () => { }, cts.Token));
    }
}
