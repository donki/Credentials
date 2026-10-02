namespace Credentials.Services;

/// <summary>Sin verificacion biometrica: en Windows la boveda se abre solo con la contraseña maestra.</summary>
public sealed class NoBiometric : IBiometric
{
    public Task<bool> IsAvailableAsync() => Task.FromResult(false);

    public Task<bool> AuthenticateAsync(string title, string reason) => Task.FromResult(false);
}

/// <summary>Sin navegador para entrar en la nube: solo en el destino sin plataforma (net10.0, el de las pruebas).</summary>
public sealed class NoOAuthBrowser : IOAuthBrowser
{
    public string RedirectUri(string providerName, string clientId) => "http://127.0.0.1/auth/";

    public Task<Uri> AuthenticateAsync(Uri authorizeUrl, Uri callback, CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException("Sin navegador en esta plataforma.");
}
