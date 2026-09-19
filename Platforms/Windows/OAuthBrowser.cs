using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Credentials.Services;

namespace Credentials.Platforms.Windows;

/// <inheritdoc cref="IOAuthBrowser"/>
/// <remarks>Navegador del sistema y un servidor local de un solo uso en 127.0.0.1 (el cliente de
/// Google es de escritorio y admite cualquier puerto; el de Microsoft lleva http://127.0.0.1/auth/
/// registrado y Entra ignora el puerto en loopback).</remarks>
public class OAuthBrowser : IOAuthBrowser
{
    public string RedirectUri(string providerName, string clientId) => $"http://127.0.0.1:{FindPort()}/auth/";

    public async Task<Uri> AuthenticateAsync(Uri authorizeUrl, Uri callback, CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(callback.ToString());
        listener.Start();
        Process.Start(new ProcessStartInfo(authorizeUrl.ToString()) { UseShellExecute = true });
        using var registration = cancellationToken.Register(listener.Abort);
        while (true)
        {
            HttpListenerContext context;
            // Si el tiempo de espera vence, Abort tumba el listener y GetContextAsync sale con
            // HttpListenerException u ObjectDisposedException segun el momento: las dos son «cancelado».
            try { context = await listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                throw;
            }

            // El navegador pide tambien /favicon.ico y cosas asi: solo vale la vuelta al camino registrado.
            var url = context.Request.Url ?? callback;
            if (!url.AbsolutePath.Equals(callback.AbsolutePath, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                continue;
            }

            var ok = context.Request.QueryString["error"] is null;
            var text = ok ? "Ya puedes volver a la aplicaci&oacute;n." : "La entrada no se ha completado.";
            var html = "<html><body style=\"font-family:Segoe UI;background:#14161F;color:#eee;text-align:center;padding-top:80px\"><h2>sOC Credentials</h2><p>" + text + "</p></body></html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            try
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
                context.Response.Close();
            }
            catch (Exception) { /* el navegador ya tiene lo que queria; la pagina de cortesia es lo de menos */ }
            listener.Stop();
            return url;
        }
    }

    private static int FindPort()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }
}
