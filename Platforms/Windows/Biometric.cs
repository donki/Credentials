using System.Runtime.InteropServices;
using Credentials.Services;
using Windows.Security.Credentials.UI;
using WinRT;

namespace Credentials.Platforms.Windows;

/// <inheritdoc cref="IBiometric"/>
/// <remarks>Windows Hello por UserConsentVerifier. En una aplicacion de escritorio hay que darle la
/// ventana (interop), si no el dialogo no sabe donde salir.</remarks>
public class Biometric : IBiometric
{
    public async Task<bool> IsAvailableAsync()
    {
        try { return await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available; }
        catch (Exception) { return false; }
    }

    public async Task<bool> AuthenticateAsync(string title, string reason)
    {
        try
        {
            var hwnd = ((MauiWinUIWindow?)Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView)?.WindowHandle ?? IntPtr.Zero;
            var interop = UserConsentVerifier.As<IUserConsentVerifierInterop>();
            var iid = typeof(global::Windows.Foundation.IAsyncOperation<UserConsentVerificationResult>).GUID;
            var ptr = interop.RequestVerificationForWindowAsync(hwnd, reason, ref iid);
            var operation = MarshalInterface<global::Windows.Foundation.IAsyncOperation<UserConsentVerificationResult>>.FromAbi(ptr);
            var result = await operation;
            return result == UserConsentVerificationResult.Verified;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [ComImport, Guid("39E050C3-4E74-441A-8DC0-B81104DF949C"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
    private interface IUserConsentVerifierInterop
    {
        IntPtr RequestVerificationForWindowAsync(IntPtr appWindow, [MarshalAs(UnmanagedType.HString)] string message, ref Guid riid);
    }
}
