using FLPresence.Core;
using Xunit;

namespace FLPresence.Tests;

public class NoteNamesTests
{
    [Theory]
    [InlineData(60, "C4")]
    [InlineData(61, "C#4")]
    [InlineData(62, "D4")]
    [InlineData(63, "D#4")]
    [InlineData(64, "E4")]
    [InlineData(21, "A0")]
    [InlineData(69, "A4")]
    [InlineData(73, "C#5")]
    [InlineData(108, "C8")]
    [InlineData(127, "G9")]
    public void NameWithOctave_MapsScientificPitch(int midi, string expected)
        => Assert.Equal(expected, NoteNames.NameWithOctave(midi));

    [Theory]
    [InlineData(60, "C")]
    [InlineData(61, "C#")]
    [InlineData(70, "A#")]
    public void Name_MapsPitchClass(int midi, string expected)
        => Assert.Equal(expected, NoteNames.Name(midi));

    [Fact]
    public void Name_HandlesOutOfRange()
    {
        Assert.Equal("", NoteNames.Name(-1));
        Assert.Equal("", NoteNames.Name(128));
    }
}

public class ChordDetectorTests
{
    [Theory]
    [InlineData(new[] { 60, 64, 67 }, "C")]        // C E G -> C Major
    [InlineData(new[] { 60, 63, 67 }, "Cm")]       // C Eb G -> C Minor
    [InlineData(new[] { 60, 64, 67, 71 }, "Cmaj7")]// C E G B -> Cmaj7
    [InlineData(new[] { 57, 60, 64 }, "Am")]       // A C E -> A Minor
    [InlineData(new[] { 62, 65, 69, 72 }, "Dm7")]  // D F A C -> Dm7
    [InlineData(new[] { 61, 64, 68, 71 }, "C#m7")] // C# E G# B -> C#m7
    [InlineData(new[] { 60, 64, 67, 70 }, "C7")]   // C E G Bb -> C7
    [InlineData(new[] { 62, 67 }, "G5")]           // D+G: fourth -> inverted power chord
    [InlineData(new[] { 60, 65, 67 }, "Csus4")]
    [InlineData(new[] { 60, 62, 67 }, "Csus2")]
    public void Detect_RecognizesKnownChords(int[] notes, string expected)
        => Assert.Equal(expected, ChordDetector.Detect(notes));

    [Fact]
    public void Detect_SingleNote_ReturnsWithOctave()
        => Assert.Equal("C#5", ChordDetector.Detect(new[] { 73 }));

    [Fact]
    public void Detect_OctaveDoubling_UsesPitchClass()
        => Assert.Equal("C", ChordDetector.Detect(new[] { 48, 60, 72 }));

    [Fact]
    public void Detect_UnknownCluster_ReturnsNull()
        => Assert.Null(ChordDetector.Detect(new[] { 60, 61, 62 }));

    [Fact]
    public void Detect_Empty_ReturnsNull()
        => Assert.Null(ChordDetector.Detect(Array.Empty<int>()));

    [Fact]
    public void NotesLabel_LimitsToThree()
    {
        var label = ChordDetector.NotesLabel(new[] { 60, 64, 67, 71, 74 });
        Assert.Equal("C4, E4, G4", label);
    }

    [Fact]
    public void DetectOrNotes_FallsBackToNoteList()
    {
        var label = ChordDetector.DetectOrNotes(new[] { 60, 61, 63 });
        Assert.Equal("C4, C#4, D#4", label);
    }
}
