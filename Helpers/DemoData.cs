#if DEBUG
using Credentials.Models;
using Credentials.Services;

namespace Credentials.Helpers;

/// <summary>
/// SOLO en Debug (el fichero entero desaparece en Release): abre o crea la boveda con una clave dada
/// y siembra entradas inventadas, para probar y capturar pantallas sin teclear. En Windows llega por
/// la linea de ordenes («--master clave --demo --page settings»); en Android, por extras del intent
/// («am start … --es master clave --ez demo true --es page settings --es lang es»).
/// </summary>
public static class DemoData
{
    /// <summary>El respiro antes de abrir la pagina pedida (que el Shell este listo). Las pruebas lo acortan.</summary>
    internal static TimeSpan PageDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public static async Task ApplyAsync(string? master, bool demo, string? page, string? lang)
    {
        if (!string.IsNullOrEmpty(lang))
        {
            ServiceHelper.GetRequiredService<ISettingsService>().Language = lang;
            ServiceHelper.GetRequiredService<ILocalizationService>().SetLanguage(lang);
        }
        if (demo)
            SocShared.AuthorNotes.Hidden = true;   // capturas de tienda: sin el botón de notas de autor
        if (string.IsNullOrEmpty(master))
            return;
        var store = ServiceHelper.GetRequiredService<VaultStore>();
        var navigation = ServiceHelper.GetRequiredService<INavigationService>();
        try
        {
            if (store.Exists) await store.UnlockAsync(master); else await store.CreateAsync(master);
            if (demo && store.Data!.Entries.Count == 0)
            {
                store.Data.Entries.AddRange(Seed(ServiceHelper.GetRequiredService<ILocalizationService>().CurrentLanguage == "en"));
                await store.SaveAsync(upload: false);
            }
            if (!string.IsNullOrEmpty(page))
            {
                await Task.Delay(PageDelay);
                if (page == "settings")
                    await navigation.GoToAsync("//SettingsPage");
                else if (page == "tutorial")
                    await navigation.GoToAsync("//TutorialPage");
                else if (page == "about")
                    await navigation.GoToAsync("//AboutPage");
                else if (page.StartsWith("entry:") && store.Data!.Entries.FirstOrDefault(x => x.Title == page[6..]) is { } entry)
                    await navigation.PushAsync(new Pages.EntryPage(entry, isNew: false));
                else if (page == "lock")
                    store.Lock();
            }
        }
        catch (Exception) { }
    }
    /// <summary>
    /// Entradas inventadas en el idioma de la app (sale en las capturas de las tiendas). Nada de
    /// marcas ni servicios reales (General 1.7): nombres genericos y dominios example.
    /// </summary>
    internal static List<Credential> Seed(bool en)
    {
        string T(string es, string english) => en ? english : es;
        return
        [
            new Credential { Kind = EntryKind.Login, Title = T("Repositorio", "Code repository"), Username = "ana@example.com", Password = "correct-horse-battery-staple", Url = "https://code.example.com", Folder = T("Trabajo", "Work"), Tags = ["dev"], Favorite = true, Totp = "otpauth://totp/Repo:ana@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Repo" },
            new Credential { Kind = EntryKind.Login, Title = T("Banco Ejemplo", "Example Bank"), Username = "12345678A", Password = "Tr0ub4dor&3", Url = "https://bank.example", Folder = "Personal", Tags = [T("dinero", "money")] },
            new Credential { Kind = EntryKind.App, Title = T("Wi-Fi de casa", "Home Wi-Fi"), Username = T("MiRed", "MyNetwork"), Password = "casa-2026-segura", Folder = "Personal" },
            new Credential { Kind = EntryKind.Totp, Title = T("Portal de la empresa", "Company portal"), Username = "ana@example.com", Totp = "otpauth://totp/Portal:ana@example.com?secret=GEZDGNBVGY3TQOJQ&issuer=Portal" },
            new Credential { Kind = EntryKind.Note, Title = T("Licencia del NAS", "NAS licence"), Notes = T("XXXX-YYYY-ZZZZ-1234 · Comprada el 3 de marzo.", "XXXX-YYYY-ZZZZ-1234 · Bought on 3 March."), Folder = T("Trabajo", "Work") },
            new Credential { Kind = EntryKind.Login, Title = T("Correo", "Email"), Username = "ana@example.com", Password = "Otra-Clave-Larga-42", Url = "https://mail.example.com", Folder = "Personal" },
        ];
    }
}
#endif