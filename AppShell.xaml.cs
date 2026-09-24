using Credentials.Helpers;
using Credentials.Services;

namespace Credentials;

public partial class AppShell : Shell
{
    private readonly ILocalizationService _l;

    public AppShell()
    {
        InitializeComponent();

        _l = ServiceHelper.GetRequiredService<ILocalizationService>();
        _l.LanguageChanged += (_, _) => ApplyTexts();
        ApplyTexts();
    }

    private void ApplyTexts()
    {
        HomeLabel.Text = _l["MenuVault"];
        SettingsLabel.Text = _l["MenuSettings"];
        AboutLabel.Text = _l["About"];
        TutorialLabel.Text = _l["MenuTutorial"];
        VersionLabel.Text = $"v{AppInfo.Current.VersionString}";

        // Los titulos de las rutas del Shell tambien se localizan (constitucion 8).
        foreach (var item in Items)
        {
            if (item.Route?.Contains("VaultPage") == true)
                item.Title = _l["MenuVault"];
            else if (item.Route?.Contains("SettingsPage") == true)
                item.Title = _l["MenuSettings"];
            else if (item.Route?.Contains("TutorialPage") == true)
                item.Title = _l["MenuTutorial"];
            else if (item.Route?.Contains("AboutPage") == true)
                item.Title = _l["About"];
        }
    }

    private async void OnHomeTapped(object sender, TappedEventArgs e) => await NavigateAsync("//VaultPage");

    private async void OnSettingsTapped(object sender, TappedEventArgs e) => await NavigateAsync("//SettingsPage");

    private async void OnTutorialTapped(object sender, TappedEventArgs e) => await NavigateAsync("//TutorialPage");

    private async void OnAboutTapped(object sender, TappedEventArgs e) => await NavigateAsync("//AboutPage");

    private async Task NavigateAsync(string route)
    {
        FlyoutIsPresented = false;
        await GoToAsync(route);
    }
}
