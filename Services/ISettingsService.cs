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

    /// <summary>Windows: al minimizar, esconderse en el area de notificacion en vez de a la barra de tareas.</summary>
    bool TrayOnMinimize { get; set; }

    /// <summary>Windows: al desbloquear, ofrecer instalar la extension en los navegadores que no la tengan.</summary>
    bool AskExtensions { get; set; }

    /// <summary>Android: al desbloquear, si otro gestor rellena las contraseñas, proponer que lo haga esta aplicacion.</summary>
    bool AskAutofill { get; set; }

    /// <summary>Windows: lista de entradas pegada a los campos de contraseña de las aplicaciones del escritorio.</summary>
    bool DesktopAutofill { get; set; }

    /// <summary>Windows: cuando conecto por ultima vez la extension de ese navegador (chrome/edge/firefox); null si nunca.</summary>
    /// <summary>La guia de configuracion ya ha salido sola tras el primer desbloqueo (despues se abre desde el menu).</summary>
    bool TutorialDone { get; set; }

    DateTimeOffset? ExtensionSeen(string browser);
    void SetExtensionSeen(string browser);
}
