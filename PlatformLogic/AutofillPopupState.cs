using Credentials.Models;

namespace Credentials.Platforms.Windows;

/// <summary>
/// La ventanita con la lista del autocompletar de escritorio, sin WinUI: lo que enseña (la cabecera,
/// una fila por entrada con su titulo y, si lo tiene, el usuario debajo) y si esta a la vista, para no
/// volver a enseñarla ni a esconderla dos veces. La ventana de verdad la pinta DesktopAutofill.Popup.
/// </summary>
public sealed class AutofillPopupState
{
    /// <summary>La cabecera de la lista.</summary>
    public const string Header = "sOC Credentials";

    /// <summary>Una fila de la lista: la entrada que se rellena al elegirla.</summary>
    public sealed record Row(string Title, string? Subtitle, Credential Entry);

    /// <summary>Las filas, en el orden de las entradas; sin usuario, sin segunda linea.</summary>
    public static IReadOnlyList<Row> Rows(IEnumerable<Credential> entries) =>
        entries.Select(e => new Row(e.Title, e.Username.Length > 0 ? e.Username : null, e)).ToList();

    /// <summary>La ventanita esta a la vista.</summary>
    public bool Visible { get; private set; }

    /// <summary>Se va a enseñar: true si no lo estaba (hay que mostrar la ventana).</summary>
    public bool Show()
    {
        if (Visible)
            return false;
        Visible = true;
        return true;
    }

    /// <summary>Se va a esconder: true si estaba a la vista (hay que esconder la ventana).</summary>
    public bool Hide()
    {
        if (!Visible)
            return false;
        Visible = false;
        return true;
    }

    /// <summary>La ventana se ha cerrado por su cuenta.</summary>
    public void Closed() => Visible = false;
}
