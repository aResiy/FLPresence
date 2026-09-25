using System.Security.Cryptography;
using System.Text;

namespace FLPresence.Core;

public enum PresenceMode { Idle, Editing, Playing, LiveMidi, Recording }

/// <summary>Fully rendered Discord presence payload.</summary>
public sealed class PresencePayload
{
    public string Details { get; init; } = "";
    public string State { get; init; } = "";
    public string SmallImageKey { get; init; } = "stop";
    public string SmallImageText { get; init; } = "";
    public DateTime? StartUtc { get; init; }
    public PresenceMode Mode { get; init; }
}

/// <summary>
/// Decides what is most interesting to show right now and when to push it.
/// Priority: Recording &gt; Live MIDI &gt; Playing &gt; Editing &gt; Idle.
/// Debounces, throttles and deduplicates so Discord is never spammed.
/// </summary>
public sealed class PresenceEngine
{
    private readonly AppSettings _settings;
    private readonly object _lock = new();

    private FlState? _state;
    private List<int> _held = new();
    private int _lastVelocity;
    private bool _bridgeAlive;
    private FlState? _native;
    private bool _nativeForeground;
    private bool _nativeAlive;
    private bool _nativeVisible;   // a native payload is currently shown

    private DateTime _lastPacketUtc = DateTime.MinValue;
    private DateTime _notesChangedUtc = DateTime.MinValue;
    private DateTime _lastPushUtc = DateTime.MinValue;
    private DateTime? _sessionStartUtc;
    private string _lastHash = "";

    public bool BridgeAlive { get { lock (_lock) return _bridgeAlive; } }
    public DateTime LastPacketUtc { get { lock (_lock) return _lastPacketUtc; } }
    public string? LastFlVersion { get; private set; }
    public string LastError { get; set; } = "";
    /// <summary>Most recent raw FL state (for diagnostics display).</summary>
    public FlState? LastState { get { lock (_lock) return _state; } }
    /// <summary>Anchored session start (Discord elapsed timer), for diagnostics.</summary>
    public DateTime? SessionStartUtc { get { lock (_lock) return _sessionStartUtc; } }

    public PresenceEngine(AppSettings settings) => _settings = settings;

    public void OnState(FlState state)
    {
        lock (_lock)
        {
            _state = state;
            LastFlVersion = string.IsNullOrWhiteSpace(state.FlVersion) ? LastFlVersion : state.FlVersion;
            if (state.Notes.Count > 0) { _held = state.Notes; _notesChangedUtc = DateTime.UtcNow; }
            _lastPacketUtc = DateTime.UtcNow;
            _bridgeAlive = true;
            // Re-anchor the Discord "elapsed" timer when the session identity
            // changed (FL restarted: start jumps either way) but absorb small
            // drift between multiple bridge instances (< 5 s).
            var start = DateTime.UtcNow - TimeSpan.FromSeconds(state.Session);
            if (_sessionStartUtc is null || Math.Abs((start - _sessionStartUtc.Value).TotalSeconds) > 5)
                _sessionStartUtc = start;
        }
    }

    /// <summary>Keepalive ping from the bridge thread: refreshes liveness and
    /// the session anchor without touching FL state.</summary>
    public void OnPing(double sessionSeconds)
    {
        lock (_lock)
        {
            _lastPacketUtc = DateTime.UtcNow;
            _bridgeAlive = true;
            var start = DateTime.UtcNow - TimeSpan.FromSeconds(sessionSeconds);
            if (_sessionStartUtc is null || start < _sessionStartUtc - TimeSpan.FromSeconds(5))
                _sessionStartUtc = start;
        }
    }

    public void OnNotes(NotesUpdate notes)
    {
        lock (_lock)
        {
            _held = notes.Notes ?? new List<int>();
            if (notes.Velocities is { Count: > 0 } && _held.Count > 0 &&
                notes.Velocities.TryGetValue(_held[^1].ToString(), out var v))
                _lastVelocity = v;
            _notesChangedUtc = DateTime.UtcNow;
            _lastPacketUtc = DateTime.UtcNow;
            _bridgeAlive = true;
        }
    }

    public void OnBye()
    {
        lock (_lock) { _bridgeAlive = false; _held = new List<int>(); _state = null; _sessionStartUtc = null; }
    }

