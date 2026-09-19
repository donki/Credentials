using Credentials.Services;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace Credentials.Pages;

/// <summary>
/// Lector de QR con la camara (ZXing): para el otpauth:// que enseñan los sitios al activar el
/// segundo factor y para el QR de exportacion de Google Authenticator. Devuelve el texto leido.
/// </summary>
public sealed class ScanPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _result = new();
    private readonly CameraBarcodeReaderView _reader;
    private bool _done;

    private ScanPage(ILocalizationService l)
    {
        Title = l["ScanTitle"];
        _reader = new CameraBarcodeReaderView
        {
            Options = new BarcodeReaderOptions { Formats = BarcodeFormat.QrCode, AutoRotate = true, Multiple = false, TryHarder = true },
            IsDetecting = true,
        };
        _reader.BarcodesDetected += (_, e) =>
        {
            var value = e.Results.FirstOrDefault()?.Value;
            if (string.IsNullOrEmpty(value) || _done)
                return;
            _done = true;
            _reader.IsDetecting = false;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                _result.TrySetResult(value);
                await Navigation.PopModalAsync();
            });
        };
        var close = new Button { Text = l["Cancel"], Style = (Style)Application.Current!.Resources["OutlineButton"], Margin = new Thickness(16) };
        close.Clicked += async (_, _) => { _done = true; _result.TrySetResult(null); await Navigation.PopModalAsync(); };
        var grid = new Grid { RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)] };
        grid.Add(_reader, 0, 0);
        grid.Add(close, 0, 1);
        Content = grid;
    }

    protected override bool OnBackButtonPressed()
    {
        _done = true;
        _result.TrySetResult(null);
        return base.OnBackButtonPressed();
    }

    /// <summary>Pide la camara, abre el lector y devuelve el texto del QR (null si se cancela o no hay permiso).</summary>
    public static async Task<string?> ScanAsync(Page owner, ILocalizationService l)
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
        {
            await SocShared.ModernDialog.AlertAsync(owner, l["Camera"], l["CameraDenied"], l["Ok"]);
            return null;
        }
        var page = new ScanPage(l);
        await owner.Navigation.PushModalAsync(new NavigationPage(page));
        return await page._result.Task;
    }
}
