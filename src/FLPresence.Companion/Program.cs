using FLPresence.Core;
using FLPresence.Discord;
using FLPresence.IPC;
using System.Diagnostics;

namespace FLPresence.Companion;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--uninstall")) { SelfInstaller.Uninstall(); return; }
        if (args.Contains("--watch")) { WatchLoop(); return; }
        // Downloaded exe (Releases): copy into place, register autostart, hand off.
        if (SelfInstaller.InstallIfNeeded()) return;

        using var mutex = new Mutex(true, "FLPresence_Companion_SingleInstance", out var first);
        if (!first) return; // already running: silently exit
        EnsureWatchdog();
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }

    private const string WatchMutexName = "FLPresence_Watchdog_SingleInstance";

    /// <summary>The watchdog normally starts at Windows login (Run key). If it
    /// is not running (fresh install, killed), start it now — otherwise the
    /// app would never come back after the next FL close.</summary>
    internal static void EnsureWatchdog()
    {
        if (Mutex.TryOpenExisting(WatchMutexName, out var m)) { m.Dispose(); return; }
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--watch")
            { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
        }
        catch { /* best-effort; Run key still starts it at next login */ }
    }

    /// <summary>
    /// Resident autostart launcher: sits in the background at Windows login and
    /// starts the tray app when FL Studio opens (the tray app exits itself a
    /// few seconds after FL closes). No window, no tray icon, no Discord.
    /// </summary>
    private static void WatchLoop()
    {
        using var mutex = new Mutex(true, WatchMutexName, out var first);
        if (!first) return; // one watchdog is enough
        while (true)
        {
            try
            {
                if (BridgeInstaller.IsFlRunning() && !TrayRunning())
                {
                    Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
                    {
                        UseShellExecute = false,
                        WorkingDirectory = AppContext.BaseDirectory,
                    });
                }
            }
            catch { /* FL check or launch failed - retry on the next tick */ }
            Thread.Sleep(2000);
        }
    }

    private static bool TrayRunning()
    {
        if (!Mutex.TryOpenExisting("FLPresence_Companion_SingleInstance", out var m)) return false;
        m.Dispose();
        return true;
    }
}

/// <summary>Owns the tray icon, services and wiring. No main window is shown.</summary>
public sealed class TrayAppContext : ApplicationContext
{
    private readonly FileLogger _log;
    private AppSettings _settings;
    private readonly PresenceEngine _engine;
    private readonly UdpBridgeListener _listener;
    private readonly DiscordPresenceService _discord;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _tick;

    private SettingsForm? _settingsForm;
    private DiagnosticsForm? _diagForm;
    private FlState? _lastDiffedState;
    private DateTime _lastPingLogUtc = DateTime.MinValue;
    private DateTime? _flGoneSince;
    private readonly FlWindowMonitor _native = new();

    public TrayAppContext()
    {
        _log = new FileLogger(LogLevel.Debug);
        _settings = AppSettings.Load();
        _engine = new PresenceEngine(_settings);
        _listener = new UdpBridgeListener(BridgeProtocol.DefaultPort);
        _discord = new DiscordPresenceService(_log);

        _listener.MessageReceived += OnBridgeMessage;
        try { _listener.Start(); _log.Info($"IPC listening on 127.0.0.1:{_listener.Port}"); }
        catch (Exception ex) { _log.Error("IPC listener failed to start", ex); }

        _tray = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
            Text = "FLPresence",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => OpenSettings();

        _tick = new System.Windows.Forms.Timer { Interval = 250 };
        _tick.Tick += (_, _) => Pump();
        _tick.Start();

        if (!BridgeInstaller.BridgeInstalled()) DeployBridge();
        BridgeInstaller.EnsureWatchAutostart();
        if (!_settings.DiscordConfigured && _settings.EnablePresence)
            _tray.ShowBalloonTip(6000, "FLPresence",
                "Discord Application ID is not set. Open Settings to configure it (see DISCORD_SETUP.md).",
                ToolTipIcon.Info);

        _log.Info("FLPresence Companion started");
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        void Check(string text, bool initial, EventHandler handler)
        {
            var item = new ToolStripMenuItem(text) { Checked = initial, CheckOnClick = true };
            item.Click += handler;
            menu.Items.Add(item);
        }

        var open = new ToolStripMenuItem("Open Settings");
        open.Click += (_, _) => OpenSettings();
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());

