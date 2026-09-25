namespace FLPresence.Core;

/// <summary>MIDI note number to scientific pitch name: 60 -> C4, 61 -> C#4, 73 -> C#5.</summary>
public static class NoteNames
{
    private static readonly string[] Names =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public static string Name(int midi)
    {
        if (midi < 0 || midi > 127) return "";
        return Names[midi % 12];
    }

    public static string NameWithOctave(int midi)
    {
        if (midi < 0 || midi > 127) return "";
        return Names[midi % 12] + (midi / 12 - 1);
    }
}