    /// <summary>
    /// Native (no-bridge) source: FL process + window title. Used only while the
    /// bridge is silent, so a connected bridge always wins with richer state.
    /// Returns true on the transition into "FL lost focus": the caller should
    /// clear the Discord presence — nothing is claimed while the user is in
    /// another app.
    /// </summary>
    public bool OnNative(FlState state, bool foreground)
    {
        lock (_lock)
        {
            _native = state;
            _nativeForeground = foreground;
            _nativeAlive = true;
            if (_bridgeAlive) return false;
            // Same session-anchor rule as OnState: FL restart resets the Discord timer.
            var start = DateTime.UtcNow - TimeSpan.FromSeconds(state.Session);
            if (_sessionStartUtc is null || Math.Abs((start - _sessionStartUtc.Value).TotalSeconds) > 5)
                _sessionStartUtc = start;
            return false;
        }
    }

    /// <summary>FL process is gone — drop native presence. Returns true on transition.</summary>
    public bool ClearNative()
    {
        lock (_lock)
        {
            if (!_nativeAlive) return false;
            _nativeAlive = false;
            _native = null;
            _nativeVisible = false;
            if (!_bridgeAlive) { _sessionStartUtc = null; _lastHash = ""; }
            return true;
        }
    }

    /// <summary>Bridge went silent — clear live status. Returns true on transition.</summary>
    public bool ExpireBridge(int timeoutSeconds)
    {
        lock (_lock)
        {
            if (!_bridgeAlive) return false;
            if (DateTime.UtcNow - _lastPacketUtc < TimeSpan.FromSeconds(timeoutSeconds)) return false;
            _bridgeAlive = false;
            _state = null;
            _held = new List<int>();
            _sessionStartUtc = null;   // next session re-anchors fresh
            _lastHash = ""; // next presence will be a fresh push
            return true;
        }
    }

