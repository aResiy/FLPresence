using System.Globalization;
using System.Text.RegularExpressions;

namespace FLPresence.Core;

/// <summary>Values available to presence templates.</summary>
public sealed class TemplateValues
{
    public string Project { get; init; } = "";
    public string Bpm { get; init; } = "";
    public string Status { get; init; } = "";
    public string Mode { get; init; } = "";
    public string Pattern { get; init; } = "";
    public string PatternNumber { get; init; } = "";
    public string Channel { get; init; } = "";
    public string Plugin { get; init; } = "";
    public string MixerTrack { get; init; } = "";
    public string Note { get; init; } = "";
    public string Chord { get; init; } = "";
    public string Velocity { get; init; } = "";
    public string SessionTime { get; init; } = "";
    public string FlVersion { get; init; } = "";
    public string Producer { get; init; } = "";
}

public static partial class TemplateRenderer
{
    [GeneratedRegex(@"\{(Project|BPM|Status|Mode|Pattern|PatternNumber|Channel|Plugin|MixerTrack|Note|Chord|Velocity|SessionTime|FLVersion|Producer)\}")]
    private static partial Regex VarRegex();

    public static string Render(string template, TemplateValues v)
    {
        if (string.IsNullOrEmpty(template)) return "";
        return VarRegex().Replace(template, m =>
        {
            var value = m.Groups[1].Value switch
            {
                "Project" => v.Project,
                "BPM" => v.Bpm,
                "Status" => v.Status,
                "Mode" => v.Mode,
                "Pattern" => v.Pattern,
                "PatternNumber" => v.PatternNumber,
                "Channel" => v.Channel,
                "Plugin" => v.Plugin,
                "MixerTrack" => v.MixerTrack,
                "Note" => v.Note,
                "Chord" => v.Chord,
                "Velocity" => v.Velocity,
                "SessionTime" => v.SessionTime,
                "FLVersion" => v.FlVersion,
                "Producer" => v.Producer,
                _ => "",
            };
            return value ?? "";
        });
    }

    /// <summary>"140", "140.5" -> "140 BPM"; missing -> "".</summary>
    public static string FormatBpm(double? bpm)
    {
        if (bpm is null or < 10 or > 999) return "";
        var rounded = Math.Round(bpm.Value);
        return Math.Abs(bpm.Value - rounded) < 0.05
            ? ((int)rounded).ToString(CultureInfo.InvariantCulture)
            : bpm.Value.ToString("0.#", CultureInfo.InvariantCulture);
    }

    public static string FormatSessionTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalMinutes:00}:{t.Seconds:00}");
    }
}