        Check("Enable Discord Presence", _settings.EnablePresence, (_, _) =>
        {
            _settings.EnablePresence = !_settings.EnablePresence;
            PersistSettings();
            if (!_settings.EnablePresence) _discord.Clear();
        });
        Check("Secret Mode", _settings.SecretMode, (_, _) =>
        {
            _settings.SecretMode = !_settings.SecretMode;
            PersistSettings();
        });
        menu.Items.Add(new ToolStripSeparator());

        Check("Start with FL Studio (autostart)", BridgeInstaller.GetStartWithWindows(), (_, _) =>
        {
            var enable = !BridgeInstaller.GetStartWithWindows();
            try { BridgeInstaller.SetStartWithWindows(enable); }
            catch (Exception ex) { _log.Error("Run key update failed", ex); }
        });
        menu.Items.Add(new ToolStripSeparator());

        var reinstall = new ToolStripMenuItem("Reinstall FL Bridge");
        reinstall.Click += (_, _) => DeployBridge();
        menu.Items.Add(reinstall);

        var restart = new ToolStripMenuItem("Restart Bridge");
        restart.Click += (_, _) => RestartBridge();
        menu.Items.Add(restart);
        menu.Items.Add(new ToolStripSeparator());

        var diag = new ToolStripMenuItem("Diagnostics");
        diag.Click += (_, _) => OpenDiagnostics();
        menu.Items.Add(diag);

