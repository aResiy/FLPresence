using FLPresence.Core;
using Xunit;

namespace FLPresence.Tests;

public class PresenceEngineTests
{
    private static AppSettings Settings() => new()
    {
        UpdateIntervalMs = 0,
        ShowProject = true, ShowBpm = true, ShowPattern = true, ShowChannel = true,
        ShowPlugin = true, ShowMidiNotes = true, ShowChords = true, ShowSessionTimer = true,
    };

    private static FlState State(bool playing = false, bool recording = false,
        string channel = "Lead", string plugin = "Serum", string pattern = "Chorus",
        double? bpm = 140, bool songMode = false) => new()
    {
        Session = 100,
        Project = "Melody.flp",
        Playing = playing,
        Recording = recording,
        SongMode = songMode,
        PatternNumber = 3,
        Pattern = pattern,
        Channel = channel,
        Plugin = plugin,
        Bpm = bpm,
        FlVersion = "24.2.2 Producer",
    };

    [Fact] // Recording > Live MIDI > Playing > Editing > Idle
    public void Priority_RecordingBeatsEverything()
    {
        var engine = new PresenceEngine(Settings());
        engine.OnState(State(playing: true, recording: true));
        engine.OnNotes(new NotesUpdate { Notes = new List<int> { 60, 64, 67 } });
        var p = engine.Tick(DateTime.UtcNow)!;
        Assert.Equal(PresenceMode.Recording, p.Mode);
        Assert.Contains("Recording", p.Details);
        Assert.Equal("record", p.SmallImageKey);
    }

    [Fact]
    public void Priority_LiveMidiBeatsPlaying()
    {
        var engine = new PresenceEngine(Settings());
        engine.OnState(State(playing: true));
        engine.OnNotes(new NotesUpdate { Notes = new List<int> { 60, 64, 67 } });
        var p = engine.Tick(DateTime.UtcNow)!;
        Assert.Equal(PresenceMode.LiveMidi, p.Mode);
        Assert.Equal("C", p.Details.Replace("🎹", "").Trim());
        Assert.Equal("midi", p.SmallImageKey);
    }

    [Fact]
    public void Priority_PlayingBeatsEditing()
    {
        var engine = new PresenceEngine(Settings());
        engine.OnState(State(playing: true));
        var p = engine.Tick(DateTime.UtcNow)!;
        Assert.Equal(PresenceMode.Playing, p.Mode);
        Assert.Contains("Chorus", p.Details);
        Assert.Contains("140 BPM", p.State);
        Assert.Equal("play", p.SmallImageKey);
    }

    [Fact]
    public void Priority_EditingWhenStopped()
    {
        var engine = new PresenceEngine(Settings());
        engine.OnState(State());
        var p = engine.Tick(DateTime.UtcNow)!;
        Assert.Equal(PresenceMode.Editing, p.Mode);
        Assert.Contains("Melody", p.Details);      // .flp hidden by default
        Assert.DoesNotContain(".flp", p.Details);
        Assert.Equal("stop", p.SmallImageKey);
    }

    [Fact]
    public void Priority_IdleWhenNothingSelected()
    {
        var engine = new PresenceEngine(Settings());
        engine.OnState(new FlState { Session = 10 });
        var p = engine.Tick(DateTime.UtcNow)!;
        Assert.Equal(PresenceMode.Idle, p.Mode);
        Assert.Contains("Producing music", p.Details);
    }

    [Fact]
    public void MidiHold_RevertsToPreviousStatusAfterHold()
    {
        var settings = Settings();
        settings.MidiHoldSeconds = 4;
        var engine = new PresenceEngine(settings);
        engine.OnState(State());
        var t0 = DateTime.UtcNow;
        engine.OnNotes(new NotesUpdate { Notes = new List<int> { 60, 64, 67 } });

        var active = engine.Tick(t0)!;
        Assert.Equal(PresenceMode.LiveMidi, active.Mode);

        engine.OnNotes(new NotesUpdate { Notes = new List<int>() }); // release keys
        var afterHold = engine.Tick(t0.AddSeconds(5));
        Assert.Equal(PresenceMode.Editing, afterHold!.Mode);
    }

    [Fact]
    public void Dedup_SameState_TicksOnce()
    {
        var engine = new PresenceEngine(Settings());
        engine.OnState(State());
        Assert.NotNull(engine.Tick(DateTime.UtcNow));
        Assert.Null(engine.Tick(DateTime.UtcNow.AddSeconds(60))); // nothing changed
    }

    [Fact]
    public void Dedup_ChangedState_EmitsAgain()
    {
        var engine = new PresenceEngine(Settings());
        engine.OnState(State());
        Assert.NotNull(engine.Tick(DateTime.UtcNow));
        engine.OnState(State(playing: true));
        var p = engine.Tick(DateTime.UtcNow);
        Assert.NotNull(p);
        Assert.Equal(PresenceMode.Playing, p!.Mode);
    }

