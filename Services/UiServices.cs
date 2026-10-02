namespace Credentials.Services;

/// <summary>
/// Los dialogos de la aplicacion (ModernDialog, constitucion A.9). Van detras de esta interfaz para
/// que las paginas se puedan probar sin pantalla: en las pruebas un doble contesta por el usuario.
/// </summary>
public interface IDialogService
{
    /// <summary>Aviso o confirmacion. True si se pulsa <paramref name="accept"/>.</summary>
    Task<bool> AlertAsync(Page page, string title, string message, string accept, string? cancel = null);

    /// <summary>Lista de opciones. La elegida, o null si se cancela.</summary>
    Task<string?> ActionSheetAsync(Page page, string? title, string cancel, params string[] options);

    /// <summary>Casilla de texto. Lo escrito, o null si se cancela.</summary>
    Task<string?> PromptAsync(Page page, string title, string? message, string accept, string cancel, bool isPassword = false);
}

/// <inheritdoc cref="IDialogService"/>
public sealed class ModernDialogService : IDialogService
{
    public Task<bool> AlertAsync(Page page, string title, string message, string accept, string? cancel = null) =>
        SocShared.ModernDialog.AlertAsync(page, title, message, accept, cancel);

    public Task<string?> ActionSheetAsync(Page page, string? title, string cancel, params string[] options) =>
        SocShared.ModernDialog.ActionSheetAsync(page, title, cancel, options);

    public Task<string?> PromptAsync(Page page, string title, string? message, string accept, string cancel, bool isPassword = false) =>
        SocShared.ModernDialog.PromptAsync(page, title, message, accept, cancel, isPassword: isPassword);
}

/// <summary>Navegacion entre las paginas del Shell (rutas «//VaultPage», «//SettingsPage»...).</summary>
public interface INavigationService
{
    Task GoToAsync(string route);

    /// <summary>Apila una pagina encima de la actual (con atras para volver).</summary>
    Task PushAsync(Page page);
}

/// <inheritdoc cref="INavigationService"/>
public sealed class ShellNavigationService : INavigationService
{
    public Task GoToAsync(string route) => Shell.Current.GoToAsync(route);

    public Task PushAsync(Page page) => Shell.Current.Navigation.PushAsync(page);
}

/// <summary>El lector de QR con la camara (ScanPage). Devuelve el texto leido, o null si se cancela o no hay permiso.</summary>
public interface IQrScanner
{
    Task<string?> ScanAsync(Page owner);
}

/// <summary>Elegir un fichero de texto (importar) y leerlo entero. Null si se cancela.</summary>
public interface ITextFilePicker
{
    Task<string?> PickTextAsync(string title);
}

/// <inheritdoc cref="ITextFilePicker"/>
public sealed class TextFilePicker : ITextFilePicker
{
    /// <summary>Abrir el fichero elegido (el de MAUI; las pruebas, sin plataforma, lo abren del disco).</summary>
    internal static Func<FileResult, Task<Stream>> Open { get; set; } = file => file.OpenReadAsync();

    public async Task<string?> PickTextAsync(string title)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = title });
        if (file is null)
            return null;
        using var stream = await Open(file);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync();
    }
}
