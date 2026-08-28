using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Leitweb.Api.Realtime;

public sealed class LiveUpdateHub
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly SemaphoreSlim _broadcastLock = new(1, 1);

    public async Task HoldAsync(WebSocket socket, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        _clients[id] = socket;
        var buffer = new byte[256];
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            _clients.TryRemove(id, out _);
            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Verbindung beendet", CancellationToken.None);
            }
            catch (WebSocketException) { }
            catch (ObjectDisposedException) { }
        }
    }

    public async Task BroadcastAsync(string type, CancellationToken ct = default)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type }));
        await _broadcastLock.WaitAsync(ct);
        try
        {
            foreach (var client in _clients.ToArray())
            {
                if (client.Value.State != WebSocketState.Open)
                {
                    _clients.TryRemove(client.Key, out _);
                    continue;
                }
                try { await client.Value.SendAsync(payload, WebSocketMessageType.Text, true, ct); }
                catch (WebSocketException) { _clients.TryRemove(client.Key, out _); }
                catch (ObjectDisposedException) { _clients.TryRemove(client.Key, out _); }
            }
        }
        finally
        {
            _broadcastLock.Release();
        }
    }
}
