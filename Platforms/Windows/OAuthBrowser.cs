using System.Diagnostics;
using Credentials.Services;

namespace Credentials.Platforms.Windows;

/// <inheritdoc cref="IOAuthBrowser"/>
/// <remarks>Navegador del sistema y un servidor local de un solo uso en 127.0.0.1 (el cliente de
/// Google es de escritorio y admite cualquier puerto; el de Microsoft lleva http://127.0.0.1/auth/
/// registrado y Entra ignora el puerto en loopback). La espera de la vuelta esta en LoopbackOAuth
/// y el servidor en HttpLoopbackListener (PlatformLogic, con pruebas).</remarks>
public class OAuthBrowser : IOAuthBrowser
{
    public string RedirectUri(string providerName, string clientId) => $"http://127.0.0.1:{HttpLoopbackListener.FreePort()}/auth/";

    public async Task<Uri> AuthenticateAsync(Uri authorizeUrl, Uri callback, CancellationToken cancellationToken)
    {
        using var listener = new HttpLoopbackListener(callback);
        return await LoopbackOAuth.AuthenticateAsync(listener, callback,
            () => Process.Start(new ProcessStartInfo(authorizeUrl.ToString()) { UseShellExecute = true }), cancellationToken).ConfigureAwait(false);
    }
}
