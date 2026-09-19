using Android.App;
using Android.Content;
using Android.Content.PM;

namespace Credentials.Platforms.Android;

/// <summary>
/// La vuelta del navegador tras entrar con Google o Microsoft. Los intent-filter (esquema propio
/// para Microsoft y el de Google, que solo se conoce al compilar) y los demas atributos estan en
/// AndroidManifest.xml: como la actividad se declara alli, lo que se ponga aqui en atributos de
/// C# se pierde al fusionar el manifiesto (comprobado: el filtro de Microsoft no llegaba al APK).
/// </summary>
[Activity(Name = "com.socratic.credentials.AuthCallback", Exported = true)]
public class WebAuthenticationCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
