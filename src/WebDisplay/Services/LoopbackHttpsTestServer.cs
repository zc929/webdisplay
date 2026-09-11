using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WebDisplay.Services;

/// <summary>
/// Loopback-only HTTPS fixture for explicitly requested --smoke-test runs.
/// Development Node.js supplies TLS because Windows SChannel cannot use an
/// ephemeral private key. PEM key/certificate and HTML travel only through stdin;
/// no key files or certificate stores are created. Normal operation needs no Node.
/// </summary>
internal sealed class LoopbackHttpsTestServer : IDisposable
{
    private readonly object _gate = new();
    private readonly string _html;
    private ServerRun? _run;
    private int _port;
    private int _requests;
    private bool _disposed;

    // Arguments contain only constant JavaScript. Generated certificate material
    // never appears in arguments, environment variables, logs, or files.
    private const string ServerScript = """
        'use strict';
        const https = require('node:https');
        const readline = require('node:readline');
        const constants = require('node:constants');
        const input = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
        let server, initialized = false, stopping = false, count = 0;
        const sockets = new Set();
        const emit = value => process.stdout.write(JSON.stringify(value) + '\n');
        const stop = () => {
          if (stopping) return;
          stopping = true;
          input.close();
          if (server) server.close(() => process.exit(0));
          for (const socket of sockets) socket.destroy();
          if (!server) process.exit(0);
          setTimeout(() => process.exit(0), 250).unref();
        };
        const fail = () => { emit({ event: 'error' }); stop(); };
        process.on('uncaughtException', fail);
        process.on('unhandledRejection', fail);
        process.stdout.on('error', stop);
        input.on('close', stop);
        input.on('line', line => {
          try {
            const config = JSON.parse(line);
            if (config.command === 'stop') { stop(); return; }
            if (initialized || config.command !== 'start') { fail(); return; }
            initialized = true;
            const body = Buffer.from(config.html, 'utf8');
            server = https.createServer({
              key: config.key, cert: config.certificate,
              minVersion: 'TLSv1.2', maxVersion: 'TLSv1.3',
              ALPNProtocols: ['http/1.1'], secureOptions: constants.SSL_OP_NO_TICKET,
              handshakeTimeout: 15000, maxHeaderSize: 16384
            }, (request, response) => {
              const favicon = (request.url || '').split('?')[0].toLowerCase() === '/favicon.ico';
              if (request.method !== 'GET' && request.method !== 'HEAD') {
                response.writeHead(405, { 'Connection': 'close' }); response.end(); return;
              }
              if (!favicon) emit({ event: 'request', count: ++count });
              const headers = {
                'Content-Type': 'text/html; charset=utf-8',
                'Cache-Control': 'no-store, no-cache, max-age=0',
                'Pragma': 'no-cache', 'Connection': 'close'
              };
              if (!favicon) headers['Content-Length'] = body.length;
              response.writeHead(favicon ? 204 : 200, headers);
              response.end(favicon || request.method === 'HEAD' ? undefined : body);
            });
            server.headersTimeout = 15000;
            server.requestTimeout = 15000;
            server.timeout = 15000;
            server.on('connection', socket => {
              sockets.add(socket);
              socket.on('close', () => sockets.delete(socket));
              socket.on('error', () => {});
            });
            server.on('tlsClientError', () => {});
            server.on('clientError', (_, socket) => socket.destroy());
            server.on('newSession', (_, __, callback) => callback());
            server.on('resumeSession', (_, callback) => callback(null, null));
            server.on('error', fail);
            server.listen(0, '127.0.0.1', () => emit({ event: 'ready', port: server.address().port }));
          } catch (_) { fail(); }
        });
        """;

    /// <summary>Call Start first; a new Start after Stop selects a fresh port.</summary>
    public string Url => "https://127.0.0.1:" + Volatile.Read(ref _port).ToString(CultureInfo.InvariantCulture) + "/";

    /// <summary>
    /// Complete GET/HEAD page requests excluding favicon and rejected handshakes.
    /// Cumulative across starts; updates arrive asynchronously over child stdout.
    /// </summary>
    public int RequestCount => Volatile.Read(ref _requests);