    /// <summary>Called periodically (companion timer). Returns a payload only when
    /// the visible presence actually changed and the throttle window passed.</summary>
    public PresencePayload? Tick(DateTime nowUtc, bool sessionTimerEnabled = true)
    {
        FlState? state;
        List<int> held;
        bool alive;
        bool native = false;
        DateTime? sessionStart;
        lock (_lock)
        {
            state = _state; held = _held; alive = _bridgeAlive; sessionStart = _sessionStartUtc;
            if (!alive)
            {
                // Native mode: presence shown the whole time FL Studio is open.
                if (!_nativeAlive || _native is null) return null;
                state = _native;
                native = true;
            }
            if ((nowUtc - _lastPushUtc).TotalMilliseconds < _settings.UpdateIntervalMs) return null;
        }

        var privacy = PrivacyFilter.Apply(state ?? new FlState(), _settings);
        var mode = DecideMode(state, held, nowUtc, native);
        var (details, smallKey) = RenderDetails(state, privacy, mode, held);
        var rendered = new PresencePayload
        {
            Details = details,
            State = RenderState(state, privacy, mode, held),
            SmallImageKey = smallKey,
            SmallImageText = _settings.ProducerName,
            StartUtc = sessionTimerEnabled ? sessionStart : null,
            Mode = mode,
        };

        // Dedup on exactly the visible fields. The session start is part of
        // the identity (a restarted FL resets the Discord timer = visible
        // change), but time passing must NEVER invalidate dedup by itself.
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(
            $"{rendered.Details}|{rendered.State}|{rendered.SmallImageKey}|{sessionStart:O}")));

        lock (_lock)
        {
            if (hash == _lastHash) return null;   // dedup: nothing visible changed
            _lastHash = hash;
            _lastPushUtc = nowUtc;
            _nativeVisible = native;   // a native payload is now live in Discord
        }
        return rendered;
    }

    /// <summary>Force the next Tick to emit (e.g. after settings change).</summary>
    public void Invalidate()
    {
        lock (_lock) _lastHash = "";
    }

    private PresenceMode DecideMode(FlState? s, List<int> held, DateTime nowUtc, bool native = false)
    {
        if (s is null) return PresenceMode.Idle;
        if (s.Recording) return PresenceMode.Recording;
        if (_settings.ShowMidiNotes)
        {
            var hold = TimeSpan.FromSeconds(Math.Max(1, _settings.MidiHoldSeconds));
            if (held.Count > 0 || nowUtc - _notesChangedUtc < hold)
                return PresenceMode.LiveMidi;
        }
        if (s.Playing) return PresenceMode.Playing;
        // Native source: a project is open = working on it.
        if (native && !string.IsNullOrEmpty(s.Project)) return PresenceMode.Editing;
        if (!string.IsNullOrEmpty(s.Channel) || !string.IsNullOrEmpty(s.Plugin))
            return PresenceMode.Editing;
        return PresenceMode.Idle;
    }

    private (string Details, string SmallKey) RenderDetails(FlState? s, PrivacyResult p, PresenceMode mode, List<int> held)
    {
        var v = Values(s, p, mode, held);
        var t = _settings;
        string tpl;
        switch (mode)
        {
            case PresenceMode.Recording: tpl = Or(t.TemplateDetailsRec, "⏺ Recording"); break;
            case PresenceMode.LiveMidi: tpl = Or(t.TemplateDetailsMidi, "🎹 {Chord}"); break;
            case PresenceMode.Playing: tpl = Or(t.TemplateDetailsPlay, "▶ {Pattern}"); break;
            case PresenceMode.Editing: tpl = Or(t.TemplateDetailsEdit, "🎛 Working on {Project}"); break;
            default: tpl = Or(t.TemplateDetailsIdle, "🎛 Producing music"); break;
        }
        var rendered = TemplateRenderer.Render(tpl, v);
        if (mode == PresenceMode.Editing && string.IsNullOrWhiteSpace(v.Project))
            rendered = TemplateRenderer.Render(Or(t.TemplateDetailsIdle, "🎛 Producing music"), v);
        if (mode == PresenceMode.LiveMidi && string.IsNullOrWhiteSpace(v.Chord))
            rendered = "🎹 Live MIDI"; // hold window after release, no chord to show
        return (rendered, SmallKey(mode));
    }

    private string RenderState(FlState? s, PrivacyResult p, PresenceMode mode, List<int> held)
    {
        var t = _settings;
        var v = Values(s, p, mode, held);
        var tpl = mode switch
        {
            PresenceMode.Recording => Or(t.TemplateStateRec, "{Channel} • {BPM} BPM"),
            PresenceMode.LiveMidi => Or(t.TemplateStateMidi, "{Plugin} • {BPM} BPM • Live MIDI"),
            PresenceMode.Playing => Or(t.TemplateStatePlay, "{BPM} BPM • {Mode}"),
            PresenceMode.Editing => Or(t.TemplateStateEdit,
                string.IsNullOrEmpty(v.Channel) && string.IsNullOrEmpty(v.Plugin)
                    ? "{BPM} BPM • FL Studio {FLVersion}" : "✏ {Channel} • {Plugin}"),
            _ => "",
        };
        var rendered = TemplateRenderer.Render(tpl, v);
        if (string.IsNullOrEmpty(v.Bpm)) rendered = rendered.Replace(" BPM", "");
        while (rendered.Contains("•  •")) rendered = rendered.Replace("•  •", "•");
        var state = rendered.Trim(" •".ToCharArray());
        if (state == "FL Studio") state = "";
        // Discord requires state >= 2 chars and rejects the WHOLE activity otherwise
        // (e.g. a template degenerating to a single glyph "✏"); empty is omitted.
        return state.Length < 2 ? "" : state;
    }

    private TemplateValues Values(FlState? s, PrivacyResult p, PresenceMode mode, List<int> held)
    {
        var chord = mode == PresenceMode.LiveMidi
            ? ChordDetector.DetectOrNotes(held, _settings.ShowChords, _settings.ShowMidiNotes) ?? ""
            : "";
        var note = held.Count > 0 ? NoteNames.NameWithOctave(held[^1]) : "";
        var bpm = _settings.ShowBpm ? TemplateRenderer.FormatBpm(s?.Bpm) : "";
        return new TemplateValues
        {
            Project = p.Project,
            Bpm = bpm,
            Status = ModeLabel(mode),
            Mode = s?.SongMode switch { true => "Song Mode", false => "Pattern Mode", null => "" },
            Pattern = p.Pattern,
            PatternNumber = s?.PatternNumber?.ToString() ?? "",
            Channel = p.Channel,
            Plugin = p.Plugin,
            MixerTrack = string.IsNullOrEmpty(p.MixerTrackName) ? s?.MixerTrack?.ToString() ?? "" : p.MixerTrackName,
            Note = note,
            Chord = chord,
            Velocity = _lastVelocity > 0 ? _lastVelocity.ToString() : "",
            SessionTime = TemplateRenderer.FormatSessionTime((s?.Session ?? 0)),
            FlVersion = s?.FlVersion ?? "",
            Producer = _settings.ProducerName,
        };
    }

    public static string ModeLabel(PresenceMode mode) => mode switch
    {
        PresenceMode.Recording => "Recording",
        PresenceMode.LiveMidi => "Live MIDI",
        PresenceMode.Playing => "Playing",
        PresenceMode.Editing => "Editing",
        _ => "Idle",
    };

    private static string SmallKey(PresenceMode mode) => mode switch
    {
        PresenceMode.Recording => "record",
        PresenceMode.LiveMidi => "midi",
        PresenceMode.Playing => "play",
        _ => "stop",
    };

    private static string Or(string? template, string fallback) =>
        string.IsNullOrWhiteSpace(template) ? fallback : template;
}
