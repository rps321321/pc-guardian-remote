using System.Diagnostics;
using System.Text.Json;

namespace PCGuardianRemote;

/// <summary>
/// Manages cloudflared — extracts from embedded resource, runs named tunnel.
/// Supports both Named Tunnels (fixed URL) and Quick Tunnels (random URL fallback).
/// </summary>
internal sealed class TunnelManager : IDisposable
{
    Process? _proc;
    volatile bool _disposed;
    string? _cloudflaredPath;

    public string? TunnelUrl { get; private set; }
    public bool IsConnected => TunnelUrl != null && _proc is { HasExited: false };
    public string? ErrorMessage { get; private set; }

    public event Action<string>? UrlAssigned;
    public event Action<string>? StatusChanged;

    /// <summary>
    /// Start a named tunnel (fixed URL) using tunnel credentials.
    /// Falls back to quick tunnel if no credentials provided.
    /// </summary>
    public void Start(AppConfig config)
    {
        if (_disposed) return;

        _cloudflaredPath = FindOrExtract();
        if (_cloudflaredPath == null)
        {
            ErrorMessage = "Could not find or extract cloudflared.exe";
            StatusChanged?.Invoke(ErrorMessage);
            return;
        }

        if (!config.HasTunnelCredentials)
        {
            ErrorMessage = "No tunnel credentials configured. Set TunnelId, TunnelSecret, and TunnelHostname in config.";
            StatusChanged?.Invoke(ErrorMessage);
            return;
        }

        StartNamedTunnel(config);
    }

    void StartNamedTunnel(AppConfig config)
    {
        // Write tunnel credentials to a temp file
        var credDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PCGuardianRemote");
        Directory.CreateDirectory(credDir);
        var credPath = Path.Combine(credDir, "tunnel-creds.json");

        var creds = new
        {
            AccountTag = "",
            TunnelID = config.TunnelId,
            TunnelSecret = config.TunnelSecret,
        };
        File.WriteAllText(credPath, JsonSerializer.Serialize(creds));

        // Write config file for cloudflared
        var configPath = Path.Combine(credDir, "cloudflared-config.yml");
        var yaml = $"""
            tunnel: {config.TunnelId}
            credentials-file: {credPath.Replace("\\", "/")}
            ingress:
              - hostname: {config.TunnelHostname}
                service: http://localhost:{config.Port}
              - service: http_status:404
            """;
        File.WriteAllText(configPath, yaml);

        StatusChanged?.Invoke("Starting named tunnel...");

        var psi = new ProcessStartInfo
        {
            FileName = _cloudflaredPath,
            Arguments = $"tunnel --config \"{configPath}\" run",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _proc = Process.Start(psi);
        if (_proc == null)
        {
            ErrorMessage = "Failed to start cloudflared";
            StatusChanged?.Invoke(ErrorMessage);
            return;
        }

        // Named tunnel URL is known from config
        TunnelUrl = $"https://{config.TunnelHostname}";
        UrlAssigned?.Invoke(TunnelUrl);
        StatusChanged?.Invoke($"Connected: {TunnelUrl}");

        // Monitor stderr for connection status
        _proc.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            Debug.WriteLine($"[Tunnel] {e.Data}");

            if (e.Data.Contains("Registered tunnel connection", StringComparison.OrdinalIgnoreCase))
                StatusChanged?.Invoke($"Connected: {TunnelUrl}");
            else if (e.Data.Contains("ERR", StringComparison.OrdinalIgnoreCase))
                StatusChanged?.Invoke($"Tunnel error — retrying...");
        };
        _proc.BeginErrorReadLine();
    }

    // Quick tunnel removed — only named tunnels with fixed URLs are supported

    string? FindOrExtract()
    {
        // 1. Same directory as exe
        var appDir = Path.Combine(AppContext.BaseDirectory, "cloudflared.exe");
        if (File.Exists(appDir)) return appDir;

        // 2. Already extracted
        var cachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PCGuardianRemote", "cloudflared.exe");
        if (File.Exists(cachePath)) return cachePath;

        // 3. Extract from embedded resource
        try
        {
            StatusChanged?.Invoke("Extracting cloudflared (one-time)...");
            using var stream = typeof(TunnelManager).Assembly
                .GetManifestResourceStream("cloudflared.exe");
            if (stream == null) return null;

            var dir = Path.GetDirectoryName(cachePath)!;
            Directory.CreateDirectory(dir);

            var tmpPath = cachePath + ".tmp";
            using (var fs = File.Create(tmpPath))
                stream.CopyTo(fs);

            if (File.Exists(cachePath)) File.Delete(cachePath);
            File.Move(tmpPath, cachePath);
            return cachePath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Tunnel] Extract failed: {ex.Message}");
            return null;
        }
    }

    public void Stop()
    {
        if (_proc is { HasExited: false })
        {
            try { _proc.Kill(); } catch { }
            try { _proc.WaitForExit(3000); } catch { }
        }
        _proc?.Dispose();
        _proc = null;
        TunnelUrl = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
