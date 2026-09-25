namespace FLPresence.Core;

/// <summary>
/// Minimal chord detector over the currently held notes.
/// Returns e.g. "C", "C#m7", "Cmaj7" — root by pitch class (no octave),
/// or null when the interval set matches no known template.
/// Longer templates are listed first so C-E-G-B matches Cmaj7, not Cmaj.
/// </summary>
public static class ChordDetector
{
    private static readonly (string Suffix, int[] Intervals)[] Templates =
    {
        ("maj7",  new[] { 0, 4, 7, 11 }),
        ("7",     new[] { 0, 4, 7, 10 }),
        ("m7",    new[] { 0, 3, 7, 10 }),
        ("m7b5",  new[] { 0, 3, 6, 10 }),
        ("dim7",  new[] { 0, 3, 6, 9 }),
        ("6",     new[] { 0, 4, 7, 9 }),
        ("m6",    new[] { 0, 3, 7, 9 }),
        ("maj9",  new[] { 0, 4, 7, 11, 2 }),
        ("add9",  new[] { 0, 4, 7, 2 }),
        ("madd9", new[] { 0, 3, 7, 2 }),
        ("",      new[] { 0, 4, 7 }),
        ("m",     new[] { 0, 3, 7 }),
        ("dim",   new[] { 0, 3, 6 }),
        ("aug",   new[] { 0, 4, 8 }),
        ("sus4",  new[] { 0, 5, 7 }),
        ("sus2",  new[] { 0, 2, 7 }),
        ("5",     new[] { 0, 7 }),
    };

    /// <summary>Detect a chord from held MIDI notes. Single note => "C#5".</summary>
    public static string? Detect(IEnumerable<int> held)
    {
        var notes = held.Where(n => n is >= 0 and <= 127).Distinct().OrderBy(n => n).ToList();
        if (notes.Count == 0) return null;
        if (notes.Count == 1) return NoteNames.NameWithOctave(notes[0]);

        var pcs = notes.Select(n => n % 12).Distinct().ToList();
        if (pcs.Count == 1) return NoteNames.Name(notes[0]); // same pc across octaves

        // Root candidates: bass note first (musical inversion beats reharming),
        // then any other pitch class.
        int bass = notes[0] % 12;
        for (int pass = 0; pass < 2; pass++)
        {
            IEnumerable<int> roots = pass == 0
                ? new[] { bass }
                : pcs.Where(p => p != bass).OrderBy(p => (p - bass + 12) % 12);
            foreach (var root in roots)
            {
                var rel = pcs.Select(p => (p - root + 12) % 12).OrderBy(x => x).ToArray();
                foreach (var t in Templates)
                    if (t.Intervals.OrderBy(x => x).SequenceEqual(rel))
                        return NoteNames.Name(root) + t.Suffix;
            }
        }
        return null;
    }

    /// <summary>Fallback display when no chord matched: up to maxNotes note names.</summary>
    public static string NotesLabel(IEnumerable<int> held, int maxNotes = 3)
    {
        var notes = held.Where(n => n is >= 0 and <= 127).Distinct().OrderBy(n => n).Take(maxNotes)
            .Select(NoteNames.NameWithOctave);
        return string.Join(", ", notes);
    }

    /// <summary>Chord if detected, else up to 3 note names — the "{Chord}" variable.</summary>
    public static string? DetectOrNotes(IEnumerable<int> held, bool chordsEnabled = true, bool notesEnabled = true)
    {
        var heldList = held.Where(n => n is >= 0 and <= 127).Distinct().ToList();
        if (heldList.Count == 0) return null;
        if (chordsEnabled)
        {
            var chord = Detect(heldList);
            if (chord != null) return chord;
        }
        return notesEnabled ? NotesLabel(heldList) : null;
    }
}
