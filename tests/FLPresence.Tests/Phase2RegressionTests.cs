using FLPresence.Core;
using Xunit;

namespace FLPresence.Tests;

/// <summary>Regression tests for the phase-2 bugfix pass: dedup must never
/// fire because time passed, and must always fire when the visible presence
/// (incl. the session timer origin) changes.</summary>
public class DedupRegressionTests
{
    private static AppSettings Settings() => new()
    {
        UpdateIntervalMs = 0,
        ShowProject = true, ShowBpm = true, ShowPattern = true,
        ShowChannel = true, ShowPlugin = true, ShowSessionTimer = true,
    };

    private static FlState State(double session) => new()
    {
        Session = session, Project = "Melody.flp", Channel = "Lead", Plugin = "Serum",
        PatternNumber = 3, Pattern = "Chorus", Bpm = 140,
    };

    [Fact]
    public void Dedup_HoursOfUnchangedState_PushExactlyOnce()
    {
        var engine = new PresenceEngine(Settings());
        engine.OnState(State(100));
        var t0 = DateTime.UtcNow;
        Assert.NotNull(engine.Tick(t0));
        // Regression: the old hash bucketed the session start per minute/hour,
        // producing a phantom re-push every hour. Two hours must push nothing.
        for (var t = t0.AddMinutes(37); t < t0.AddHours(2); t = t.AddMinutes(37))
            Assert.Null(engine.Tick(t));
    }

    [Fact]
    public void Dedup_SessionRestart_SameVisibleFields_PushesWithNewTimer()
    {
        var engine = new PresenceEngine(Settings());
        var t0 = DateTime.UtcNow;
        engine.OnState(State(100));                       // FL running for 100 s
        var p1 = engine.Tick(t0)!;
        // FL restarted: session resets to ~0, visible text identical.
        engine.OnState(State(3));
        var p2 = engine.Tick(t0.AddSeconds(1));
        Assert.NotNull(p2);                               // timer reset = visible change
        Assert.True(p2!.StartUtc > p1.StartUtc, "restart must move StartUtc forward (elapsed resets)");
    }

    [Fact]
    public void Ping_KeepsBridgeAliveAndAnchorsSession()
    {
        var engine = new PresenceEngine(Settings());
        engine.OnPing(50);
        Assert.True(engine.BridgeAlive);
        var t0 = DateTime.UtcNow;
        // No state yet → no payload, but ExpireBridge must not fire on pings.
        Assert.False(engine.ExpireBridge(15));
        Assert.Equal(engine.SessionStartUtc!.Value.Date, t0.Date);
    }

    [Fact]
    public void Ping_StaleState_DoesNotReviveClearedState()
    {
        var engine = new PresenceEngine(Settings());
        engine.OnState(new FlState { Session = 10, Channel = "Lead" });
        engine.OnBye();
        engine.OnPing(10);                                // ping without state
        Assert.True(engine.BridgeAlive);                  // liveness restored...
        var p = engine.Tick(DateTime.UtcNow);
        // ...but with no state snapshot the presence stays Idle, not Editing.
        Assert.Equal(PresenceMode.Idle, p!.Mode);
    }
}

public class BridgeProtocolPhase2Tests
{
    [Fact]
    public void Parse_Ping_WithSessionAndSource()
    {
        var msg = BridgeProtocol.Parse(
            """{"type":"ping","t":1790000000.5,"session":42,"source":"FLPresence"}""");
        Assert.NotNull(msg);
        Assert.Equal("ping", msg!.Type);
        Assert.Equal(42, msg.Session);
        Assert.Equal("FLPresence", msg.Source);
    }

    [Fact]
    public void Parse_Ping_WithoutOptionalFields()
    {
        var msg = BridgeProtocol.Parse("""{"type":"ping","t":1.0}""");
        Assert.NotNull(msg);
        Assert.Equal("ping", msg!.Type);
        Assert.Null(msg.Session);
    }

