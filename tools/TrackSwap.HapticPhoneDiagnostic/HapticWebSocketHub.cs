using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace TrackSwap.HapticPhoneDiagnostic;

internal sealed class HapticWebSocketHub
{
    private readonly ConcurrentDictionary<string, ClientConnection> clients = new();
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public int ClientCount => clients.Count;

    public async Task AcceptAsync(HttpContext context, CancellationToken cancellationToken)
    {
        WebSocket socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        string clientId = Guid.NewGuid().ToString("N")[..8];
        var client = new ClientConnection(clientId, socket);
        clients[clientId] = client;
        await BroadcastAsync(new
        {
            type = "clients",
            count = clients.Count
        }, cancellationToken).ConfigureAwait(false);

        var buffer = new byte[4096];
        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
                if (result.MessageType != WebSocketMessageType.Text || !result.EndOfMessage)
                {
                    continue;
                }

                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("type", out JsonElement type) || type.GetString() != "ack")
                {
                    continue;
                }

                await BroadcastAsync(new
                {
                    type = "ack",
                    clientId,
                    eventId = GetInt64(root, "eventId"),
                    supported = GetBoolean(root, "supported"),
                    armed = GetBoolean(root, "armed"),
                    accepted = GetBoolean(root, "accepted"),
                    visible = GetBoolean(root, "visible"),
                    reason = GetString(root, "reason"),
                    receivedAtUtc = DateTimeOffset.UtcNow
                }, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            clients.TryRemove(clientId, out _);
            client.Dispose();
            await BroadcastAsync(new
            {
                type = "clients",
                count = clients.Count
            }, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public Task BroadcastHapticAsync(object message, CancellationToken cancellationToken)
    {
        return BroadcastAsync(message, cancellationToken);
    }

    private async Task BroadcastAsync(object message, CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, jsonOptions);
        foreach (ClientConnection client in clients.Values)
        {
            if (!await client.TrySendAsync(payload, cancellationToken).ConfigureAwait(false))
            {
                clients.TryRemove(client.Id, out _);
                client.Dispose();
            }
        }
    }

    private static long GetInt64(JsonElement root, string property)
    {
        return root.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long result)
            ? result
            : 0;
    }

    private static bool GetBoolean(JsonElement root, string property)
    {
        return root.TryGetProperty(property, out JsonElement value) &&
            (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) &&
            value.GetBoolean();
    }

    private static string GetString(JsonElement root, string property)
    {
        return root.TryGetProperty(property, out JsonElement value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private sealed class ClientConnection : IDisposable
    {
        private readonly SemaphoreSlim sendLock = new(1, 1);

        public ClientConnection(string id, WebSocket socket)
        {
            Id = id;
            Socket = socket;
        }

        public string Id { get; }

        private WebSocket Socket { get; }

        public async Task<bool> TrySendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Socket.State != WebSocketState.Open)
                {
                    return false;
                }
                await Socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (WebSocketException)
            {
                return false;
            }
            finally
            {
                sendLock.Release();
            }
        }

        public void Dispose()
        {
            Socket.Dispose();
            sendLock.Dispose();
        }
    }
}
