using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCGuardianRemote;

internal sealed class AppConfig
{
    // Cloudflare Named Tunnel credentials (baked by IT)
    public string TunnelId { get; set; } = "";
    public string TunnelSecret { get; set; } = "";
    public string TunnelHostname { get; set; } = "";  // e.g. "client-pc.myitdomain.com"

    // Shell access
    public string Pin { get; set; } = "1234";
    public int Port { get; set; } = 7777;

    // Behavior
    public bool StartWithWindows { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public int ShellTimeoutMinutes { get; set; } = 30;

    // Paths
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PCGuardianRemote");
    static readonly string FilePath = Path.Combine(Dir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new();
            }
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Config] Save failed: {ex.Message}");
        }
    }

    public bool HasTunnelCredentials =>
        !string.IsNullOrWhiteSpace(TunnelId) &&
        !string.IsNullOrWhiteSpace(TunnelSecret) &&
        !string.IsNullOrWhiteSpace(TunnelHostname);
}
