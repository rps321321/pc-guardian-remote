using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace PCGuardianRemote;

/// <summary>
/// HTTP server with WebSocket PowerShell terminal.
/// Serves a terminal web page and handles shell sessions.
/// </summary>
internal sealed class ShellServer : IDisposable
{
    HttpListener? _listener;
    Thread? _thread;
    volatile bool _stopped;
    int _shellActiveInt;
    readonly string _pin;
    readonly int _port;
    readonly int _timeoutMinutes;

    public ShellServer(string pin, int port, int timeoutMinutes = 30)
    {
        _pin = string.IsNullOrWhiteSpace(pin) ? GeneratePin() : pin;
        _port = port;
        _timeoutMinutes = timeoutMinutes;
    }

    public string ActivePin => _pin;

    public void Start()
    {
        // Open firewall port
        try
        {
            var psi = new ProcessStartInfo("netsh",
                $"advfirewall firewall add rule name=\"PCGuardianRemote\" dir=in action=allow protocol=tcp localport={_port}")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch { }

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{_port}/");
        _listener.Start();

        _thread = new Thread(ListenLoop) { IsBackground = true, Name = "ShellServer" };
        _thread.Start();
    }

    void ListenLoop()
    {
        while (!_stopped)
        {
            try
            {
                var ctx = _listener?.GetContext();
                if (ctx == null) break;
                if (ctx.Request.IsWebSocketRequest
                    && ctx.Request.Url?.AbsolutePath?.TrimEnd('/').Equals("/shell", StringComparison.OrdinalIgnoreCase) == true)
                    _ = Task.Run(() => HandleWebSocket(ctx));
                else
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { }
        }
    }

    void HandleRequest(HttpListenerContext ctx)
    {
        var res = ctx.Response;
        var path = ctx.Request.Url?.AbsolutePath?.TrimEnd('/') ?? "";
        try
        {
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                if (!PinMatches(ctx.Request.QueryString["pin"], _pin))
                { res.StatusCode = 401; WriteJson(res, new { error = "Invalid PIN" }); return; }
            }

            if (path.Equals("/terminal", StringComparison.OrdinalIgnoreCase) || path == "")
            {
                res.ContentType = "text/html; charset=utf-8";
                var bytes = Encoding.UTF8.GetBytes(TerminalPage.Generate(Environment.MachineName));
                res.ContentLength64 = bytes.Length;
                res.OutputStream.Write(bytes);
                return;
            }

            if (path.Equals("/api/metrics", StringComparison.OrdinalIgnoreCase))
            {
                WriteJson(res, new
                {
                    host = Environment.MachineName,
                    os = Environment.OSVersion.ToString(),
                    uptime = (long)TimeSpan.FromMilliseconds(Environment.TickCount64).TotalMinutes,
                    timestamp = DateTime.Now.ToString("o"),
                });
                return;
            }

            res.StatusCode = 404;
            WriteJson(res, new { error = "Not found" });
        }
        catch (Exception ex) { try { res.StatusCode = 500; WriteJson(res, new { error = ex.Message }); } catch { } }
        finally { try { res.Close(); } catch { } }
    }

    async Task HandleWebSocket(HttpListenerContext ctx)
    {
        if (!PinMatches(ctx.Request.QueryString["pin"], _pin))
        { ctx.Response.StatusCode = 401; ctx.Response.Close(); return; }

        if (Interlocked.CompareExchange(ref _shellActiveInt, 1, 0) != 0)
        {
            var wsReject = await ctx.AcceptWebSocketAsync(null);
            await wsReject.WebSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Another session active", CancellationToken.None);
            wsReject.WebSocket.Dispose();
            return;
        }

        WebSocket? ws = null;
        Process? shell = null;
        using var cts = new CancellationTokenSource();
        try
        {
            ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket;
            shell = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoLogo -NoProfile -NoExit -Command -",
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                },
                EnableRaisingEvents = true
            };
            shell.Exited += (_, _) => { try { cts.Cancel(); } catch { } };
            shell.Start();
            await shell.StandardInput.WriteLineAsync("[Console]::OutputEncoding = [Text.Encoding]::UTF8");
            await shell.StandardInput.FlushAsync();

            var token = cts.Token;
            var timeout = new System.Threading.Timer(_ => { try { cts.Cancel(); } catch { } },
                null, TimeSpan.FromMinutes(_timeoutMinutes), Timeout.InfiniteTimeSpan);

            _ = Task.Run(async () => { var buf = new byte[4096]; try { while (ws.State == WebSocketState.Open) { int n = await shell.StandardOutput.BaseStream.ReadAsync(buf, 0, buf.Length, token); if (n == 0) break; await ws.SendAsync(new ArraySegment<byte>(buf, 0, n), WebSocketMessageType.Text, true, CancellationToken.None); } } catch { } }, token);
            _ = Task.Run(async () => { var buf = new byte[4096]; try { while (ws.State == WebSocketState.Open) { int n = await shell.StandardError.BaseStream.ReadAsync(buf, 0, buf.Length, token); if (n == 0) break; await ws.SendAsync(new ArraySegment<byte>(buf, 0, n), WebSocketMessageType.Text, true, CancellationToken.None); } } catch { } }, token);

            var recvBuf = new byte[4096];
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(recvBuf), token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                await shell.StandardInput.WriteAsync(Encoding.UTF8.GetString(recvBuf, 0, result.Count));
                await shell.StandardInput.FlushAsync();
                timeout.Change(TimeSpan.FromMinutes(_timeoutMinutes), Timeout.InfiniteTimeSpan);
            }
            timeout.Dispose();
        }
        catch { }
        finally
        {
            Interlocked.Exchange(ref _shellActiveInt, 0);
            if (shell is { HasExited: false }) try { shell.Kill(); } catch { }
            shell?.Dispose();
            if (ws?.State == WebSocketState.Open)
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
            ws?.Dispose();
        }
    }

    static bool PinMatches(string? submitted, string stored)
    {
        if (submitted is null) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(submitted), Encoding.UTF8.GetBytes(stored));
    }

    static void WriteJson(HttpListenerResponse res, object data)
    {
        res.ContentType = "application/json";
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes);
    }

    static string GeneratePin() => RandomNumberGenerator.GetInt32(100000, 999999).ToString();

    public void Dispose() { _stopped = true; _listener?.Close(); _listener = null; }
}
