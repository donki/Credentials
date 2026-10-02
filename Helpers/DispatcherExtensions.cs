namespace Credentials.Helpers;

/// <summary>Lo de MainThread.BeginInvokeOnMainThread, con el despachador de la pagina (que las pruebas sustituyen).</summary>
public static class DispatcherExtensions
{
    /// <summary>Al momento si ya se esta en el hilo de la interfaz; si no, se encola en el.</summary>
    public static void RunOnUi(this IDispatcher dispatcher, Action action)
    {
        if (dispatcher.IsDispatchRequired)
            dispatcher.Dispatch(action);
        else
            action();
    }
}
