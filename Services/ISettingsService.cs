namespace Credentials.Services;

/// <summary>Preferencias del usuario (Preferences del sistema). Nada sensible: eso va en la boveda o en SecureStorage.</summary>
public interface ISettingsService
{
    /// <summary>Codigo de idioma ("es", "en") o vacio para el del sistema.</summary>
    string Language { get; set; }

    /// <summary>Donde vive la boveda: solo aqui, Google Drive u OneDrive.</summary>
    StorageMode Storage { get; set; }

    /// <summary>Correo de la cuenta de la nube, solo para enseñarlo.</summary>
    string AccountEmail { get; set; }

    /// <summary>Minutos sin usar la aplicacion tras los que se bloquea sola; 0 = nunca.</summary>
    int AutoLockMinutes { get; set; }

    /// <summary>Desbloquear con Windows Hello / huella (la clave queda en la boveda del sistema).</summary>
    bool Biometrics { get; set; }

    /// <summary>Segundos que lo copiado (contraseñas, codigos) se queda en el portapapeles; 0 = no se vacia.</summary>
    int ClipboardSeconds { get; set; }

    /// <summary>Ultimo orden elegido en la lista.</summary>
    string SortMode { get; set; }
}
