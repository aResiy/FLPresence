namespace FLPresence.Core;

/// <summary>
/// Human-readable diff of consecutive FL state snapshots, for the change log
/// ("FL state changed: BPM 140 -> 150"). Pure function, unit-tested.
/// </summary>
public static class FlStateDiffer
{
    /// <summary>Diff lines describing what changed from <paramref name="prev"/>
    /// to <paramref name="now"/>. Empty list when prev is null (first state)
    /// or nothing visible changed.</summary>
    public static List<string> Diff(FlState? prev, FlState now)
    {
        var lines = new List<string>();
        if (prev is null || now is null) return lines;

        if (prev.Project != now.Project)
            lines.Add($"FL state changed: Project '{Or(prev.Project)}' -> '{Or(now.Project)}'");
        if (prev.Playing != now.Playing || prev.Recording != now.Recording)
            lines.Add($"Transport: {Label(prev)} -> {Label(now)}");
        if (prev.Bpm != now.Bpm)
            lines.Add($"FL state changed: BPM {Fmt(prev.Bpm)} -> {Fmt(now.Bpm)}");
        if (prev.PatternNumber != now.PatternNumber || prev.Pattern != now.Pattern)
            lines.Add($"Pattern changed: {Pat(prev)} -> {Pat(now)}");
        if (prev.Channel != now.Channel)
            lines.Add($"FL state changed: Channel '{Or(prev.Channel)}' -> '{Or(now.Channel)}'");
        if (prev.Plugin != now.Plugin)
            lines.Add($"FL state changed: Plugin '{Or(prev.Plugin)}' -> '{Or(now.Plugin)}'");
        if (prev.MixerTrackName != now.MixerTrackName)
            lines.Add($"FL state changed: Mixer track '{Or(prev.MixerTrackName)}' -> '{Or(now.MixerTrackName)}'");
        if (prev.SongMode != now.SongMode && now.SongMode is not null)
            lines.Add($"FL state changed: Mode {(prev.SongMode switch { true => "Song", false => "Pattern", null => "?" })}" +
                      $" -> {(now.SongMode switch { true => "Song", false => "Pattern", null => "?" })}");
        if (prev.Metronome != now.Metronome)
            lines.Add($"FL state changed: Metronome {(prev.Metronome ? "on" : "off")} -> {(now.Metronome ? "on" : "off")}");

        return lines;
    }

    private static string Label(FlState s) =>
        s.Recording ? "Recording" : s.Playing ? "Playing" : "Stopped";

    private static string Pat(FlState s)
    {
        var name = string.IsNullOrEmpty(s.Pattern) ? $"Pattern {s.PatternNumber}" : s.Pattern;
        return string.IsNullOrWhiteSpace(name) ? "?" : $"{s.PatternNumber} {name}".Trim();
    }

    private static string Fmt(double? v) => v?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?";

    private static string Or(string s) => string.IsNullOrWhiteSpace(s) ? "?" : s;
}
