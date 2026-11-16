using System.Runtime.InteropServices;

namespace Credentials.Platforms.Windows;

/// <summary>
/// ¿La aplicacion corre empaquetada (MSIX, la de la Microsoft Store) o suelta (el exe, con o sin
/// lanzador)? Es la unica pregunta que se hace, y todo lo que depende de ella la hace aqui.
/// </summary>
/// <remarks>
/// Empaquetada, lo que se escribe en HKCU queda virtualizado dentro del paquete: el navegador no ve
/// la clave de su host de mensajeria nativa y la extension nunca encuentra el puente. Por eso en la
/// version de la Store no se registra el puente ni se ofrece instalar la extension: se remite a la
/// version exe (<see cref="ReleasesUrl"/>). Se pregunta con GetCurrentPackageFullName (kernel32,
/// Windows 8 en adelante): con un bufer de longitud 0 contesta ERROR_INSUFFICIENT_BUFFER si hay
/// paquete y APPMODEL_ERROR_NO_PACKAGE si no. Si la llamada no existe (otro sistema) o falla, se da
/// por no empaquetada, que es como se ha comportado siempre.
/// </remarks>
public static class PackageIdentity
{
    /// <summary>Donde se descarga la version exe (la que puede usar las extensiones).</summary>
    public const string ReleasesUrl = "https://github.com/donki/Credentials/releases";

    public const int ErrorSuccess = 0;
    public const int ErrorInsufficientBuffer = 122;
    public const int AppModelErrorNoPackage = 15700;

    private static readonly Lazy<bool> Cached = new(() => Interpret(Probe));

    /// <summary>La aplicacion corre con identidad de paquete (MSIX). Se mira una vez por proceso.</summary>
    public static bool IsPackaged() => Cached.Value;

    /// <summary>Traduce lo que contesta GetCurrentPackageFullName; si lanza, no empaquetada.</summary>
    public static bool Interpret(Func<int> probe)
    {
        try
        {
            return probe() is ErrorInsufficientBuffer or ErrorSuccess;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static int Probe()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2))
            return AppModelErrorNoPackage;
        uint length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero);
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}
