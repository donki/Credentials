using Credentials.Models;

namespace Credentials.Services;

/// <summary>
/// Lo comun al autocompletar de Android y a las extensiones de navegador: que entradas casan con un
/// sitio (dominio) o una app (paquete), y como se guarda lo que el usuario acaba de escribir.
/// </summary>
public static class AutofillLogic
{
    /// <summary>Entradas que casan con el dominio (web) o con el paquete (app Android).</summary>
    public static List<Credential> Match(IEnumerable<Credential> entries, string? domain, string? package)
    {
        var live = entries.Where(e => !e.Deleted && e.Kind is EntryKind.Login or EntryKind.App && (e.Username.Length > 0 || e.Password.Length > 0)).ToList();
        if (!string.IsNullOrEmpty(domain))
        {
            var d = domain.ToLowerInvariant();
            var byHost = live.Where(e => e.Host.Length > 0 && SameSite(d, e.Host.ToLowerInvariant())).ToList();
            if (byHost.Count > 0)
                return byHost.OrderByDescending(e => e.Favorite).ThenBy(e => e.Title).ToList();
        }
        if (!string.IsNullOrEmpty(package))
        {
            // com.twitter.android → «twitter»: se busca en la URL, el titulo y un campo extra «android».
            var parts = package.Split('.').Where(p => p.Length > 3 && p is not ("com" or "org" or "net" or "android" or "app" or "mobile")).ToList();
            var byApp = live.Where(e =>
                e.Fields.Any(f => f.Name.Equals("android", StringComparison.OrdinalIgnoreCase) && f.Value.Equals(package, StringComparison.OrdinalIgnoreCase)) ||
                parts.Any(p => e.Host.Contains(p, StringComparison.OrdinalIgnoreCase) || e.Title.Contains(p, StringComparison.OrdinalIgnoreCase))).ToList();
            return byApp.OrderByDescending(e => e.Favorite).ThenBy(e => e.Title).ToList();
        }
        return [];
    }

    /// <summary>
    /// «login.example.com» casa con una entrada de «example.com» y al reves; «notexample.com» no.
    /// Se compara por etiquetas completas, no por sufijo de texto.
    /// </summary>
    public static bool SameSite(string a, string b)
    {
        if (a == b) return true;
        return a.EndsWith("." + b, StringComparison.Ordinal) || b.EndsWith("." + a, StringComparison.Ordinal);
    }

    /// <summary>Busqueda libre por titulo, usuario, URL y etiquetas (para el popup de la extension).</summary>
    public static List<Credential> Search(IEnumerable<Credential> entries, string query)
    {
        var q = query.Trim();
        var live = entries.Where(e => !e.Deleted && e.Kind is EntryKind.Login or EntryKind.App);
        if (q.Length == 0)
            return live.OrderByDescending(e => e.Favorite).ThenBy(e => e.Title).ToList();
        return live.Where(e => e.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                            || e.Username.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                            || e.Url.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                            || e.Tags.Any(t => t.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
                   .OrderByDescending(e => e.Favorite).ThenBy(e => e.Title).ToList();
    }

    /// <summary>
    /// Guarda lo escrito: si ya hay una entrada del mismo sitio o app con ese usuario, se le cambia la
    /// contraseña (la anterior queda en el historial); si no, se crea una nueva con el dominio o el
    /// nombre de la app como titulo. Devuelve si hubo algo que guardar (y ya se guardo).
    /// </summary>
    public static async Task<bool> UpsertAsync(VaultStore store, string? domain, string? package, string? appLabel, string username, string password)
    {
        var entries = store.Data!.Entries;
        var existing = Match(entries, domain, package)
            .FirstOrDefault(e => username.Length == 0 || e.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        var now = DateTimeOffset.UtcNow;
        if (existing is not null)
        {
            if (password.Length == 0 || existing.Password == password)
                return false;
            if (existing.Password.Length > 0)
                existing.History.Insert(0, new PasswordHistoryItem(existing.Password, existing.ModifiedAt));
            existing.Password = password;
            if (existing.Username.Length == 0)
                existing.Username = username;
            existing.ModifiedAt = now;
        }
        else
        {
            var web = !string.IsNullOrEmpty(domain);
            var entry = new Credential
            {
                Kind = web ? EntryKind.Login : EntryKind.App,
                Title = web ? domain! : appLabel ?? package ?? "App",
                Username = username,
                Password = password,
                Url = web ? "https://" + domain : string.Empty,
                CreatedAt = now,
                ModifiedAt = now,
            };
            if (!web && !string.IsNullOrEmpty(package))
                entry.Fields.Add(new CustomField { Name = "android", Value = package });
            entries.Add(entry);
        }
        await store.SaveAsync();
        return true;
    }
}
