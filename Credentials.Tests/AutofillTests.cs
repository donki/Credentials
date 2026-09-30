using Credentials.Models;
using Credentials.Services;

namespace Credentials.Tests;

public class AutofillTests
{
    private static Credential Login(string title, string url, string user = "ana", string password = "p", bool fav = false, EntryKind kind = EntryKind.Login) =>
        new() { Title = title, Url = url, Username = user, Password = password, Favorite = fav, Kind = kind };

    [Theory]
    [InlineData("example.com", "example.com", true)]
    [InlineData("login.example.com", "example.com", true)]
    [InlineData("example.com", "login.example.com", true)]
    [InlineData("notexample.com", "example.com", false)]
    [InlineData("example.com.evil.net", "example.com", false)]
    [InlineData("example.co", "example.com", false)]
    public void SameSite_ComparesWholeLabels(string a, string b, bool expected) => Assert.Equal(expected, AutofillLogic.SameSite(a, b));

    [Fact]
    public void Match_ByDomain_FavoritesFirst_SkipsDeletedNotesAndEmpty()
    {
        var entries = new List<Credential>
        {
            Login("Zeta", "https://accounts.example.com"),
            Login("Alfa", "example.com"),
            Login("Favorita", "https://www.example.com/login", fav: true),
            Login("Borrada", "example.com") .With(e => e.Deleted = true),
            Login("Nota", "example.com", kind: EntryKind.Note),
            Login("Vacia", "example.com", user: "", password: ""),
            Login("Parecida", "https://notexample.com"),
        };

        Assert.Equal(["Favorita", "Alfa", "Zeta"], AutofillLogic.Match(entries, "EXAMPLE.com", null).Select(e => e.Title));
        Assert.Empty(AutofillLogic.Match(entries, "nada.org", null));
        Assert.Empty(AutofillLogic.Match(entries, null, null));
    }

    [Fact]
    public void Match_ByAndroidPackage()
    {
        var byField = Login("Mi banco", "", kind: EntryKind.App);
        byField.Fields.Add(new CustomField { Name = "Android", Value = "es.banco.movil" });
        var entries = new List<Credential>
        {
            Login("Twitter", "https://x.com"),
            Login("Otra", "https://twitter.com"),
            byField,
            Login("Nada", "https://ejemplo.org"),
        };

        Assert.Equal(["Otra", "Twitter"], AutofillLogic.Match(entries, null, "com.twitter.android").Select(e => e.Title));
        Assert.Equal(["Mi banco"], AutofillLogic.Match(entries, null, "es.banco.movil").Select(e => e.Title));

        // Con dominio que no casa, se prueba con el paquete.
        Assert.Equal(["Otra", "Twitter"], AutofillLogic.Match(entries, "desconocido.net", "com.twitter.android").Select(e => e.Title));
        // Las partes genericas del paquete (com, android, app…) no cuentan.
        Assert.Empty(AutofillLogic.Match(entries, null, "com.android.app"));
    }

    [Fact]
    public void Search_TitleUserUrlTags()
    {
        var tagged = Login("Correo", "mail.example.com");
        tagged.Tags.Add("Trabajo");
        var entries = new List<Credential>
        {
            tagged,
            Login("Banco", "banco.es", user: "ÁNGELA"),
            Login("Nota", "", kind: EntryKind.Note),
            Login("Borrada", "x").With(e => e.Deleted = true),
            Login("Alfa", "a.com", fav: true),
        };

        Assert.Equal(["Alfa", "Banco", "Correo"], AutofillLogic.Search(entries, "  ").Select(e => e.Title));
        Assert.Equal(["Correo"], AutofillLogic.Search(entries, "trabajo").Select(e => e.Title));
        Assert.Equal(["Banco"], AutofillLogic.Search(entries, "ángela").Select(e => e.Title));
        Assert.Equal(["Correo"], AutofillLogic.Search(entries, "MAIL.").Select(e => e.Title));
        Assert.Empty(AutofillLogic.Search(entries, "nota"));
    }

    [Fact]
    public async Task Upsert_NewWebEntry_ThenPasswordChangeKeepsHistory()
    {
        using var box = Sandbox.Create();
        await box.Store.CreateAsync("maestra");
        var entries = box.Store.Data!.Entries;

        Assert.True(await AutofillLogic.UpsertAsync(box.Store, "github.com", null, null, "ana", "uno"));
        var e = Assert.Single(entries);
        Assert.Equal((EntryKind.Login, "github.com", "https://github.com"), (e.Kind, e.Title, e.Url));

        // Misma contraseña: nada que guardar.
        Assert.False(await AutofillLogic.UpsertAsync(box.Store, "github.com", null, null, "ANA", "uno"));
        Assert.False(await AutofillLogic.UpsertAsync(box.Store, "github.com", null, null, "ana", ""));

        Assert.True(await AutofillLogic.UpsertAsync(box.Store, "gist.github.com", null, null, "ana", "dos"));
        Assert.Single(entries);
        Assert.Equal("dos", e.Password);
        Assert.Equal("uno", e.History.Single().Password);

        // Otro usuario del mismo sitio: entrada nueva.
        Assert.True(await AutofillLogic.UpsertAsync(box.Store, "github.com", null, null, "bea", "tres"));
        Assert.Equal(2, entries.Count);

        // Y todo quedo guardado en el fichero cifrado.
        box.Store.Lock();
        await box.Store.UnlockAsync("maestra");
        Assert.Equal(2, box.Store.Data!.Entries.Count);
    }

    /// <summary>
    /// Fallo encontrado por esta prueba (2026-09-30): una entrada guardada sin usuario no se
    /// reconocia al volver a guardar con usuario, y quedaba duplicada en vez de completarse.
    /// </summary>
    [Fact]
    public async Task Upsert_App_AndFillsMissingUsername()
    {
        using var box = Sandbox.Create();
        await box.Store.CreateAsync("maestra");

        Assert.True(await AutofillLogic.UpsertAsync(box.Store, null, "com.spotify.music", "Spotify", "", "p1"));
        var e = box.Store.Data!.Entries.Single();
        Assert.Equal((EntryKind.App, "Spotify", ""), (e.Kind, e.Title, e.Url));
        Assert.Equal("com.spotify.music", e.Fields.Single(f => f.Name == "android").Value);

        Assert.True(await AutofillLogic.UpsertAsync(box.Store, null, "com.spotify.music", "Spotify", "ana", "p2"));
        Assert.Single(box.Store.Data.Entries);
        Assert.Equal("ana", e.Username);
        Assert.Equal("p2", e.Password);

        Assert.True(await AutofillLogic.UpsertAsync(box.Store, null, "org.sin.etiqueta", null, "u", "p"));
        Assert.Contains(box.Store.Data.Entries, x => x.Title == "org.sin.etiqueta");
        Assert.True(await AutofillLogic.UpsertAsync(box.Store, null, null, null, "u", "p"));
        Assert.Contains(box.Store.Data.Entries, x => x.Title == "App");
    }

    [Fact]
    public async Task Upsert_EmptyOldPassword_LeavesNoHistory()
    {
        using var box = Sandbox.Create();
        await box.Store.CreateAsync("maestra");
        box.Store.Data!.Entries.Add(Login("Sin clave", "https://a.com", user: "ana", password: ""));

        Assert.True(await AutofillLogic.UpsertAsync(box.Store, "a.com", null, null, "ana", "nueva"));
        Assert.Empty(box.Store.Data.Entries.Single().History);
    }
}

internal static class TestExtensions
{
    public static T With<T>(this T item, Action<T> change)
    {
        change(item);
        return item;
    }
}