    [Fact]
    public void Throttle_SuppressesRapidUpdates()
    {
        var settings = Settings();
        settings.UpdateIntervalMs = 1000;
        var engine = new PresenceEngine(settings);
        engine.OnState(State());
        var t0 = DateTime.UtcNow;
        Assert.NotNull(engine.Tick(t0));
        engine.OnState(State(playing: true));
        Assert.Null(engine.Tick(t0.AddMilliseconds(100)));  // inside throttle window
        Assert.NotNull(engine.Tick(t0.AddMilliseconds(1100)));
    }

    [Fact]
    public void ExpireBridge_TimesOutAndResets()
    {
        var engine = new PresenceEngine(Settings());
        engine.OnState(State());
        Assert.False(engine.ExpireBridge(15));  // fresh packet inside 15 s window
        Assert.True(engine.ExpireBridge(0));    // zero window expires immediately
        Assert.False(engine.BridgeAlive);
        Assert.Null(engine.Tick(DateTime.UtcNow));
    }

    [Fact]
    public void Bye_ClearsBridge()
    {
        var engine = new PresenceEngine(Settings());
        engine.OnState(State());
        engine.OnBye();
        Assert.False(engine.BridgeAlive);
        Assert.Null(engine.Tick(DateTime.UtcNow));
    }

    [Fact]
    public void SessionTimer_StartIsStable()
    {
        var engine = new PresenceEngine(Settings());
        var t0 = DateTime.UtcNow;
        engine.OnState(new FlState { Session = 10 });
        var p1 = engine.Tick(t0)!;
        engine.OnState(new FlState { Session = 12, Playing = true }); // also un-dedups the tick
        var p2 = engine.Tick(t0.AddSeconds(1))!;
        Assert.NotNull(p1.StartUtc);
        Assert.NotNull(p2.StartUtc);
        Assert.Equal(p1.StartUtc!.Value, p2.StartUtc!.Value, TimeSpan.FromSeconds(5));
    }
}

public class PrivacyTests
{
    private static AppSettings Settings(bool secret = false, bool hideProjectOnly = false,
        List<string>? blacklist = null) => new()
    {
        ShowProject = true, ShowChannel = true, ShowPlugin = true, ShowPattern = true,
        SecretMode = secret, HideProjectOnly = hideProjectOnly, ProjectBlacklist = blacklist ?? new(),
    };

    private static readonly FlState State = new()
    {
        Project = "My Secret Track.flp", Channel = "Serum Bass", Plugin = "Serum",
        Pattern = "Drop", MixerTrackName = "Bass",
    };

    [Fact]
    public void Off_ShowsEverything()
    {
        var r = PrivacyFilter.Apply(State, Settings());
        Assert.Equal("My Secret Track", r.Project); // .flp hidden by default
        Assert.Equal("Serum Bass", r.Channel);
        Assert.Equal("Serum", r.Plugin);
        Assert.Equal("Drop", r.Pattern);
        Assert.False(r.SecretActive);
    }

    [Fact]
    public void SecretMode_HidesAllNames()
    {
        var r = PrivacyFilter.Apply(State, Settings(secret: true));
        Assert.Equal("", r.Project);
        Assert.Equal("Channel", r.Channel);
        Assert.Equal("Plugin", r.Plugin);
        Assert.Equal("Pattern", r.Pattern);
        Assert.True(r.SecretActive);
    }

    [Fact]
    public void HideProjectOnly_KeepsOtherNames()
    {
        var r = PrivacyFilter.Apply(State, Settings(secret: true, hideProjectOnly: true));
        Assert.Equal("", r.Project);
        Assert.Equal("Serum Bass", r.Channel);
        Assert.Equal("Serum", r.Plugin);
    }

    [Fact]
    public void Blacklist_TriggersSecretMode()
    {
        var r = PrivacyFilter.Apply(State, Settings(blacklist: new List<string> { "secret" }));
        Assert.True(r.BlacklistTriggered);
        Assert.True(r.SecretActive);
        Assert.Equal("", r.Project);
    }

    [Fact]
    public void Blacklist_CaseInsensitive()
    {
        var r = PrivacyFilter.Apply(State, Settings(blacklist: new List<string> { "SECRET TRACK" }));
        Assert.True(r.BlacklistTriggered);
    }

    [Fact]
    public void ShowToggles_RemoveFields()
    {
        var s = Settings();
        s.ShowChannel = false;
        s.ShowPlugin = false;
        var r = PrivacyFilter.Apply(State, s);
        Assert.Equal("", r.Channel);
        Assert.Equal("", r.Plugin);
        Assert.Equal("My Secret Track", r.Project);
    }

    [Fact]
    public void ShowProjectOff_HidesProject()
    {
        var s = Settings();
        s.ShowProject = false;
        var r = PrivacyFilter.Apply(State, s);
        Assert.Equal("", r.Project);
    }
}
