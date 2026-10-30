using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using WatsonWebserver.Core;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace UmamusumeResponseAnalyzer.Plugin;

internal static class WebSocketUpgrade
{
    internal static async Task RunAsync(
        HttpContextBase context,
        Func<WebSocket, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var key = request.Headers["Sec-WebSocket-Key"];
        var validKey = key is not null && Convert.TryFromBase64String(key, new byte[16], out var length) && length == 16;
        if (request.Method != HttpMethod.GET || request.ProtocolVersion != "HTTP/1.1" ||
            !string.Equals(request.Headers["Upgrade"], "websocket", StringComparison.OrdinalIgnoreCase) ||
            !(request.Headers["Connection"] ?? "").Split(',').Any(value => value.Trim().Equals("Upgrade", StringComparison.OrdinalIgnoreCase)) ||
            request.Headers["Sec-WebSocket-Version"] != "13" || !validKey || request.ContentLength > 0 || request.ChunkedTransfer)
        {
            context.Response.StatusCode = 400;
            await context.Response.Send(string.Empty, cancellationToken).ConfigureAwait(false);
            return;
        }

        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(
            key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var handshake = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n");
        // Watson.Lite exposes the duplex client stream here; Response.Send would close it.
        var stream = request.Data;
        await stream.WriteAsync(handshake, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        context.Response.StatusCode = 101;
        context.Response.ResponseSent = true;
        using var socket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, TimeSpan.FromSeconds(30));
        using var abort = cancellationToken.Register(socket.Abort);
        await handler(socket, cancellationToken).ConfigureAwait(false);
    }
}
