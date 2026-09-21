using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
using Credentials.Services;

namespace Credentials.Platforms.Android;

/// <inheritdoc cref="IBiometric"/>
/// <remarks>BiometricPrompt de AndroidX: huella, cara o lo que tenga el movil, con el PIN del
/// dispositivo como alternativa (DeviceCredential).</remarks>
public class Biometric : IBiometric
{
    private const int Allowed = BiometricManager.Authenticators.BiometricStrong | BiometricManager.Authenticators.DeviceCredential;

    public Task<bool> IsAvailableAsync()
    {
        try
        {
            var manager = BiometricManager.From(Platform.AppContext);
            return Task.FromResult(manager.CanAuthenticate(Allowed) == BiometricManager.BiometricSuccess);
        }
        catch (Exception) { return Task.FromResult(false); }
    }

    public Task<bool> AuthenticateAsync(string title, string reason)
    {
        var tcs = new TaskCompletionSource<bool>();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                if (Platform.CurrentActivity is not FragmentActivity activity)
                {
                    tcs.TrySetResult(false);
                    return;
                }
                var executor = ContextCompat.GetMainExecutor(activity);
                var prompt = new BiometricPrompt(activity, executor, new Callback(tcs));
                var info = new BiometricPrompt.PromptInfo.Builder()
                    .SetTitle(title)
                    .SetSubtitle(reason)
                    .SetAllowedAuthenticators(Allowed)
                    .Build();
                prompt.Authenticate(info);
            }
            catch (Exception)
            {
                tcs.TrySetResult(false);
            }
        });
        return tcs.Task;
    }

    private sealed class Callback(TaskCompletionSource<bool> tcs) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result) => tcs.TrySetResult(true);
        public override void OnAuthenticationError(int errorCode, Java.Lang.ICharSequence errString) => tcs.TrySetResult(false);
        public override void OnAuthenticationFailed() { }
    }
}