    [Fact]
    public void Parse_Error_CarriesWhereAndMessage()
    {
        var msg = BridgeProtocol.Parse(
            """{"type":"error","where":"OnIdle","message":"AttributeError: boom","t":1.0}""");
        Assert.NotNull(msg);
        Assert.Equal("error", msg!.Type);
        Assert.Contains("OnIdle", msg.ErrorText);
        Assert.Contains("boom", msg.ErrorText);
    }

    [Fact]
    public void Parse_State_IncludesSourceField()
    {
        var msg = BridgeProtocol.Parse(
            """{"type":"state","t":1.0,"session":7,"source":"loopMIDI Port","project":"X"}""");
        Assert.NotNull(msg!.State);
        Assert.Equal("loopMIDI Port", msg.State!.Source);
        Assert.Equal(7, msg.State.Session);
    }
}

public class FlStateDifferTests
{
    private static FlState State(
        string project = "Song.flp", bool playing = false, bool recording = false,
        double? bpm = 140, int patternNumber = 1, string pattern = "Pattern 1",
        string channel = "Kick", string plugin = "Fruit Kick",
        string mixer = "Master", bool? songMode = false, bool metronome = false) => new()
    {
        Project = project, Playing = playing, Recording = recording, Bpm = bpm,
        PatternNumber = patternNumber, Pattern = pattern, Channel = channel,
        Plugin = plugin, MixerTrackName = mixer, SongMode = songMode, Metronome = metronome,
    };

    [Fact]
    public void FirstState_ProducesNoDiff()
    {
        Assert.Empty(FlStateDiffer.Diff(null, State()));
    }

    [Fact]
    public void UnchangedState_ProducesNoDiff()
    {
        Assert.Empty(FlStateDiffer.Diff(State(), State()));
    }

    [Fact]
    public void BpmChange_LogsOldAndNew()
    {
        var lines = FlStateDiffer.Diff(State(), State(bpm: 150));
        var line = Assert.Single(lines);
        Assert.Equal("FL state changed: BPM 140 -> 150", line);
    }

    [Fact]
    public void TransportChange_UsesHumanLabels()
    {
        var lines = FlStateDiffer.Diff(State(), State(playing: true));
        Assert.Equal("Transport: Stopped -> Playing", Assert.Single(lines));
        Assert.Contains("-> Recording", FlStateDiffer.Diff(State(playing: true), State(playing: true, recording: true)).Single());
        Assert.Contains("Stopped", FlStateDiffer.Diff(State(playing: true), State()).Single());
    }

    [Fact]
    public void PatternChange_IncludesNumberAndName()
    {
        var lines = FlStateDiffer.Diff(State(), State(patternNumber: 4, pattern: "Verse"));
        Assert.Equal("Pattern changed: 1 Pattern 1 -> 4 Verse", Assert.Single(lines));
    }

    [Fact]
    public void ChannelAndPluginChange_EachLogged()
    {
        var lines = FlStateDiffer.Diff(State(), State(channel: "Bass", plugin: "Serum"));
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Contains("Channel 'Kick' -> 'Bass'"));
        Assert.Contains(lines, l => l.Contains("Plugin 'Fruit Kick' -> 'Serum'"));
    }

    [Fact]
    public void ProjectChangeLogged_EmptyShownAsQuestionMark()
    {
        var lines = FlStateDiffer.Diff(State(), State(project: ""));
        Assert.Equal("FL state changed: Project 'Song.flp' -> '?'", Assert.Single(lines));
    }

    [Fact]
    public void SongModeAndMetronomeLogged()
    {
        Assert.Contains("Mode Pattern -> Song", FlStateDiffer.Diff(State(), State(songMode: true)).Single());
        Assert.Contains("Metronome off -> on", FlStateDiffer.Diff(State(), State(metronome: true)).Single());
    }
}
