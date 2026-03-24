using System.Drawing;
using Microsoft.Win32;

namespace PCGuardianRemote;

internal sealed class MainForm : Form
{
    readonly AppConfig _config;
    readonly ShellServer _server;
    readonly TunnelManager _tunnel;
    readonly NotifyIcon _tray;
    readonly Label _lblStatus;
    readonly Label _lblUrl;
    readonly Label _lblPin;
    readonly Label _lblLocal;

    public MainForm()
    {
        _config = AppConfig.Load();
        _server = new ShellServer(_config.Pin, _config.Port, _config.ShellTimeoutMinutes);
        _tunnel = new TunnelManager();

        // Window setup
        Text = "PC Guardian Remote";
        Size = new Size(480, 320);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 18, 27);
        ForeColor = Color.FromArgb(228, 228, 231);
        Font = new Font("Segoe UI", 10f);

        try
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("guardian.ico");
            if (stream != null) Icon = new Icon(stream);
        }
        catch { }

        // Title
        var lblTitle = new Label
        {
            Text = "PC Guardian Remote",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 16),
        };
        Controls.Add(lblTitle);

        // Status indicator
        _lblStatus = new Label
        {
            Text = "Starting...",
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(245, 158, 11),
            AutoSize = true,
            Location = new Point(20, 56),
        };
        Controls.Add(_lblStatus);

        // URL display
        var lblUrlLabel = new Label
        {
            Text = "Remote URL:",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(113, 113, 122),
            AutoSize = true,
            Location = new Point(20, 96),
        };
        Controls.Add(lblUrlLabel);

        _lblUrl = new Label
        {
            Text = "Connecting...",
            Font = new Font("Segoe UI Semibold", 11f),
            ForeColor = Color.FromArgb(99, 102, 241),
            AutoSize = true,
            Location = new Point(20, 116),
            Cursor = Cursors.Hand,
        };
        _lblUrl.Click += (_, _) =>
        {
            if (_tunnel.TunnelUrl != null)
            {
                Clipboard.SetText(_tunnel.TunnelUrl + "/terminal");
                _lblStatus.Text = "URL copied to clipboard!";
                _lblStatus.ForeColor = Color.FromArgb(16, 185, 129);
            }
        };
        Controls.Add(_lblUrl);

        // PIN display
        var lblPinLabel = new Label
        {
            Text = "PIN:",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(113, 113, 122),
            AutoSize = true,
            Location = new Point(20, 156),
        };
        Controls.Add(lblPinLabel);

        _lblPin = new Label
        {
            Text = _server.ActivePin,
            Font = new Font("Segoe UI Semibold", 14f),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 176),
        };
        Controls.Add(_lblPin);

        // Local URL
        _lblLocal = new Label
        {
            Text = $"Local: http://localhost:{_config.Port}/terminal",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(82, 82, 91),
            AutoSize = true,
            Location = new Point(20, 220),
        };
        Controls.Add(_lblLocal);

        // Copy button
        var btnCopy = new Button
        {
            Text = "Copy URL + PIN",
            Font = new Font("Segoe UI Semibold", 9f),
            BackColor = Color.FromArgb(99, 102, 241),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(140, 34),
            Location = new Point(20, 250),
            Cursor = Cursors.Hand,
        };
        btnCopy.FlatAppearance.BorderSize = 0;
        btnCopy.Click += (_, _) =>
        {
            var url = _tunnel.TunnelUrl ?? $"http://localhost:{_config.Port}";
            Clipboard.SetText($"{url}/terminal\nPIN: {_server.ActivePin}");
            _lblStatus.Text = "Copied to clipboard!";
            _lblStatus.ForeColor = Color.FromArgb(16, 185, 129);
        };
        Controls.Add(btnCopy);

        // Tray icon
        _tray = new NotifyIcon
        {
            Icon = Icon,
            Text = "PC Guardian Remote",
            Visible = true,
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add("Copy URL", null, (_, _) =>
        {
            var url = _tunnel.TunnelUrl ?? $"http://localhost:{_config.Port}";
            Clipboard.SetText($"{url}/terminal\nPIN: {_server.ActivePin}");
        });
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) => { _tray.Visible = false; Application.Exit(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };

        // Wire tunnel events
        _tunnel.UrlAssigned += url =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnUrlAssigned(url)); return; }
            OnUrlAssigned(url);
        };
        _tunnel.StatusChanged += status =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnStatusChanged(status)); return; }
            OnStatusChanged(status);
        };

        // Start everything
        StartServices();

        // Auto-start with Windows
        if (_config.StartWithWindows)
            SetStartup(true);
    }

    void StartServices()
    {
        // AV whitelist
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath != null)
            {
                var psi = new System.Diagnostics.ProcessStartInfo("powershell",
                    $"-NoProfile -Command \"Add-MpPreference -ExclusionPath '{exePath}' -ErrorAction SilentlyContinue\"")
                { CreateNoWindow = true, UseShellExecute = false };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(5000);
            }
        }
        catch { }

        // Start HTTP + WebSocket server
        try
        {
            _server.Start();
            _lblStatus.Text = "Server running on port " + _config.Port;
            _lblStatus.ForeColor = Color.FromArgb(16, 185, 129);
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Server error: " + ex.Message;
            _lblStatus.ForeColor = Color.FromArgb(239, 68, 68);
        }

        // Start tunnel
        _tunnel.Start(_config);
    }

    void OnUrlAssigned(string url)
    {
        _lblUrl.Text = url + "/terminal";
        _lblUrl.ForeColor = Color.FromArgb(99, 102, 241);
        _tray.Text = $"PC Guardian Remote\n{url}";

        // Save PIN to config if auto-generated
        _config.Pin = _server.ActivePin;
        _config.Save();
    }

    void OnStatusChanged(string status)
    {
        _lblStatus.Text = status;
        _lblStatus.ForeColor = status.Contains("Connected") || status.Contains("running")
            ? Color.FromArgb(16, 185, 129)
            : Color.FromArgb(245, 158, 11);
    }

    static void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (enable)
                key?.SetValue("PCGuardianRemote", $"\"{Environment.ProcessPath}\"");
            else
                key?.DeleteValue("PCGuardianRemote", false);
        }
        catch { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && _config.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _tunnel.Dispose();
        _server.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosing(e);
    }

    protected override void OnResize(EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized && _config.MinimizeToTray)
            Hide();
        base.OnResize(e);
    }
}
