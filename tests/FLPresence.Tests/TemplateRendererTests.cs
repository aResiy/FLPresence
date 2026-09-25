using FLPresence.Core;
using Xunit;

namespace FLPresence.Tests;

public class TemplateRendererTests
{
    private static TemplateValues Sample() => new()
    {
        Project = "Melody", Bpm = "140", Status = "Playing", Mode = "Song Mode",
        Pattern = "Chorus", PatternNumber = "3", Channel = "Lead", Plugin = "Serum",
        MixerTrack = "Lead", Note = "C#5", Chord = "C#m7", Velocity = "100",
        SessionTime = "12:34", FlVersion = "24.2.2 Producer", Producer = "Me",
    };

    [Fact]
    public void Render_ReplacesAllDocumentedVariables()
    {
        var template = "{Project} {BPM} {Status} {Mode} {Pattern} {PatternNumber} {Channel} " +
                       "{Plugin} {MixerTrack} {Note} {Chord} {Velocity} {SessionTime} {FLVersion}";
        var rendered = TemplateRenderer.Render(template, Sample());
        Assert.Equal("Melody 140 Playing Song Mode Chorus 3 Lead Serum Lead C#5 C#m7 100 12:34 24.2.2 Producer",
            rendered);
    }

    [Fact]
    public void Render_UnknownVariableStays()
        => Assert.Equal("{Nope} x", TemplateRenderer.Render("{Nope} x", Sample()));

    [Fact]
    public void Render_EmptyTemplateIsEmpty()
        => Assert.Equal("", TemplateRenderer.Render("", Sample()));

    [Fact]
    public void FormatBpm_RoundsWhole()
    {
        Assert.Equal("140", TemplateRenderer.FormatBpm(140.0));
        Assert.Equal("140", TemplateRenderer.FormatBpm(139.98));
        Assert.Equal("139.5", TemplateRenderer.FormatBpm(139.5));
    }

    [Fact]
    public void FormatBpm_RejectsJunk()
    {
        Assert.Equal("", TemplateRenderer.FormatBpm(null));
        Assert.Equal("", TemplateRenderer.FormatBpm(0));
        Assert.Equal("", TemplateRenderer.FormatBpm(5000));
    }

    [Fact]
    public void FormatSessionTime_Formats()
    {
        Assert.Equal("00:05", TemplateRenderer.FormatSessionTime(5));
        Assert.Equal("12:34", TemplateRenderer.FormatSessionTime(754));
        Assert.Equal("1:00:00", TemplateRenderer.FormatSessionTime(3600));
        Assert.Equal("00:00", TemplateRenderer.FormatSessionTime(-3));
    }
}

public class BridgeProtocolTests
{
    [Fact]
    public void Parse_StateMessage_MapsAllFields()
    {
        var json = """{"type":"state","t":1.5,"session":42,"flVersion":"24.2.2 Producer","project":"Test.flp","playing":true,"recording":false,"songMode":true,"patternNumber":3,"pattern":"Chorus","channelIndex":1,"channel":"Lead","plugin":"Serum","mixerTrack":2,"mixerTrackName":"Lead","bpm":140,"posMs":1200.5,"lenMs":9999,"metronome":true,"tsNum":4,"notes":[60,64],"velocities":{"60":100},"notesCount":2}""";
        var msg = BridgeProtocol.Parse(json);
        Assert.NotNull(msg);
        Assert.Equal("state", msg!.Type);
        var s = msg.State!;
        Assert.Equal(42, s.Session);
        Assert.Equal("Test.flp", s.Project);
        Assert.True(s.Playing);
        Assert.True(s.SongMode);
        Assert.Equal(3, s.PatternNumber);
        Assert.Equal("Serum", s.Plugin);
        Assert.Equal(140, s.Bpm);
        Assert.Equal(new[] { 60, 64 }, s.Notes);
        Assert.Equal(100, s.Velocities["60"]);
    }

    [Fact]
    public void Parse_NotesMessage()
    {
        var msg = BridgeProtocol.Parse("""{"type":"notes","notes":[61,64,68],"velocities":{"61":88},"notesCount":3}""");
        Assert.Equal("notes", msg!.Type);
        Assert.Equal(3, msg.Notes!.NotesCount);
        Assert.Contains(64, msg.Notes.Notes);
    }

    [Theory]
    [InlineData("""{"type":"hello"}""", "hello")]
    [InlineData("""{"type":"bye"}""", "bye")]
    public void Parse_LifecycleMessages(string json, string expected)
        => Assert.Equal(expected, BridgeProtocol.Parse(json)!.Type);

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"type":"unknown"}""")]
    [InlineData("")]
    public void Parse_BadInput_ReturnsNull(string json)
        => Assert.Null(BridgeProtocol.Parse(json));
}
