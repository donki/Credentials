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
    public static async Task ApplyAsync(string? master, bool demo, string? page, string? lang)
    {
        if (!string.IsNullOrEmpty(lang))
        {
            ServiceHelper.GetRequiredService<ISettingsService>().Language = lang;
            ServiceHelper.GetRequiredService<ILocalizationService>().SetLanguage(lang);
        }
        if (string.IsNullOrEmpty(master))
            return;
        var store = ServiceHelper.GetRequiredService<VaultStore>();
        try
        {
            if (store.Exists) await store.UnlockAsync(master); else await store.CreateAsync(master);
            if (demo && store.Data!.Entries.Count == 0)
            {
                store.Data.Entries.AddRange(
                [
                    new Credential { Kind = EntryKind.Login, Title = "GitHub", Username = "ana@example.com", Password = "correct-horse-battery-staple", Url = "https://github.com", Folder = "Trabajo", Tags = ["dev"], Favorite = true, Totp = "otpauth://totp/GitHub:ana@example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub" },
                    new Credential { Kind = EntryKind.Login, Title = "Banco Ejemplo", Username = "12345678A", Password = "Tr0ub4dor&3", Url = "https://banco.example", Folder = "Personal", Tags = ["dinero"] },
                    new Credential { Kind = EntryKind.App, Title = "Wi-Fi de casa", Username = "MiRed", Password = "casa-2026-segura", Folder = "Personal" },
                    new Credential { Kind = EntryKind.Totp, Title = "Microsoft", Username = "ana@example.com", Totp = "otpauth://totp/Microsoft:ana@example.com?secret=GEZDGNBVGY3TQOJQ&issuer=Microsoft" },
                    new Credential { Kind = EntryKind.Note, Title = "Licencia del NAS", Notes = "XXXX-YYYY-ZZZZ-1234 · Comprada el 3 de marzo.", Folder = "Trabajo" },
                    new Credential { Kind = EntryKind.Login, Title = "Correo", Username = "ana@example.com", Password = "Otra-Clave-Larga-42", Url = "https://mail.example.com", Folder = "Personal" },
                ]);
                await store.SaveAsync(upload: false);
            }
            if (!string.IsNullOrEmpty(page))
            {
                await Task.Delay(500);
                if (page == "settings")
                    await Shell.Current.GoToAsync("//SettingsPage");
                else if (page == "tutorial")
                    await Shell.Current.GoToAsync("//TutorialPage");
                else if (page == "about")
                    await Shell.Current.GoToAsync("//AboutPage");
                else if (page.StartsWith("entry:") && store.Data!.Entries.FirstOrDefault(x => x.Title == page[6..]) is { } entry)
                    await Shell.Current.Navigation.PushAsync(new Pages.EntryPage(entry, isNew: false));
                else if (page == "lock")
                    store.Lock();
            }
        }
        catch (Exception) { }
    }
}
#endif
