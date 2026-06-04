using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TormentaVTT.Services
{
    // Minimal TCP-based network service for basic host/join and message passing.
    public sealed class NetworkService
    {
        private TcpListener? _listener;
        private TcpClient? _client;
        private CancellationTokenSource? _cts;

        public bool IsHost { get; private set; }

        public event Action<string>? MessageReceived;

        public bool StartHost(int port)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                _cts = new CancellationTokenSource();
                IsHost = true;
                Task.Run(() => AcceptLoopAsync(_cts.Token));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool Join(string host, int port)
        {
            try
            {
                _client = new TcpClient();
                _client.Connect(host, port);
                _cts = new CancellationTokenSource();
                Task.Run(() => ClientReceiveLoopAsync(_client, _cts.Token));
                IsHost = false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
            }
            catch { }
            try { _listener?.Stop(); } catch { }
            try { _client?.Close(); } catch { }
            _listener = null;
            _client = null;
            IsHost = false;
        }

        public bool IsConnected()
        {
            if (IsHost) return _listener != null;
            return _client != null && _client.Connected;
        }

        public async Task SendMessageAsync(string text)
        {
            try
            {
                var data = Encoding.UTF8.GetBytes(text + "\n");
                if (IsHost && _listener != null)
                {
                    // For host, accept one connection and send if exists
                    // (This minimal implementation does not track multiple clients.)
                    // No-op in host for now.
                }
                else if (_client != null && _client.Connected)
                {
                    var stream = _client.GetStream();
                    await stream.WriteAsync(data, 0, data.Length);
                }
            }
            catch { }
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(ct);
                    _ = Task.Run(() => ClientReceiveLoopAsync(client, ct));
                }
                catch { break; }
            }
        }

        private async Task ClientReceiveLoopAsync(TcpClient client, CancellationToken ct)
        {
            var stream = client.GetStream();
            var buf = new byte[4096];
            while (!ct.IsCancellationRequested && client.Connected)
            {
                try
                {
                    var len = await stream.ReadAsync(buf, 0, buf.Length, ct);
                    if (len <= 0) break;
                    var txt = Encoding.UTF8.GetString(buf, 0, len).Trim();
                    MessageReceived?.Invoke(txt);
                }
                catch { break; }
            }
            try { client.Close(); } catch { }
        }
    }
}
