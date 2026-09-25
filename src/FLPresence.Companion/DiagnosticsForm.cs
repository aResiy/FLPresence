using System.Diagnostics;
using FLPresence.Core;
using FLPresence.Discord;
using Microsoft.Win32;

namespace FLPresence.Companion;

/// <summary>Dark diagnostics window: FL Studio / MIDI Sources / Discord /
/// Current Presence sections, refreshed live.</summary>
public sealed class DiagnosticsForm : Form
{
    private readonly TrayAppContext _app;
    private readonly FileLogger _log;
    private readonly ListView _list = new();
    private readonly Button _loopMidiBtn;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };

    private static readonly Color Bg = Color.FromArgb(24, 24, 28);
    private static readonly Color Fg = Color.FromArgb(232, 232, 236);

    public DiagnosticsForm(TrayAppContext app, FileLogger log)
    {
        _app = app; _log = log;
        Text = "FLPresence — Diagnostics";
        BackColor = Bg; ForeColor = Fg;
        Font = new Font("Segoe UI", 9.5f);
        Size = new Size(620, 560);
        StartPosition = FormStartPosition.CenterScreen;

        _list.View = View.Details;
        _list.Dock = DockStyle.Fill;
        _list.FullRowSelect = true;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.ShowGroups = true;
        _list.BackColor = Color.FromArgb(32, 32, 38);
        _list.ForeColor = Fg;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.Columns.Add("Check", 230);
        _list.Columns.Add("Status", 340);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 40, BackColor = Bg, FlowDirection = FlowDirection.LeftToRight,
        };
        _loopMidiBtn = new Button
        {
            Text = "Set up virtual MIDI port (loopMIDI)…",
            AutoSize = true,
            BackColor = Color.FromArgb(45, 45, 52), ForeColor = Fg, FlatStyle = FlatStyle.Flat,
        };
        _loopMidiBtn.Click += (_, _) => SetupLoopMidi();
        buttons.Controls.Add(_loopMidiBtn);
        var logBtn = new Button
        {
            Text = "Open log folder", AutoSize = true,
            BackColor = Color.FromArgb(45, 45, 52), ForeColor = Fg, FlatStyle = FlatStyle.Flat,
        };
        logBtn.Click += (_, _) =>
        {
            try { Process.Start("explorer.exe", $"/select,\"{_log.FilePath}\""); }
            catch (Exception) { }
        };
        buttons.Controls.Add(logBtn);

        Controls.Add(_list);
        Controls.Add(buttons);

        _refresh.Tick += (_, _) => RefreshList();
        _refresh.Start();
        RefreshList();
    }

    private void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Groups.Clear();
        var gFl = new ListViewGroupsGroup("FL Studio");
        var gMidi = new ListViewGroupsGroup("MIDI Sources");
        var gDiscord = new ListViewGroupsGroup("Discord");
        var gPresence = new ListViewGroupsGroup("Current Presence");
        void Row(ListViewGroupsGroup g, string k, string v, bool ok)
        {
            var item = new ListViewItem(k) { ForeColor = ok ? Color.FromArgb(140, 220, 140) : Fg, Group = g.Native };
            item.SubItems.Add(v);
            _list.Items.Add(item);
        }

        // --- FL Studio ------------------------------------------------------
        var engine = _app.Engine;
        var flRunning = BridgeInstaller.IsFlRunning();
        var install = BridgeInstaller.DetectFlInstall();
        var st = engine.LastState;
        Row(gFl, "FL Studio", flRunning ? "running" : "not running", flRunning);
        Row(gFl, "Detected install", install is null ? "not found" : $"{install.Value.Name} {install.Value.Version}".Trim(), install != null);
        Row(gFl, "FL version (from bridge)", st?.FlVersion is { Length: > 0 } v ? v : "—", st?.FlVersion?.Length > 0);
        Row(gFl, "Bridge script", BridgeInstaller.BridgeInstalled() ? BridgeInstaller.BridgePath() ?? "" : "not installed", BridgeInstaller.BridgeInstalled());
        Row(gFl, "Bridge connected", engine.BridgeAlive ? "connected" : "not connected", engine.BridgeAlive);
        var last = engine.LastPacketUtc;
        Row(gFl, "Last bridge update", last == DateTime.MinValue ? "—" : $"{(DateTime.UtcNow - last).TotalSeconds:0}s ago", engine.BridgeAlive);
        if (st is not null)
        {
            Row(gFl, "Project", string.IsNullOrWhiteSpace(st.Project) ? "—" : st.Project, st.Project.Length > 0);
            Row(gFl, "Transport", st.Recording ? "Recording" : st.Playing ? "Playing" : "Stopped", true);
            Row(gFl, "BPM", st.Bpm?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—", st.Bpm != null);
            Row(gFl, "Pattern", string.IsNullOrWhiteSpace(st.Pattern) ? (st.PatternNumber is { } pn ? pn.ToString() : "—") : st.Pattern, st.PatternNumber != null);
            Row(gFl, "Channel / plugin", string.IsNullOrWhiteSpace(st.Channel) ? "—" : $"{st.Channel}{(string.IsNullOrWhiteSpace(st.Plugin) ? "" : $" ({st.Plugin})")}", st.Channel.Length > 0);
            Row(gFl, "Mixer track", string.IsNullOrWhiteSpace(st.MixerTrackName) ? st.MixerTrack?.ToString() ?? "—" : st.MixerTrackName, true);
            Row(gFl, "Metronome", st.Metronome ? "on" : "off", true);
        }

        // --- MIDI Sources ---------------------------------------------------
        var inputs = MidiDevices.GetInputs();
        var virtualPort = inputs.FirstOrDefault(d => d.Name == MidiDevices.VirtualPortName);
        var hasVirtual = virtualPort is not null ||
                         inputs.Any(d => d.Name.Contains(MidiDevices.LoopMidiMarker, StringComparison.OrdinalIgnoreCase));
        Row(gMidi, "MIDI sources found", inputs.Count == 0 ? "none (FL state still works via the virtual port)" : $"{inputs.Count} device(s)", true);
        foreach (var d in inputs)
        {
            var script = AssignedScript(d.Name);
            var label = d.Name == MidiDevices.VirtualPortName || d.Name.Contains(MidiDevices.LoopMidiMarker, StringComparison.OrdinalIgnoreCase)
                ? $"{d.Name} (virtual)" : d.Name;
            Row(gMidi, "Input", script is null ? label : $"{label} → script: {script}", script is not null);
        }
        if (inputs.Count == 0)
            Row(gMidi, "Input", "— none —", false);
        Row(gMidi, "Virtual port", hasVirtual
            ? $"present ({virtualPort?.Name ?? MidiDevices.LoopMidiMarker})"
            : "not installed — click the button below; keeps the bridge alive with no controller connected",
            hasVirtual);
        var vreg = BridgeInstaller.VirtualPortRegistryName();
        var vscript = vreg is null ? null : AssignedScript(vreg);
        Row(gMidi, "Bridge on virtual port", vreg is null ? "port not seen by FL yet" : vscript == "FLPresence" ? "assigned ✓" : "not assigned yet (restart FL after setup)", vscript == "FLPresence");


        // --- Discord --------------------------------------------------------
        var discord = _app.Discord;
        var s = _app.Settings;
        Row(gDiscord, "Discord detected", IsDiscordRunning() ? "running" : "not running", IsDiscordRunning());
        Row(gDiscord, "RPC connected", discord.RpcConnected ? "yes" : "no", discord.RpcConnected);
        Row(gDiscord, "RPC READY", discord.RpcConnected ? "true" : "false", discord.RpcConnected);
        Row(gDiscord, "Application ID", s.DiscordConfigured ? (discord.ClientId ?? s.DiscordApplicationId ?? "") : "not set", s.DiscordConfigured);
        Row(gDiscord, "Large asset key", DiscordPresenceService.LargeAssetKey + "  (must exist in the portal's Rich Presence assets)", true);
        var summary = discord.LastUpdateSummary;
        Row(gDiscord, "Small asset key", summary is { Length: > 0 } ? ExtractSmallKey(summary) : "—", true);
        Row(gDiscord, "Last SetActivity", discord.LastUpdateUtc is { } u ? $"{(DateTime.UtcNow - u).TotalSeconds:0}s ago" : "never", discord.LastUpdateUtc != null);
        Row(gDiscord, "Last error", string.IsNullOrEmpty(discord.LastError) ? "none" : discord.LastError, discord.LastError.Length == 0);
        Row(gDiscord, "Secret Mode", s.SecretMode ? "ON" : "off", true);
        Row(gDiscord, "Bridge errors", string.IsNullOrEmpty(engine.LastError) ? "none" : engine.LastError, engine.LastError.Length == 0);

        // --- Current Presence -------------------------------------------------
        var pres = discord.LastUpdateSummary;
        Row(gPresence, "Presence", pres is { Length: > 0 } ? pres : (engine.BridgeAlive ? "waiting for first push" : "— (no bridge)"), pres?.Length > 0);
        Row(gPresence, "Session start", engine.SessionStartUtc is { } ss ? ss.ToLocalTime().ToString("HH:mm:ss") : "—", engine.SessionStartUtc != null);
        Row(gPresence, "Session timer", s.ShowSessionTimer ? "enabled (Discord elapsed counter)" : "disabled", true);

        _loopMidiBtn.Visible = !hasVirtual;
        _list.EndUpdate();
    }

    private static string ExtractSmallKey(string summary)
    {
        var i = summary.IndexOf("small=");
        return i < 0 ? "—" : summary[(i + 6)..].TrimEnd(']', ' ');
    }

    private static string? AssignedScript(string deviceName)
    {
        try
        {
            using var dev = Registry.CurrentUser.OpenSubKey(
                $@"Software\Image-Line\FL Studio 24\Devices\MIDI input\{deviceName}");
            return dev?.GetValue("ScriptFolder") as string is { Length: > 0 } f ? f : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void SetupLoopMidi()
    {
        try
        {
            _loopMidiBtn.Enabled = false;
            var exe = MidiDevices.DownloadLoopMidiInstaller(
                Path.Combine(Path.GetTempPath(), "FLPresence", "loopMIDI"));
            if (exe is null)
            {
                MessageBox.Show("Download failed. Get loopMIDI manually from " +
                                "tobias-erichsen.de (free), install it, then restart FLPresence.",
                    "FLPresence", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!MidiDevices.RunLoopMidiInstaller(exe))
            {
                MessageBox.Show("Installation cancelled.", "FLPresence",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            MessageBox.Show("loopMIDI installed.\n\n" +
                            "1. Open loopMIDI from the Start menu and create a port named \"" +
                            MidiDevices.VirtualPortName + "\" (or use the default \"loopMIDI Port\").\n" +
                            "2. Restart FL Studio once — FLPresence attaches its bridge automatically.",
                "FLPresence", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _log.Error("loopMIDI setup failed", ex);
        }
        finally
        {
            _loopMidiBtn.Enabled = true;
        }
    }

    private static bool IsDiscordRunning() =>
        Process.GetProcessesByName("Discord").Length > 0;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { Hide(); e.Cancel = true; return; }
        base.OnFormClosing(e);
    }

    /// <summary>Tiny wrapper so the groups read cleanly.</summary>
    private sealed class ListViewGroupsGroup
    {
        public ListViewGroup Native { get; }
        public ListViewGroupsGroup(string header)
        {
            Native = new ListViewGroup(header) { HeaderAlignment = HorizontalAlignment.Left };
        }
    }
}
