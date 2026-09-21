using System.Text.Json.Serialization;

namespace Credentials.Models;

/// <summary>Que clase de cosa guarda una entrada.</summary>
public enum EntryKind
{
    /// <summary>Cuenta de un sitio web: usuario, contraseña, URL y, si lo tiene, TOTP.</summary>
    Login,
    /// <summary>Credencial de una aplicacion o servicio (Wi-Fi, servidor, API, licencia…).</summary>
    App,
    /// <summary>Solo segundo factor (TOTP), como en una app de autenticacion.</summary>
    Totp,
    /// <summary>Nota segura: texto cifrado, sin mas.</summary>
    Note,
}

/// <summary>Un campo a medida: etiqueta y valor; puede ser oculto (se ve como una contraseña).</summary>
public sealed class CustomField
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Hidden { get; set; }
}

/// <summary>Una contraseña anterior: por si se cambio y el sitio no la acepto.</summary>
public sealed record PasswordHistoryItem(string Password, DateTimeOffset ChangedAt);

/// <summary>
/// Una credencial. Todo lo que hay aqui viaja cifrado dentro de la boveda; nada se guarda suelto.
/// <see cref="ModifiedAt"/> es lo que decide, campo a campo no, entrada a entrada, quien gana al
/// mezclar la copia local con la de la nube.
/// </summary>
public sealed class Credential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public EntryKind Kind { get; set; } = EntryKind.Login;
    public string Title { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    /// <summary>Carpeta (ruta con «/»: «Trabajo/Servidores»). Vacia = raiz.</summary>
    public string Folder { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public bool Favorite { get; set; }

    /// <summary>Segundo factor en formato otpauth:// (secreto, algoritmo, digitos, periodo). Vacio = no tiene.</summary>
    public string Totp { get; set; } = string.Empty;

    public List<CustomField> Fields { get; set; } = [];
    public List<PasswordHistoryItem> History { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Borrada: se conserva un tiempo para que la baja llegue a los demas dispositivos al mezclar.</summary>
    public bool Deleted { get; set; }

    [JsonIgnore] public bool HasTotp => Totp.Length > 0;
    [JsonIgnore] public bool HasPassword => Password.Length > 0;

    /// <summary>Dominio de la URL, para buscar y para agrupar («github.com»).</summary>
    [JsonIgnore]
    public string Host
    {
        get
        {
            if (Url.Length == 0) return string.Empty;
            var text = Url.Contains("://", StringComparison.Ordinal) ? Url : "https://" + Url;
            return Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri.Host : Url;
        }
    }

    public Credential Clone()
    {
        var copy = (Credential)MemberwiseClone();
        copy.Tags = [.. Tags];
        copy.Fields = Fields.Select(f => new CustomField { Name = f.Name, Value = f.Value, Hidden = f.Hidden }).ToList();
        copy.History = [.. History];
        return copy;
    }
}

/// <summary>
/// El contenido de la boveda en claro (solo existe en memoria, ya descifrado). Se serializa a JSON
/// y ese JSON es lo que se cifra. <see cref="Version"/> permite cambiar el esquema sin perder nada.
/// </summary>
public sealed class VaultData
{
    public int Version { get; set; } = 1;
    public Guid VaultId { get; set; } = Guid.NewGuid();
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Credential> Entries { get; set; } = [];

    /// <summary>Carpetas que existen aunque esten vacias.</summary>
    public List<string> Folders { get; set; } = [];

    /// <summary>
    /// Mezcla otra copia (la de la nube, o la de otro dispositivo) con esta: por entrada, gana la mas
    /// nueva; las que solo estan en un lado se añaden. Devuelve cuantas han cambiado aqui.
    /// </summary>
    public int Merge(VaultData other)
    {
        var changed = 0;
        var mine = Entries.ToDictionary(e => e.Id);
        foreach (var theirs in other.Entries)
        {
            if (!mine.TryGetValue(theirs.Id, out var ours))
            {
                Entries.Add(theirs);
                changed++;
            }
            else if (theirs.ModifiedAt > ours.ModifiedAt)
            {
                Entries[Entries.IndexOf(ours)] = theirs;
                changed++;
            }
        }
        foreach (var f in other.Folders)
        {
            if (!Folders.Contains(f, StringComparer.OrdinalIgnoreCase))
            {
                Folders.Add(f);
                changed++;
            }
        }
        if (other.ModifiedAt > ModifiedAt)
            ModifiedAt = other.ModifiedAt;
        return changed;
    }

    /// <summary>Las borradas hace mas de 90 dias ya han tenido tiempo de llegar a todas partes.</summary>
    public void Purge() => Entries.RemoveAll(e => e.Deleted && e.ModifiedAt < DateTimeOffset.UtcNow.AddDays(-90));
}
