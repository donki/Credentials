namespace Credentials.Services;

/// <summary>Que hacer con «atras» en la puerta de desbloqueo de Android.</summary>
public enum GateBackAction
{
    /// <summary>Nada (no hay actividad).</summary>
    None,

    /// <summary>La aplicacion se ha ido al fondo (como en inicio).</summary>
    MovedToBack,

    /// <summary>Se rellenaba otra aplicacion (actividad de autocompletar): cerrar la puerta sin rellenar.</summary>
    CloseGate,
}

/// <summary>
/// Lo que la aplicacion hace solo en Android (servicio de autocompletar del sistema, servicio
/// preferido de contraseñas, ajustes de los navegadores, capturas de pantalla, mandar la aplicacion
/// al fondo). Las paginas lo piden aqui en lugar de llevar <c>#if ANDROID</c>: en Android es
/// <c>Platforms.Android.AndroidIntegration</c>; en Windows y sin plataforma,
/// <see cref="NoAndroidIntegration"/>; las pruebas ponen un doble.
/// </summary>
public interface IAndroidIntegration
{
    /// <summary>Es Android (si no, nada de lo de abajo se enseña ni hace nada).</summary>
    bool IsAvailable { get; }

    /// <summary>El dispositivo admite servicio de autocompletar (Android 8+).</summary>
    bool AutofillSupported { get; }

    /// <summary>El servicio de autocompletar activo es esta aplicacion.</summary>
    bool AutofillIsOurs { get; }

    /// <summary>Hay «servicio preferido de contraseñas» (Android 14+), el que siguen los navegadores.</summary>
    bool HasPreferredService { get; }

    /// <summary>Los navegadores instalados que tienen gestor de contraseñas propio (paquete y nombre).</summary>
    IReadOnlyList<(string Package, string Name)> InstalledBrowsers();

    /// <summary>Abre la pantalla del sistema del servicio preferido; false si no hay ninguna.</summary>
    bool OpenPreferredService();

    /// <summary>Abre los ajustes de un navegador; false si no se puede.</summary>
    bool OpenBrowserSettings(string package);

    /// <summary>Pide al sistema que esta aplicacion sea el servicio de autocompletar.</summary>
    void RequestAutofill();

    /// <summary>Aplica al momento el ajuste de permitir capturas (FLAG_SECURE).</summary>
    void ApplyScreenshotPolicy();

    /// <summary>Tras desbloquear: si otro gestor rellena las contraseñas, se propone que lo haga esta.</summary>
    Task OfferAutofillAfterUnlockAsync(Page page);

    /// <summary>Manda la aplicacion al fondo (atras en la boveda).</summary>
    void MoveToBack();

    /// <summary>Atras en la puerta de desbloqueo.</summary>
    GateBackAction GateBack();
}

/// <summary>Sin Android (Windows y el destino sin plataforma): no hay nada que hacer.</summary>
public sealed class NoAndroidIntegration : IAndroidIntegration
{
    public bool IsAvailable => false;
    public bool AutofillSupported => false;
    public bool AutofillIsOurs => false;
    public bool HasPreferredService => false;
    public IReadOnlyList<(string Package, string Name)> InstalledBrowsers() => [];
    public bool OpenPreferredService() => false;
    public bool OpenBrowserSettings(string package) => false;
    public void RequestAutofill() { }
    public void ApplyScreenshotPolicy() { }
    public Task OfferAutofillAfterUnlockAsync(Page page) => Task.CompletedTask;
    public void MoveToBack() { }
    public GateBackAction GateBack() => GateBackAction.None;
}
