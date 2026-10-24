// Minimal raw-socket listener that owns the API port while Kestrel is down.
// On incoming connection: sends an HTML loading page with meta-refresh, stops
// to free the port, then starts Kestrel via callback.
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WinActivityTracker.Service;

public class PortWatcher
{
    private readonly int _port;
    private readonly Func<Task> _onWake;
    private Socket? _listener;
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();

    public bool IsListening { get; private set; }

    public PortWatcher(int port, Func<Task> onWake)
    {
        _port = port;
        _onWake = onWake;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (IsListening) return;

            var cts = new CancellationTokenSource();
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                listener.Bind(new IPEndPoint(IPAddress.Loopback, _port));
                listener.Listen(1);
            }
            catch
            {
                listener.Dispose();
                cts.Dispose();
                throw;
            }
            _cts = cts;
            _listener = listener;
            IsListening = true;
            _ = AcceptLoop(listener, cts.Token);
        }
    }

    public void Stop() => Stop(null);

    private bool Stop(Socket? expectedListener)
    {
        CancellationTokenSource? cts;
        lock (_lock)
        {
            if (!IsListening || (expectedListener != null && _listener != expectedListener)) return false;

            cts = _cts;
            _cts = null;
            cts?.Cancel();
            try { _listener?.Close(); } catch { }
            try { _listener?.Dispose(); } catch { }
            _listener = null;
            IsListening = false;
        }
        cts?.Dispose();
        return true;
    }

    private async Task AcceptLoop(Socket listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await listener.AcceptAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }

            // Loading page with meta-refresh: the browser retries every 1s
            // until Kestrel is up and serving the dashboard.
            try
            {
                await SendLoadingPage(client, ct);
            }
            catch { /* client may disconnect early */ }

            try { client.Close(); } catch { }
            try { client.Dispose(); } catch { }

            // Free the port, then wake Kestrel.
            if (ct.IsCancellationRequested || !Stop(listener)) break;
            try { await _onWake(); }
            catch (Exception ex)
            {
                Console.WriteLine($"Dashboard wake failed: {ex.Message}");
                try { Start(); } catch { /* A busy port remains retryable. */ }
            }
            break;
        }
    }

    private static async Task SendLoadingPage(Socket client, CancellationToken ct)
    {
        var html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
                   "<meta http-equiv=\"refresh\" content=\"1\">" +
                   "<title>Loading...</title><style>" +
                   "body{font-family:system-ui;display:flex;justify-content:center;" +
                   "align-items:center;height:100vh;margin:0;background:#1e1e1e;color:#ccc}" +
                   "</style></head><body><p>Loading...</p></body></html>";

        var response = "HTTP/1.1 200 OK\r\n" +
                       "Content-Type: text/html; charset=utf-8\r\n" +
                       $"Content-Length: {Encoding.UTF8.GetByteCount(html)}\r\n" +
                       "Connection: close\r\n" +
                       "\r\n" + html;

        var bytes = Encoding.UTF8.GetBytes(response);
        _ = await client.SendAsync(bytes, SocketFlags.None, ct);
    }
}
