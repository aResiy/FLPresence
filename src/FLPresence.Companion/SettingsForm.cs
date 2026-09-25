using FLPresence.Core;

namespace FLPresence.Companion;

/// <summary>Dark settings window. Edits a copy; Saved fires with the new settings.</summary>
public sealed class SettingsForm : Form
{
    public event Action<AppSettings>? Saved;

    private readonly AppSettings _s;
    private readonly FileLogger _log;

    private readonly TextBox _appId = new();
    private readonly TextBox _producer = new();
    private readonly NumericUpDown _interval = new();
    private readonly NumericUpDown _midiHold = new();
    private readonly CheckBox _showProject = new(), _showBpm = new(), _showPattern = new(),
        _showChannel = new(), _showPlugin = new(), _showMidi = new(), _showChords = new(),
        _showTimer = new(), _hideFlp = new(), _secret = new(), _hideProjectOnly = new();
    private readonly TextBox _blacklist = new();
    private readonly TextBox _tIdle = new(), _tEditD = new(), _tEditS = new(), _tPlayD = new(),
        _tPlayS = new(), _tMidiD = new(), _tMidiS = new(), _tRecD = new(), _tRecS = new();

    private static readonly Color Bg = Color.FromArgb(24, 24, 28);
    private static readonly Color Panel = Color.FromArgb(32, 32, 38);
    private static readonly Color Fg = Color.FromArgb(232, 232, 236);
    private static readonly Color Accent = Color.FromArgb(255, 122, 47);