    public LoopbackHttpsTestServer(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        _html = html;
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_run is not null) return;
            var start = new ProcessStartInfo(ResolveNodeExecutable())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add("--eval");
            start.ArgumentList.Add(ServerScript);
            // Ambient developer preloads must not gain access to the test key.
            start.Environment.Remove("NODE_OPTIONS");
            start.Environment.Remove("NODE_PATH");
            var run = new ServerRun(new Process { StartInfo = start });
            try
            {
                if (!run.Process.Start()) throw new InvalidOperationException("Node.js 未启动。");
                _run = run;
                _ = ReadOutputAsync(run);
                _ = DrainErrorsAsync(run);
                StartRunAsync(run).GetAwaiter().GetResult();
                Volatile.Write(ref _port, run.Ready.Task.GetAwaiter().GetResult());
            }
            catch
            {
                if (ReferenceEquals(_run, run)) _run = null;
                StopRun(run);
                throw;
            }
        }
    }

    private async Task StartRunAsync(ServerRun run)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddHours(2));
        string configuration = JsonSerializer.Serialize(new
        {
            command = "start",
            key = key.ExportPkcs8PrivateKeyPem(),
            certificate = certificate.ExportCertificatePem(),
            html = _html
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await run.Process.StandardInput.WriteLineAsync(configuration.AsMemory(), timeout.Token).ConfigureAwait(false);
            await run.Process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
            await run.Ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("本机 HTTPS 测试服务器未在 10 秒内启动，请检查测试用 Node.js 可执行文件。");
        }
    }

    private async Task ReadOutputAsync(ServerRun run)
    {
        try
        {
            while (await run.Process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is string line)
            {
                using JsonDocument message = JsonDocument.Parse(line);
                JsonElement root = message.RootElement;
                string? kind = root.GetProperty("event").GetString();
                if (kind == "ready")
                {
                    int port = root.GetProperty("port").GetInt32();
                    if (port is < 1 or > 65535) throw new InvalidDataException();
                    run.Ready.TrySetResult(port);
                }
                else if (kind == "request")
                {
                    int count = root.GetProperty("count").GetInt32();
                    lock (_gate)
                    {
                        if (ReferenceEquals(_run, run) && count > run.LastCount)
                        {
                            Interlocked.Add(ref _requests, count - run.LastCount);
                            run.LastCount = count;
                        }
                    }
                }
                else if (kind == "error")
                    run.Ready.TrySetException(new InvalidOperationException("Node.js 无法初始化本机 HTTPS 测试服务器。"));
            }
        }
        catch (Exception)
        {
            // Never forward child output or exception text: these are not logs.
        }
        finally
        {
            run.Ready.TrySetException(new InvalidOperationException("本机 HTTPS 测试进程在报告监听端口前退出，请检查 Node.js。"));
        }
    }

    private static async Task DrainErrorsAsync(ServerRun run)
    {
        // Drain the pipe to prevent child deadlock, but never log its contents.
        char[] buffer = new char[1024];
        try
        {
            while (await run.Process.StandardError.ReadAsync(buffer.AsMemory()).ConfigureAwait(false) != 0) { }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
        finally { Array.Clear(buffer); }
    }

    private static string ResolveNodeExecutable()
    {
        string? configured = Environment.GetEnvironmentVariable("WEBDISPLAY_TEST_NODE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (Path.IsPathFullyQualified(configured) && File.Exists(configured)) return configured;
            throw new InvalidOperationException("WEBDISPLAY_TEST_NODE 必须指向现有 Node.js 可执行文件的绝对路径；此依赖仅用于 --smoke-test。");
        }
        foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            string directory = entry.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(directory)) continue;
            string candidate = Path.Combine(directory, "node.exe");
            if (File.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("HTTPS 冒烟测试需要 Node.js。请设置 WEBDISPLAY_TEST_NODE 为 node.exe 的绝对路径，或把 Node.js 加入 PATH。正常运行程序不需要 Node.js。");
    }

    public void Stop()
    {
        ServerRun? run;
        lock (_gate) { run = _run; _run = null; }
        if (run is not null) StopRun(run);
    }

    public void Dispose()
    {
        ServerRun? run;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            run = _run;
            _run = null;
        }
        if (run is not null) StopRun(run);
    }

    private static void StopRun(ServerRun run)
    {
        if (Interlocked.Exchange(ref run.Stopping, 1) != 0) return;
        try
        {
            if (run.Process.HasExited) return;
            try
            {
                run.Process.StandardInput.WriteLineAsync("{\"command\":\"stop\"}").Wait(TimeSpan.FromMilliseconds(100));
                run.Process.StandardInput.FlushAsync().Wait(TimeSpan.FromMilliseconds(100));
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or AggregateException) { }
            if (!run.Process.WaitForExit(750))
            {
                // This handle identifies only the child this fixture started.
                run.Process.Kill();
                run.Process.WaitForExit(1000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        finally { run.Process.Dispose(); }
    }

    private sealed class ServerRun
    {
        internal readonly Process Process;
        internal readonly TaskCompletionSource<int> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int LastCount;
        internal int Stopping;
        internal ServerRun(Process process) => Process = process;
    }
}
