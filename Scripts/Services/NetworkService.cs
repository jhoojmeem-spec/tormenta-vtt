using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TormentaVTT.Services
{
    /// <summary>
    /// TCP network service with multi-client support and length-prefixed message framing.
    /// Framing: 4 bytes LE int32 (length) + UTF-8 JSON body.
    ///
    /// Host: listens, tracks all clients by ID, re-broadcasts incoming messages.
    /// Client: connects to host, sends via host relay.
    /// </summary>
    public sealed class NetworkService
    {
        private TcpListener?   _listener;
        private TcpClient?     _hostClient;
        private readonly ConcurrentDictionary<string, TcpClient> _clients = new();
        private CancellationTokenSource? _cts;

        public bool   IsHost      { get; private set; }
        public string LocalId     { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public int    ClientCount => _clients.Count;
        public bool   IsConnected =>
            IsHost ? (_listener != null) : (_hostClient?.Connected == true);

        /// <summary>Fires on background thread. (senderId, json)</summary>
        public event Action<string, string>? MessageReceived;
        public event Action<string>? ClientConnected;
        public event Action<string>? ClientDisconnected;

        // ── Host ─────────────────────────────────────────────────────────────
        public bool StartHost(int port)
        {
            Stop();
            try
            {
                _cts      = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                IsHost    = true;
                Task.Run(() => AcceptLoopAsync(_cts.Token));
                return true;
            }
            catch { _listener = null; return false; }
        }

        // ── Client ───────────────────────────────────────────────────────────
        public bool Join(string host, int port)
        {
            Stop();
            try
            {
                _cts        = new CancellationTokenSource();
                _hostClient = new TcpClient();
                _hostClient.Connect(host, port);
                IsHost      = false;
                Task.Run(() => ClientReadLoopAsync(_hostClient, _cts.Token));
                return true;
            }
            catch { _hostClient = null; return false; }
        }

        // ── Stop ─────────────────────────────────────────────────────────────
        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _hostClient?.Close(); } catch { }
            foreach (var kv in _clients) try { kv.Value.Close(); } catch { }
            _clients.Clear();
            _listener = null; _hostClient = null; IsHost = false;
        }

        // ── Send helpers ─────────────────────────────────────────────────────
        public Task SendAsync(string json) =>
            IsHost ? BroadcastAsync(json) : SendToHostAsync(json);

        public async Task BroadcastAsync(string json)
        {
            var dead = new List<string>();
            foreach (var kv in _clients)
                try   { await WriteFramedAsync(kv.Value.GetStream(), json); }
                catch { dead.Add(kv.Key); }
            RemoveDead(dead);
        }

        public async Task SendToClientAsync(string clientId, string json)
        {
            if (!_clients.TryGetValue(clientId, out var client)) return;
            try   { await WriteFramedAsync(client.GetStream(), json); }
            catch { RemoveDead(new List<string> { clientId }); }
        }

        public async Task SendToHostAsync(string json)
        {
            if (_hostClient?.Connected != true) return;
            try   { await WriteFramedAsync(_hostClient.GetStream(), json); }
            catch { }
        }

        // ── Internal loops ───────────────────────────────────────────────────
        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var client   = await _listener.AcceptTcpClientAsync(ct);
                    var clientId = Guid.NewGuid().ToString("N").Substring(0, 8);
                    _clients[clientId] = client;
                    ClientConnected?.Invoke(clientId);
                    _ = Task.Run(() => HostReadLoopAsync(clientId, client, ct));
                }
                catch { break; }
            }
        }

        private async Task HostReadLoopAsync(string clientId, TcpClient client, CancellationToken ct)
        {
            var stream = client.GetStream();
            while (!ct.IsCancellationRequested && client.Connected)
            {
                var json = await ReadFramedAsync(stream, ct);
                if (json == null) break;
                MessageReceived?.Invoke(clientId, json);
                await BroadcastExceptAsync(clientId, json);
            }
            Cleanup(clientId, client);
        }

        private async Task ClientReadLoopAsync(TcpClient client, CancellationToken ct)
        {
            var stream = client.GetStream();
            while (!ct.IsCancellationRequested && client.Connected)
            {
                var json = await ReadFramedAsync(stream, ct);
                if (json == null) break;
                MessageReceived?.Invoke("host", json);
            }
            try { client.Close(); } catch { }
        }

        private async Task BroadcastExceptAsync(string excludeId, string json)
        {
            var dead = new List<string>();
            foreach (var kv in _clients)
            {
                if (kv.Key == excludeId) continue;
                try   { await WriteFramedAsync(kv.Value.GetStream(), json); }
                catch { dead.Add(kv.Key); }
            }
            RemoveDead(dead);
        }

        // ── Framing ──────────────────────────────────────────────────────────
        private static async Task WriteFramedAsync(NetworkStream stream, string json)
        {
            var body   = Encoding.UTF8.GetBytes(json);
            var header = BitConverter.GetBytes(body.Length);
            await stream.WriteAsync(header, 0, 4);
            await stream.WriteAsync(body,   0, body.Length);
            await stream.FlushAsync();
        }

        private static async Task<string?> ReadFramedAsync(NetworkStream stream, CancellationToken ct)
        {
            var hdr = new byte[4];
            if (!await ReadExactAsync(stream, hdr, 4, ct)) return null;
            var length = BitConverter.ToInt32(hdr, 0);
            if (length <= 0 || length > 4_000_000) return null;
            var body = new byte[length];
            if (!await ReadExactAsync(stream, body, length, ct)) return null;
            return Encoding.UTF8.GetString(body);
        }

        private static async Task<bool> ReadExactAsync(
            NetworkStream stream, byte[] buf, int count, CancellationToken ct)
        {
            var got = 0;
            while (got < count)
            {
                var n = await stream.ReadAsync(buf, got, count - got, ct);
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private void Cleanup(string id, TcpClient c)
        {
            try { c.Close(); } catch { }
            _clients.TryRemove(id, out _);
            ClientDisconnected?.Invoke(id);
        }

        private void RemoveDead(List<string> ids)
        {
            foreach (var id in ids)
            {
                if (_clients.TryRemove(id, out var c)) try { c.Close(); } catch { }
                ClientDisconnected?.Invoke(id);
            }
        }

        public ICollection<string> GetClientIds() => _clients.Keys;
    }
}
