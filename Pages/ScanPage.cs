using Credentials.Helpers;
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

    internal ScanPage(ILocalizationService l)
    {
        Title = l["ScanTitle"];
        _reader = new CameraBarcodeReaderView
        {
            Options = new BarcodeReaderOptions { Formats = BarcodeFormat.QrCode, AutoRotate = true, Multiple = false, TryHarder = true },
            IsDetecting = true,
        };
        _reader.BarcodesDetected += (_, e) => Detected(e.Results.FirstOrDefault()?.Value);
        var close = new Button { Text = l["Cancel"], Style = (Style)Application.Current!.Resources["OutlineButton"], Margin = new Thickness(16) };
        close.Clicked += async (_, _) => { _done = true; _result.TrySetResult(null); await Navigation.PopModalAsync(); };
        var grid = new Grid { RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)] };
        grid.Add(_reader, 0, 0);
        grid.Add(close, 0, 1);
        Content = grid;
    }

    /// <summary>El texto leido (null si se cancela).</summary>
    internal Task<string?> Result => _result.Task;

    /// <summary>La camara ha leido un QR: el primero que traiga texto cierra el lector.</summary>
    internal void Detected(string? value)
    {
        if (string.IsNullOrEmpty(value) || _done)
            return;
        _done = true;
        _reader.IsDetecting = false;
        Dispatcher.RunOnUi(async () =>
        {
            _result.TrySetResult(value);
            await Navigation.PopModalAsync();
        });
    }

    protected override bool OnBackButtonPressed()
    {
        _done = true;
        _result.TrySetResult(null);
        return base.OnBackButtonPressed();
    }
}

/// <summary>El lector de QR de verdad: pide la camara, abre <see cref="ScanPage"/> y espera lo leido.</summary>
public sealed class CameraQrScanner(ILocalizationService l, IDialogService dialogs) : IQrScanner
{
    /// <summary>El permiso de la camara (en las pruebas, un doble): true si lo hay o se concede.</summary>
    internal static Func<Task<bool>> CameraAllowed { get; set; } = async () =>
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.Camera>();
        return status == PermissionStatus.Granted;
    };

    public async Task<string?> ScanAsync(Page owner)
    {
        if (!await CameraAllowed())
        {
            await dialogs.AlertAsync(owner, l["Camera"], l["CameraDenied"], l["Ok"]);
            return null;
        }
        var page = new ScanPage(l);
        await owner.Navigation.PushModalAsync(new NavigationPage(page));
        return await page.Result;
    }
}
