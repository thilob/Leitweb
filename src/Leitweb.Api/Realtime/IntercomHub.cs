using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Leitweb.Api.Realtime;

public sealed class IntercomHub(IConfiguration configuration) : IDisposable
{
    public const string Protocol = "leitweb-intercom";
    public const string BearerPrefix = "bearer.";
    private const int MaxMessageBytes = 64 * 1024;
    private const int MaxClients = 100;
    private const int MaxChannelMembers = 8;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly Dictionary<string, Client> _clients = new();
    private readonly Dictionary<string, Floor> _floors = new();
    private Timer? _timer;

    private sealed class Client(string name, string? subject, DateTimeOffset? expires, CancellationTokenSource lifetime)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string RenewalKey { get; } = Guid.NewGuid().ToString("N");
        public string Name { get; } = name;
        public string? Subject { get; } = subject;
        public DateTimeOffset? Expires { get; set; } = expires;
        public bool Ready { get; set; }
        public string? Room { get; set; }
        public Invitation? Invite { get; set; }
        public CancellationTokenSource Lifetime { get; } = lifetime;
        public Channel<object> Messages { get; } = Channel.CreateBounded<object>(
            new BoundedChannelOptions(256) { SingleReader = true });
    }

    private sealed record Invitation(Client Caller, Client Recipient, DateTimeOffset Until);
    private sealed record Floor(Client Speaker, string Request, DateTimeOffset Until);

    public async Task HoldAsync(WebSocket socket, string name, string? subject, DateTimeOffset? expires, CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (expires is { } deadline)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return;
            lifetime.CancelAfter(remaining);
        }

        var client = new Client(name, subject, expires, lifetime);
        lock (_gate)
        {
            if (_clients.Count >= MaxClients)
            {
                socket.Abort();
                return;
            }
            _clients.Add(client.Id, client);
            _timer ??= new Timer(_ => Expire(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            Enqueue(client, new
            {
                type = "hello", id = client.Id, renewalKey = client.RenewalKey, iceServers = IceServers(client.Id)
            });
            Publish();
        }

        var send = SendAsync(socket, client, lifetime.Token);
        var receive = ReceiveAsync(socket, client, lifetime.Token);
        try
        {
            await Task.WhenAny(send, receive);
        }
        finally
        {
            lock (_gate)
            {
                _clients.Remove(client.Id);
                Leave(client);
                Publish();
            }
            await lifetime.CancelAsync();
            socket.Abort();
            await Task.WhenAll(send, receive);
        }
    }

    public bool Renew(string renewalKey, string subject, DateTimeOffset expires)
    {
        lock (_gate)
        {
            var client = _clients.Values.FirstOrDefault(candidate =>
                candidate.RenewalKey == renewalKey && candidate.Subject == subject);
            var now = DateTimeOffset.UtcNow;
            if (client?.Expires is not { } previous || previous <= now || expires <= now ||
                client.Lifetime.IsCancellationRequested) return false;
            if (expires > previous)
            {
                client.Expires = expires;
                client.Lifetime.CancelAfter(expires - now);
            }
            return true;
        }
    }

    private object[] IceServers(string clientId)
    {
        var servers = new List<object>();
        if (configuration["Intercom:StunUrl"] is { Length: > 0 } stun)
            servers.Add(new { urls = stun });
        if (configuration["Intercom:TurnUrl"] is { Length: > 0 } turn &&
            configuration["Intercom:TurnSecret"] is { Length: > 0 } secret)
        {
            var username = $"{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}:{clientId}";
            // coturn's REST authentication scheme uses HMAC-SHA1 for temporary credentials.
            var credential = Convert.ToBase64String(HMACSHA1.HashData(
                Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(username)));
            servers.Add(new { urls = turn, username, credential });
        }
        return servers.ToArray();
    }

    private static async Task SendAsync(WebSocket socket, Client client, CancellationToken ct)
    {
        try
        {
            await foreach (var message in client.Messages.Reader.ReadAllAsync(ct))
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
                await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    private async Task ReceiveAsync(WebSocket socket, Client client, CancellationToken ct)
    {
        var buffer = new byte[MaxMessageBytes];
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var window = DateTimeOffset.UtcNow;
        var count = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                idle.CancelAfter(TimeSpan.FromSeconds(50));
                var length = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer.AsMemory(length), idle.Token);
                    if (result.MessageType != WebSocketMessageType.Text) return;
                    length += result.Count;
                    if (length == buffer.Length && !result.EndOfMessage) return;
                } while (!result.EndOfMessage);

                var now = DateTimeOffset.UtcNow;
                if (now - window >= TimeSpan.FromSeconds(1))
                {
                    window = now;
                    count = 0;
                }
                if (++count > 256) return;

                using var document = JsonDocument.Parse(buffer.AsMemory(0, length));
                if (document.RootElement.ValueKind != JsonValueKind.Object) return;
                lock (_gate) Handle(client, document.RootElement);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (JsonException) { }
    }

    private void Handle(Client client, JsonElement message)
    {
        if (!_clients.ContainsKey(client.Id)) return;
        var type = ReadString(message, "type");
        if (type == "ping")
        {
            Enqueue(client, new { type = "pong" });
            return;
        }
        if (type is "ready" or "unready")
        {
            var ready = type == "ready";
            if (client.Ready != ready)
            {
                if (!ready) Leave(client);
                client.Ready = ready;
                Publish();
            }
            return;
        }
        if (!client.Ready) return;

        var target = ReadString(message, "target");
        switch (type)
        {
            case "join":
                if (target is not ("channel-1" or "channel-2" or "channel-3") || target == client.Room) return;
                if (Members(target).Count() >= MaxChannelMembers)
                {
                    Error(client, "Der Kanal ist voll.");
                    return;
                }
                Leave(client);
                client.Room = target;
                break;
            case "leave":
                Leave(client);
                break;
            case "invite":
                if (target is null || target == client.Id || !_clients.TryGetValue(target, out var recipient) ||
                    !recipient.Ready || recipient.Invite is not null || IsDirect(recipient.Room) ||
                    client.Invite is not null || IsDirect(client.Room))
                {
                    Error(client, "Der Client ist gerade nicht für einen Direktanruf verfügbar.");
                    return;
                }
                client.Invite = recipient.Invite = new Invitation(client, recipient, DateTimeOffset.UtcNow.AddSeconds(25));
                break;
            case "accept":
                if (client.Invite is not { } invite || invite.Recipient != client || invite.Caller.Id != target ||
                    invite.Until <= DateTimeOffset.UtcNow) return;
                Leave(client);
                Leave(invite.Caller);
                client.Room = invite.Caller.Room = $"direct-{Guid.NewGuid():N}";
                break;
            case "decline":
                if (client.Invite is not { } pending) return;
                ClearInvitation(pending);
                if (client == pending.Recipient) Error(pending.Caller, "Der Direktanruf wurde abgelehnt.");
                break;
            case "press":
                if (client.Room is not { } room || ReadString(message, "room") != room ||
                    ReadString(message, "request") is not { Length: > 0 and <= 64 } request) return;
                if (!Members(room).Any(member => member != client))
                {
                    Error(client, "Im Gespräch ist noch kein weiterer Client.", request);
                    return;
                }
                if (_floors.ContainsKey(room))
                {
                    Error(client, "Das Sprechrecht ist belegt.", request);
                    return;
                }
                _floors.Add(room, new Floor(client, request, DateTimeOffset.UtcNow.AddSeconds(30)));
                break;
            case "release":
                if (client.Room is not { } current || ReadString(message, "room") != current ||
                    !_floors.TryGetValue(current, out var floor) || floor.Speaker != client ||
                    floor.Request != ReadString(message, "request")) return;
                _floors.Remove(current);
                break;
            case "signal":
                RelaySignal(client, target, message);
                return;
            default:
                return;
        }
        Publish();
    }

    private void RelaySignal(Client sender, string? target, JsonElement message)
    {
        if (target is null || target == sender.Id || sender.Room is not { } room ||
            ReadString(message, "room") != room || !_clients.TryGetValue(target, out var receiver) ||
            receiver.Room != room || !message.TryGetProperty("data", out var signal) ||
            signal.ValueKind != JsonValueKind.Object ||
            ReadString(signal, "kind") is not ("offer" or "answer" or "candidate")) return;

        Enqueue(receiver, new { type = "signal", from = sender.Id, room, data = signal.Clone() });
    }

    private void Leave(Client client)
    {
        if (client.Invite is { } invite) ClearInvitation(invite);
        if (client.Room is not { } room) return;
        client.Room = null;
        if (_floors.TryGetValue(room, out var floor) && floor.Speaker == client) _floors.Remove(room);
        if (!IsDirect(room)) return;

        foreach (var other in Members(room)) other.Room = null;
        _floors.Remove(room);
    }

    private static bool IsDirect(string? room) => room?.StartsWith("direct-", StringComparison.Ordinal) == true;
    private IEnumerable<Client> Members(string room) => _clients.Values.Where(client => client.Room == room);

    private static void ClearInvitation(Invitation invite)
    {
        invite.Caller.Invite = null;
        invite.Recipient.Invite = null;
    }

    private void Expire()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var changed = false;
            foreach (var client in _clients.Values)
            {
                if (client.Invite is not { } invite || invite.Until > now) continue;
                ClearInvitation(invite);
                Error(invite.Caller, "Der Direktanruf wurde nicht angenommen.");
                changed = true;
            }
            foreach (var room in _floors.Where(entry => entry.Value.Until <= now).Select(entry => entry.Key).ToArray())
            {
                _floors.Remove(room);
                changed = true;
            }
            if (changed) Publish();
        }
    }

    private void Publish()
    {
        var clients = _clients.Values.Select(client => new
        {
            client.Id,
            client.Name,
            available = client.Ready && client.Invite is null && !IsDirect(client.Room)
        }).ToArray();
        foreach (var client in _clients.Values)
        {
            Floor? floor = null;
            if (client.Room is { } room) _floors.TryGetValue(room, out floor);
            Enqueue(client, new
            {
                type = "state",
                clients,
                room = client.Room,
                peers = client.Room is { } active ? Members(active).Where(member => member != client)
                    .Select(member => member.Id).ToArray() : [],
                speaker = floor?.Speaker.Id,
                request = floor is { } granted && granted.Speaker == client ? granted.Request : null,
                incoming = client.Invite is { } incoming && incoming.Recipient == client ? incoming.Caller.Id : null,
                outgoing = client.Invite is { } outgoing && outgoing.Caller == client ? outgoing.Recipient.Id : null
            });
        }
    }

    private static string? ReadString(JsonElement message, string key) =>
        message.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void Error(Client client, string message, string? request = null) =>
        Enqueue(client, new { type = "error", message, request });

    private static void Enqueue(Client client, object message)
    {
        if (!client.Messages.Writer.TryWrite(message)) client.Lifetime.Cancel();
    }

    public void Dispose() => _timer?.Dispose();
}