        var about = new ToolStripMenuItem("About");
        about.Click += (_, _) => MessageBox.Show(
            "FLPresence 1.3.1\n\nDiscord Rich Presence for FL Studio.\n" +
            $"Bridge: UDP 127.0.0.1:{BridgeProtocol.DefaultPort} (localhost only)\n" +
            "Official FL Studio MIDI Scripting API — no memory reading.\n" +
            "Works without any MIDI keyboard (native window tracking); " +
            "a connected bridge adds transport/pattern/live notes.\n\n" +
            $"Log: {_log.FilePath}",
            "About FLPresence", MessageBoxButtons.OK, MessageBoxIcon.Information);
        menu.Items.Add(about);

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(exit);
        return menu;
    }

    // --- bridge messages ---------------------------------------------------

    private void OnBridgeMessage(BridgeMessage msg)
    {
        try
        {
            switch (msg.Type)
            {
                case "state":
                    if (msg.State != null)
                    {
                        _engine.OnState(msg.State);
                        _log.Debug($"state: project='{msg.State.Project}' playing={msg.State.Playing} " +
                                   $"rec={msg.State.Recording} bpm={msg.State.Bpm} pat={msg.State.Pattern}");
                        // Visible-change log (BPM/pattern/transport/...).
                        foreach (var line in FlStateDiffer.Diff(_lastDiffedState, msg.State))
                            _log.Info(line);
                        _lastDiffedState = msg.State;
                    }
                    break;
                case "notes":
                    if (msg.Notes != null)
                    {
                        _engine.OnNotes(msg.Notes);
                        _log.Debug($"notes: [{string.Join(",", msg.Notes.Notes)}] count={msg.Notes.NotesCount}");
                    }
                    break;
                case "ping":
                    // Keepalive from the bridge thread; log sparsely.
                    if (msg.Session is { } s) _engine.OnPing(s);
                    if (DateTime.UtcNow - _lastPingLogUtc > TimeSpan.FromSeconds(60))
                    {
                        _lastPingLogUtc = DateTime.UtcNow;
                        _log.Info($"FL Studio bridge alive (ping, {msg.Source ?? "unknown port"})");
                    }
                    break;
                case "error":
                    _log.Error($"Bridge script error: {msg.ErrorText}");
                    _engine.LastError = $"bridge: {msg.ErrorText}";
                    break;
                case "hello":
                    _log.Info($"FL Studio bridge connected (hello, {msg.Source ?? "unknown port"})");
                    break;
                case "bye":
                    _log.Info("FL Studio bridge said bye");
                    _engine.OnBye();
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Error("Bridge message handling failed", ex);
        }
    }

    // --- main pump ----------------------------------------------------------

    private void Pump()
    {
        try
        {
            // The app lives exactly as long as FL Studio: exit shortly after FL
            // closes (grace covers FL restarting itself); the watchdog relaunches.
            if (BridgeInstaller.IsFlRunning()) _flGoneSince = null;
            else
            {
                _flGoneSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - _flGoneSince > TimeSpan.FromSeconds(5))
                {
                    _log.Info("FL Studio closed - exiting (watchdog relaunches on next FL start)");
                    ExitApp();
                    return;
                }
            }

            // Native (no-bridge) source: FL process + window title. Presence is
            // shown only while FL has focus; everywhere else Discord is clean.
            var nativeState = _native.Poll(BridgeInstaller.IsFlRunning());
            if (nativeState != null)
            {
                if (_engine.OnNative(nativeState, _native.Foreground) && !_engine.BridgeAlive)
                {
                    _log.Info("FL Studio lost focus — presence hidden");
                    _discord.Clear();
                }
            }
            else if (_engine.ClearNative() && !_engine.BridgeAlive)
            {
                _log.Info("FL Studio gone — native presence cleared");
                _discord.Clear();
            }

            if (_engine.ExpireBridge(_settings.BridgeTimeoutSeconds))
            {
                _log.Info("Bridge timed out — clearing presence");
                _discord.Clear();
            }
            if (!_settings.EnablePresence) return;

            if (_settings.DiscordConfigured)
            {
                _discord.Connect(_settings.DiscordApplicationId);
                _discord.EnsureConnected();
            }
            var payload = _engine.Tick(DateTime.UtcNow, _settings.ShowSessionTimer);
            if (payload != null)
            {
                _log.Debug($"presence: [{payload.Mode}] '{payload.Details}' | '{payload.State}' [{payload.SmallImageKey}]");
                if (_settings.DiscordConfigured)
                {
                    _discord.EnsureConnected();
                    // Not delivered (Discord still connecting): re-push next tick,
                    // otherwise dedup would swallow it until something changes.
                    if (!_discord.Update(payload)) _engine.Invalidate();
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("Pump failed", ex);
        }
    }

    // --- menu actions ---------------------------------------------------------

    private void DeployBridge()
    {
        try
        {
            var path = BridgeInstaller.DeployBridge(_log);
            var (assigned, used) = BridgeInstaller.TryAutoAssignScript();
            _log.Info($"Script auto-assign: {assigned} device(s) assigned, {used} already using another script");
            string extra = assigned > 0
                ? $"Auto-assigned to {assigned} MIDI input device(s) — restart FL Studio."
                : "In FL Studio: Options → MIDI Settings → Input → Controller type → \"FLPresence\".";
            _tray.ShowBalloonTip(8000, "FLPresence Bridge installed", $"{extra}\nPath: {path}", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _log.Error("Bridge deployment failed", ex);
            MessageBox.Show(ex.Message, "FLPresence — bridge install failed",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RestartBridge()
    {
        _listener.Stop();
        _engine.OnBye();
        _discord.Clear();
        try { _listener.Start(); }
        catch (Exception ex) { _log.Error("IPC listener restart failed", ex); }
        _engine.Invalidate();
        _log.Info("Bridge IPC restarted; waiting for FL Studio heartbeat");
    }

    private void PersistSettings()
    {
        try { _settings.Save(); }
        catch (Exception ex) { _log.Error("Settings save failed", ex); }
        _engine.Invalidate();
    }

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false }) { _settingsForm.Activate(); return; }
        _settingsForm = new SettingsForm(_settings, _log);
        _settingsForm.Saved += s =>
        {
            var idChanged = !string.Equals(_settings.DiscordApplicationId, s.DiscordApplicationId, StringComparison.Ordinal);
            _settings = s;
            PersistSettings();
            if (idChanged || !_settings.EnablePresence)
            {
                _discord.Clear();
                _discord.Deinitialize();
            }
        };
        _settingsForm.Show();
    }

    private void OpenDiagnostics()
    {
        if (_diagForm is { IsDisposed: false }) { _diagForm.Activate(); return; }
        _diagForm = new DiagnosticsForm(this, _log);
        _diagForm.Show();
    }

    // diagnostics accessors
    internal PresenceEngine Engine => _engine;
    internal AppSettings Settings => _settings;
    internal DiscordPresenceService Discord => _discord;

    private void ExitApp()
    {
        try { _discord.Clear(); } catch (Exception) { }
        _tick.Stop();
        _listener.Stop();
        _tray.Visible = false;
        _discord.Dispose();
        _log.Info("FLPresence Companion exited");
        Application.Exit();
    }
}
