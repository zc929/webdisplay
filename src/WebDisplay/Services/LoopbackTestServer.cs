using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebDisplay.Services;

// Only constructed by the explicitly requested --smoke-test developer mode.
// Loopback-only HTTP verifies recovery without disconnecting the user's network.
internal sealed class LoopbackTestServer : IDisposable
{
    private readonly byte[] _body;
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;
    private int _port, _requests;
    public string Url => "http://127.0.0.1:" + _port + "/";
    public int RequestCount => Volatile.Read(ref _requests);
    public LoopbackTestServer(string html) => _body = Encoding.UTF8.GetBytes(html);
    public void Start()
    {
        _stop = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = ServeAsync(_listener, _stop.Token);
    }
    private async Task ServeAsync(TcpListener listener, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token);
                _ = RespondAsync(client, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
    }
    private async Task RespondAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            string? first = await reader.ReadLineAsync(token);
            if (first == null) return;
            for (int i = 0; i < 100; i++) { if (string.IsNullOrEmpty(await reader.ReadLineAsync(token))) break; }
            if (!first.Contains("favicon.ico")) Interlocked.Increment(ref _requests);
            byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {_body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, token);
            await stream.WriteAsync(_body, token);
            await stream.FlushAsync(token);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (SocketException) { }
    }
    public void Stop() { _stop?.Cancel(); _listener?.Stop(); _stop?.Dispose(); _stop = null; _listener = null; }
    public void Dispose() => Stop();
}
