using System.IO.Pipes;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Credentials.Services;

namespace Credentials.Platforms.Windows;

/// <summary>
/// Lo que la aplicacion contesta a las extensiones de navegador. CredentialsHost.exe le pasa por la
/// tuberia una linea JSON por peticion («id», «type» y lo demas) y espera otra linea con el mismo
/// «id». Aqui no hay tuberia ni ventanas: pasar al hilo principal y traer la ventana para
/// desbloquear se le dan hechos (ExtensionServer en Windows; dobles en las pruebas).
/// </summary>
public sealed class ExtensionRequestHandler(VaultStore store, ISettingsService settings, Func<Func<Task>, Task> onMainThread, Action requestUnlock)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Un navegador acaba de saludar por primera vez (para avisar en pantalla).</summary>
    public event Action<string>? BrowserConnected;

    /// <summary>Cuanto espera «save», con la boveda cerrada, a que el usuario la desbloquee.</summary>
    public TimeSpan SaveUnlockTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Cada cuanto se mira, mientras tanto, si ya esta abierta.</summary>
    public TimeSpan SaveUnlockPoll { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Atiende una conexion: linea a linea hasta que el host la cierre (o se cancele).</summary>
    public async Task ServeAsync(Stream pipe, Func<bool> isConnected, CancellationToken cancellationToken)
    {
        var reader = new StreamReader(pipe, new UTF8Encoding(false));
        var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
        try
        {
            while (isConnected() && await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                var response = await HandleAsync(line).ConfigureAwait(false);
                await writer.WriteLineAsync(response).ConfigureAwait(false);
            }
        }
        catch (Exception) { /* el host se fue: se cierra esta conexion y ya */ }
    }

    public async Task<string> HandleAsync(string line)
    {
        JsonNode? request;
        JsonNode? id;
        string type;
        try
        {
            request = JsonNode.Parse(line);
            id = request?["id"]?.DeepClone();
            type = request?["type"]?.GetValue<string>() ?? string.Empty;
        }
        catch (Exception) { return "{\"error\":\"badjson\"}"; }
        var reply = new JsonObject { ["id"] = id };
        try
        {
            // Todo lo que toca la boveda o la interfaz va al hilo principal.
            await onMainThread(async () =>
            {
                switch (type)
                {
                    case "hello":
                    {
                        var browser = request?["browser"]?.GetValue<string>() ?? "chrome";
                        var first = settings.ExtensionSeen(browser) is null;
                        settings.SetExtensionSeen(browser);
                        if (first) BrowserConnected?.Invoke(browser);
                        reply["ok"] = true;
                        reply["locked"] = !store.IsUnlocked && !await store.TryTrustedUnlockAsync();
                        break;
                    }
                    case "show":
                        // El usuario ha pedido la aplicacion (popup, menu, desplegable): si esta
                        // bloqueada, aqui si se pide la contraseña.
                        requestUnlock();
                        reply["ok"] = true;
                        break;
                    case "list":
                    case "search":
                        await ListAsync(request, type, reply);
                        break;
                    case "save":
                        await SaveAsync(request, reply);
                        break;
                    default:
                        reply["error"] = "unknown";
                        break;
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            reply["error"] = "app";
            reply["detail"] = ex.Message;
        }
        return reply.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// Peticiones pasivas (la insignia al cargar cada pestaña, el desplegable al enfocar un campo):
    /// con la boveda cerrada se contesta «locked» y punto, sin sacar la ventana. La contraseña solo
    /// se pide cuando el usuario actua.
    /// </summary>
    private async Task ListAsync(JsonNode? request, string type, JsonObject reply)
    {
        if (!store.IsUnlocked && !await store.TryTrustedUnlockAsync())
        {
            reply["locked"] = true;
            return;
        }
        store.Touch();
        var entries = type == "list"
            ? AutofillLogic.Match(store.Data!.Entries, request?["host"]?.GetValue<string>(), null)
            : AutofillLogic.Search(store.Data!.Entries, request?["query"]?.GetValue<string>() ?? string.Empty);
        var array = new JsonArray();
        foreach (var e in entries.Take(type == "list" ? 20 : 50))
        {
            var item = new JsonObject
            {
                ["id"] = e.Id.ToString(),
                ["title"] = e.Title,
                ["username"] = e.Username,
                ["password"] = e.Password,
                ["url"] = e.Url,
            };
            if (e.HasTotp && Totp.Parse(e.Totp) is { } totp)
            {
                var (code, left) = totp.Now();
                item["totp"] = code;
                item["totpLeft"] = left;
            }
            array.Add(item);
        }
        reply["entries"] = array;
    }

    /// <summary>Guardar lo que se acaba de escribir en una web; con la boveda cerrada se pide abrirla y se espera.</summary>
    private async Task SaveAsync(JsonNode? request, JsonObject reply)
    {
        if (!store.IsUnlocked)
        {
            requestUnlock();
            var deadline = DateTime.UtcNow + SaveUnlockTimeout;
            while (!store.IsUnlocked && DateTime.UtcNow < deadline)
                await Task.Delay(SaveUnlockPoll);
            if (!store.IsUnlocked)
            {
                reply["locked"] = true;
                return;
            }
        }
        var host = request?["host"]?.GetValue<string>();
        var username = request?["username"]?.GetValue<string>() ?? string.Empty;
        var password = request?["password"]?.GetValue<string>() ?? string.Empty;
        var saved = await AutofillLogic.UpsertAsync(store, host, null, null, username, password);
        reply["ok"] = true;
        reply["changed"] = saved;
    }
}

/// <summary>
/// El bucle de la tuberia con nombre: espera a que CredentialsHost.exe se conecte, atiende cada
/// conexion aparte (ExtensionRequestHandler) y vuelve a esperar. Si la tuberia no se puede crear
/// (la tiene otra instancia) se reintenta al rato. Crear la tuberia (con su nombre y permisos) se le
/// pasa: en la aplicacion solo la abre el mismo usuario de Windows; en las pruebas, un nombre propio.
/// </summary>
public sealed class PipeServerLoop(Func<NamedPipeServerStream> create, ExtensionRequestHandler handler)
{
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = create();
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                return;
            }
            catch (Exception)
            {
                // Otra instancia tiene la tuberia (o no se pudo crear): se espera y se reintenta.
                server?.Dispose();
                try { await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            var connection = server;
            _ = Task.Run(async () =>
            {
                using (connection)
                    await handler.ServeAsync(connection, () => connection.IsConnected, cancellationToken).ConfigureAwait(false);
            });
        }
    }
}
