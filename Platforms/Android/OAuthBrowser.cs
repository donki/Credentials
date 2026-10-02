using Credentials.Services;

namespace Credentials.Platforms.Android;

/// <inheritdoc cref="IOAuthBrowser"/>
/// <remarks>WebAuthenticator (pestaña de Chrome, no un WebView, que Google rechaza). Microsoft
/// vuelve por el esquema propio de la aplicacion; Google exige el identificador de cliente invertido,
/// que se calcula en oauth.props y recoge WebAuthenticationCallbackActivity.</remarks>
public class OAuthBrowser : IOAuthBrowser
{
    public string RedirectUri(string providerName, string clientId) => AndroidOAuth.RedirectUri(providerName, OAuthSecrets.GoogleRedirectScheme);

    public async Task<Uri> AuthenticateAsync(Uri authorizeUrl, Uri callback, CancellationToken cancellationToken)
    {
        var result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
        {
            Url = authorizeUrl,
            CallbackUrl = callback,
            PrefersEphemeralWebBrowserSession = false,
        });
        return AndroidOAuth.CallbackWithProperties(callback, result.Properties);
    }
}
