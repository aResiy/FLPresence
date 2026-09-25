using FLPresence.Core;
using Xunit;

namespace FLPresence.Tests;

public class FlTitleParserTests
{
    [Theory]
    [InlineData("battle in you soul - FL Studio 24", "battle in you soul", "24")]
    [InlineData("*battle in you soul - FL Studio 24", "battle in you soul", "24")] // unsaved marker
    [InlineData("song.flp - FL Studio 21.2", "song", "21.2")]
    [InlineData("my – mix – FL Studio 24", "my – mix", "24")] // en dash inside name survives
    [InlineData("FL Studio 24", "", "24")]
    [InlineData("FL Studio", "", "")]
    [InlineData("", "", "")]
    [InlineData(null, "", "")]
    public void Parse_KnownShapes(string? title, string project, string version)
    {
        var (p, v) = FlTitleParser.Parse(title);
        Assert.Equal(project, p);
        Assert.Equal(version, v);
    }
}

public class NativeSourceEngineTests
{
    private static FlState NativeState(string project = "my song", double session = 60) => new()
    {
        Session = session,
        Source = "window",
        Project = project,
        FlVersion = "24",
    };

    [Fact]
    public void Native_Editing_DegenerateState_Omitted_NotSingleGlyph()
    {
        // Regression: state template "✏ {Channel} • {Plugin}" with both empty
        // degraded to "✏" — Discord rejected the WHOLE activity (state >= 2 chars).
        var e = new PresenceEngine(new AppSettings());
        e.OnNative(NativeState(), foreground: true);
        var p = e.Tick(DateTime.UtcNow);
        Assert.NotNull(p);
        Assert.True(p.State.Length is 0 or >= 2, $"state was '{p.State}'");
    }

    [Fact]
    public void Native_Foreground_ShowsEditing()
    {
        var e = new PresenceEngine(new AppSettings());
        e.OnNative(NativeState(), foreground: true);
        var p = e.Tick(DateTime.UtcNow);
        Assert.NotNull(p);
        Assert.Equal(PresenceMode.Editing, p.Mode);
        Assert.Contains("my song", p.Details);
    }

    [Fact]
    public void Native_WithoutProject_IsIdle()
    {
        var e = new PresenceEngine(new AppSettings());
        e.OnNative(NativeState(project: ""), foreground: true);
        var p = e.Tick(DateTime.UtcNow);
        Assert.NotNull(p);
        Assert.Equal(PresenceMode.Idle, p.Mode);
    }

    [Fact]
    public void Bridge_State_Wins_Over_Native()
    {
        var e = new PresenceEngine(new AppSettings());
        e.OnNative(NativeState(), foreground: true);
        e.OnState(new FlState { Session = 60, Project = "my song", Playing = true, Pattern = "Verse" });
        var p = e.Tick(DateTime.UtcNow);
        Assert.NotNull(p);
        Assert.Equal(PresenceMode.Playing, p.Mode);
    }

    [Fact]
    public void ClearNative_StopsPresence_WhenNoBridge()
    {
        var e = new PresenceEngine(new AppSettings());
        e.OnNative(NativeState(), foreground: true);
        Assert.True(e.Tick(DateTime.UtcNow) != null);
        Assert.True(e.ClearNative());
        Assert.Null(e.Tick(DateTime.UtcNow.AddSeconds(5)));
        Assert.False(e.ClearNative()); // no second transition
    }


    [Fact]
    public void Native_Background_StillShown_WithBpm()
    {
        var e = new PresenceEngine(new AppSettings());
        var st = NativeState(); st.Bpm = 140;
        Assert.False(e.OnNative(st, foreground: false));
        var p = e.Tick(DateTime.UtcNow);
        Assert.NotNull(p);
        Assert.Equal("🎛 Working on my song", p.Details);
        Assert.Equal("140 BPM • FL Studio 24", p.State);
    }

    [Fact]
    public void Native_NoBpm_ShowsVersionOnly()
    {
        var e = new PresenceEngine(new AppSettings());
        e.OnNative(NativeState(), foreground: true);
        Assert.Equal("FL Studio 24", e.Tick(DateTime.UtcNow)!.State);
    }
}

public class FlpReaderTests
{
    [Fact]
    public void ReadBpm_ParsesTempoEvent()
    {
        var path = Path.GetTempFileName();
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write("FLhd"u8.ToArray()); w.Write(6); w.Write(new byte[6]);
            w.Write("FLdt"u8.ToArray()); w.Write(0);
            w.Write((byte)10); w.Write((byte)1);                   // byte event
            w.Write((byte)199); w.Write((byte)3); w.Write(new byte[3]); // text event
            w.Write((byte)156); w.Write(128500u);                  // tempo 128.5
        }
        Assert.Equal(128.5, FlpReader.ReadBpm(path));
        File.Delete(path);
    }
}
