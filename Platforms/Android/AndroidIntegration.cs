using Credentials.Services;

namespace Credentials.Platforms.Android;

/// <inheritdoc cref="IAndroidIntegration"/>
/// <remarks>Cada cosa es una llamada a AutofillSetup, MainActivity o la actividad en curso: lo que se decide con ellas esta en las paginas.</remarks>
public sealed class AndroidIntegration(ISettingsService settings, ILocalizationService l) : IAndroidIntegration
{
    public bool IsAvailable => true;
    public bool AutofillSupported => AutofillSetup.Supported;
    public bool AutofillIsOurs => AutofillSetup.IsOurs;
    public bool HasPreferredService => AutofillSetup.HasPreferredService;
    public IReadOnlyList<(string Package, string Name)> InstalledBrowsers() => AutofillSetup.InstalledBrowsers();
    public bool OpenPreferredService() => AutofillSetup.OpenPreferredService();
    public bool OpenBrowserSettings(string package) => AutofillSetup.OpenBrowserSettings(package);
    public void RequestAutofill() => AutofillSetup.Request();
    public void ApplyScreenshotPolicy() => MainActivity.ApplyScreenshotPolicy();
    public Task OfferAutofillAfterUnlockAsync(Page page) => AutofillSetup.OfferAfterUnlockAsync(page, settings, l);
    public void MoveToBack() => Platform.CurrentActivity?.MoveTaskToBack(true);

    public GateBackAction GateBack()
    {
        var activity = Platform.CurrentActivity;
        if (activity is MainActivity)
        {
            activity.MoveTaskToBack(true);
            return GateBackAction.MovedToBack;
        }
        return activity is null ? GateBackAction.None : GateBackAction.CloseGate;
    }
}
