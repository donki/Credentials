using Microsoft.UI.Xaml;

namespace Credentials.WinUI;

/// <summary>Arranque WinUI de la misma aplicacion MAUI que corre en Android.</summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        // Una sola instancia: si ya hay otra (aunque este en la bandeja), se le pide que se enseñe y esta se va.
        if (!Credentials.Services.VaultStore.Sandbox && !Credentials.Platforms.Windows.SingleInstance.Claim())
        {
            Environment.Exit(0);
            return;
        }
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
