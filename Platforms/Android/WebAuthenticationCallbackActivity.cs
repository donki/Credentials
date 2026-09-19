using Android.App;
using Android.Content;
using Android.Content.PM;

namespace Credentials.Platforms.Android;

/// <summary>
/// La vuelta del navegador tras entrar con Google o Microsoft. El esquema propio va aqui; el de
/// Google (identificador de cliente invertido, que solo se conoce al compilar) lo añade
/// AndroidManifest.xml a esta misma actividad con el marcador ${googleScheme}.
/// </summary>
[Activity(Name = "com.socratic.credentials.AuthCallback", NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "com.socratic.credentials")]
public class WebAuthenticationCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
