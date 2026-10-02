using Credentials.Pages;
namespace Credentials.Ui.Tests;
public class SmokeTests
{
    [Fact]
    public void Construye_todo()
    {
        using var app = TestHost.Start();
        _ = new VaultPage();
        _ = new SettingsPage();
        _ = new UnlockPage();
        _ = new AboutPage();
        _ = new TutorialPage();
        _ = new EntryPage(new Credentials.Models.Credential { Title = "x" }, isNew: false);
        _ = new AppShell();
    }
}