    public SettingsForm(AppSettings settings, FileLogger log)
    {
        _s = Clone(settings);
        _log = log;
        Text = "FLPresence — Settings";
        BackColor = Bg; ForeColor = Fg;
        Font = new Font("Segoe UI", 9.5f);
        Size = new Size(560, 560);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 520);
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) Hide(); };

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            BackColor = Bg, ForeColor = Fg,
        };
        tabs.TabPages.Add(GeneralTab());
        tabs.TabPages.Add(DisplayTab());
        tabs.TabPages.Add(PrivacyTab());
        tabs.TabPages.Add(TemplatesTab());

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft,
            BackColor = Panel, Padding = new Padding(8),
        };
        var save = DarkButton("Save");
        save.Click += (_, _) => Save();
        var cancel = DarkButton("Cancel");
        cancel.Click += (_, _) => Close();
        bottom.Controls.Add(save);
        bottom.Controls.Add(cancel);

        Controls.Add(tabs);
        Controls.Add(bottom);
        LoadValues();
    }

    private static AppSettings Clone(AppSettings s)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(s);
        return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, AppSettings.Json)!;
    }

    private Button DarkButton(string text) => new()
    { Text = text, BackColor = Panel, ForeColor = Fg, FlatStyle = FlatStyle.Flat, Width = 96 };
    private static Label Cap(string text) => new()
    { Text = text, AutoSize = true, ForeColor = Color.FromArgb(150, 150, 158) };
    private static CheckBox Opt(string text, bool check) => new()
    { Text = text, AutoSize = true, ForeColor = Color.FromArgb(232, 232, 236), CheckAlign = ContentAlignment.MiddleLeft };

    private TabPage Page(string title)
    {
        var page = new TabPage(title) { BackColor = Bg, ForeColor = Fg };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, Padding = new Padding(14), BackColor = Bg,
        };
        page.Controls.Add(flow);
        return page;
    }

    private Control Lbl(string text, Control forControl)
    {
        var l = Cap(text);
        return l;
    }

    private TabPage GeneralTab()
    {
        var p = Page("General");
        var flow = (FlowLayoutPanel)p.Controls[0];
        flow.Controls.Add(Cap("Discord Application ID (from Discord Developer Portal)"));
        _appId.Width = 480; _appId.BackColor = Panel; _appId.ForeColor = Fg; _appId.BorderStyle = BorderStyle.FixedSingle;
        flow.Controls.Add(_appId);
        flow.Controls.Add(Cap("Producer / display name (shown as image tooltip)"));
        _producer.Width = 300; _producer.BackColor = Panel; _producer.ForeColor = Fg; _producer.BorderStyle = BorderStyle.FixedSingle;
        flow.Controls.Add(_producer);
        flow.Controls.Add(Cap("Update interval, ms (Discord rate limiting)"));
        _interval.Minimum = 250; _interval.Maximum = 10000; _interval.Width = 100;
        flow.Controls.Add(_interval);
        flow.Controls.Add(Cap("Live MIDI hold time after last note, s"));
        _midiHold.Minimum = 1; _midiHold.Maximum = 60; _midiHold.Width = 100;
        flow.Controls.Add(_midiHold);
        return p;
    }

    private TabPage DisplayTab()
    {
        var p = Page("Display");
        var flow = (FlowLayoutPanel)p.Controls[0];
        _showProject.Text = "Show project name"; _showBpm.Text = "Show BPM";
        _showPattern.Text = "Show pattern name"; _showChannel.Text = "Show channel name";
        _showPlugin.Text = "Show plugin name"; _showMidi.Text = "Show live MIDI notes";
        _showChords.Text = "Show chords"; _showTimer.Text = "Show session timer";
        _hideFlp.Text = "Hide .flp extension";
        foreach (var cb in new[] { _showProject, _showBpm, _showPattern, _showChannel, _showPlugin, _showMidi, _showChords, _showTimer, _hideFlp })
            flow.Controls.Add(cb);
        return p;
    }

    private TabPage PrivacyTab()
    {
        var p = Page("Privacy");
        var flow = (FlowLayoutPanel)p.Controls[0];
        _secret.Text = "Secret Mode (hide project, channels, patterns, plugins)";
        _hideProjectOnly.Text = "Hide project name only";
        flow.Controls.Add(_secret);
        flow.Controls.Add(_hideProjectOnly);
        flow.Controls.Add(Cap("Project name blacklist (one phrase per line — triggers Secret Mode):"));
        _blacklist.Multiline = true; _blacklist.Size = new Size(480, 110);
        _blacklist.BackColor = Panel; _blacklist.ForeColor = Fg; _blacklist.BorderStyle = BorderStyle.FixedSingle;
        flow.Controls.Add(_blacklist);
        return p;
    }

    private TabPage TemplatesTab()
    {
        var p = Page("Templates");
        var flow = (FlowLayoutPanel)p.Controls[0];
        void Tpl(string caption, TextBox box)
        {
            flow.Controls.Add(Cap(caption));
            box.Width = 490; box.BackColor = Panel; box.ForeColor = Fg; box.BorderStyle = BorderStyle.FixedSingle;
            flow.Controls.Add(box);
        }
        Tpl("Idle details", _tIdle);
        Tpl("Editing details", _tEditD);
        Tpl("Editing state", _tEditS);
        Tpl("Playing details", _tPlayD);
        Tpl("Playing state", _tPlayS);
        Tpl("Live MIDI details", _tMidiD);
        Tpl("Live MIDI state", _tMidiS);
        Tpl("Recording details", _tRecD);
        Tpl("Recording state", _tRecS);
        flow.Controls.Add(Cap("Variables: {Project} {BPM} {Status} {Mode} {Pattern} {PatternNumber} " +
                              "{Channel} {Plugin} {MixerTrack} {Note} {Chord} {Velocity} {SessionTime} {FLVersion}"));
        return p;
    }

    private void LoadValues()
    {
        _appId.Text = _s.DiscordApplicationId;
        _producer.Text = _s.ProducerName;
        _interval.Value = Math.Clamp(_s.UpdateIntervalMs, 250, 10000);
        _midiHold.Value = Math.Clamp(_s.MidiHoldSeconds, 1, 60);
        _showProject.Checked = _s.ShowProject; _showBpm.Checked = _s.ShowBpm;
        _showPattern.Checked = _s.ShowPattern; _showChannel.Checked = _s.ShowChannel;
        _showPlugin.Checked = _s.ShowPlugin; _showMidi.Checked = _s.ShowMidiNotes;
        _showChords.Checked = _s.ShowChords; _showTimer.Checked = _s.ShowSessionTimer;
        _hideFlp.Checked = _s.HideFlpExtension;
        _secret.Checked = _s.SecretMode; _hideProjectOnly.Checked = _s.HideProjectOnly;
        _blacklist.Lines = _s.ProjectBlacklist.ToArray();
        _tIdle.Text = _s.TemplateDetailsIdle; _tEditD.Text = _s.TemplateDetailsEdit;
        _tEditS.Text = _s.TemplateStateEdit; _tPlayD.Text = _s.TemplateDetailsPlay;
        _tPlayS.Text = _s.TemplateStatePlay; _tMidiD.Text = _s.TemplateDetailsMidi;
        _tMidiS.Text = _s.TemplateStateMidi; _tRecD.Text = _s.TemplateDetailsRec;
        _tRecS.Text = _s.TemplateStateRec;
    }

    private void Save()
    {
        _s.DiscordApplicationId = _appId.Text.Trim();
        _s.ProducerName = _producer.Text.Trim();
        _s.UpdateIntervalMs = (int)_interval.Value;
        _s.MidiHoldSeconds = (int)_midiHold.Value;
        _s.ShowProject = _showProject.Checked; _s.ShowBpm = _showBpm.Checked;
        _s.ShowPattern = _showPattern.Checked; _s.ShowChannel = _showChannel.Checked;
        _s.ShowPlugin = _showPlugin.Checked; _s.ShowMidiNotes = _showMidi.Checked;
        _s.ShowChords = _showChords.Checked; _s.ShowSessionTimer = _showTimer.Checked;
        _s.HideFlpExtension = _hideFlp.Checked;
        _s.SecretMode = _secret.Checked; _s.HideProjectOnly = _hideProjectOnly.Checked;
        _s.ProjectBlacklist = _blacklist.Lines
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        _s.TemplateDetailsIdle = _tIdle.Text.Trim(); _s.TemplateDetailsEdit = _tEditD.Text.Trim();
        _s.TemplateStateEdit = _tEditS.Text.Trim(); _s.TemplateDetailsPlay = _tPlayD.Text.Trim();
        _s.TemplateStatePlay = _tPlayS.Text.Trim(); _s.TemplateDetailsMidi = _tMidiD.Text.Trim();
        _s.TemplateStateMidi = _tMidiS.Text.Trim(); _s.TemplateDetailsRec = _tRecD.Text.Trim();
        _s.TemplateStateRec = _tRecS.Text.Trim();
        try { _s.Save(); }
        catch (Exception ex) { _log.Error("Settings save failed", ex); }
        Saved?.Invoke(_s);
        Close();
    }
}
