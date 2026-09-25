using System.Text.Json;

namespace FLPresence.Core;

/// <summary>Snapshot sent by the FLPresence Bridge (UDP JSON, camelCase).</summary>
public sealed class FlState
{
    public double T { get; set; }
    public double Session { get; set; }          // seconds since bridge init
    public string? Source { get; set; }          // device.getName() of the sending instance
    public string FlVersion { get; set; } = "";
    public string Project { get; set; } = "";
    public bool Playing { get; set; }
    public bool Recording { get; set; }
    public bool? SongMode { get; set; }          // true=song, false=pattern
    public int? PatternNumber { get; set; }
    public string Pattern { get; set; } = "";
    public int? ChannelIndex { get; set; }
    public string Channel { get; set; } = "";
    public string Plugin { get; set; } = "";
    public int? MixerTrack { get; set; }
    public string MixerTrackName { get; set; } = "";
    public double? Bpm { get; set; }
    public double? PosMs { get; set; }
    public double? LenMs { get; set; }
    public bool Metronome { get; set; }
    public int? TsNum { get; set; }
    public List<int> Notes { get; set; } = new();
    public Dictionary<string, int> Velocities { get; set; } = new();
    public int NotesCount { get; set; }
}

/// <summary>Held-note set pushed by the bridge on note on/off.</summary>
public sealed class NotesUpdate
{
    public double T { get; set; }
    public List<int> Notes { get; set; } = new();
    public Dictionary<string, int> Velocities { get; set; } = new();
    public int NotesCount { get; set; }
}

/// <summary>Parsed bridge datagram. Unknown types are ignored by consumers.</summary>
public sealed class BridgeMessage
{
    public string Type { get; set; } = "";
    public FlState? State { get; set; }
    public NotesUpdate? Notes { get; set; }
    public double? Session { get; set; }     // ping: keepalive session seconds
    public string? Source { get; set; }      // ping/hello: device.getName()
    public string? ErrorText { get; set; }   // error: bridge-side failure report
}

public static class BridgeProtocol
{
    public const int DefaultPort = 39901;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static BridgeMessage? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "state":
                    return new BridgeMessage { Type = "state", State = JsonSerializer.Deserialize<FlState>(json, Json) };
                case "notes":
                    return new BridgeMessage { Type = "notes", Notes = JsonSerializer.Deserialize<NotesUpdate>(json, Json) };
                case "ping":
                    return new BridgeMessage
                    {
                        Type = "ping",
                        Session = doc.RootElement.TryGetProperty("session", out var sess)
                            && sess.TryGetDouble(out var s) ? s : null,
                        Source = doc.RootElement.TryGetProperty("source", out var src)
                            ? src.GetString() : null,
                    };
                case "error":
                    return new BridgeMessage
                    {
                        Type = "error",
                        ErrorText = (doc.RootElement.TryGetProperty("where", out var w) ? w.GetString() : null,
                                     doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null)
                                    switch { var (w2, m2) => $"{w2}: {m2}".Trim(' ', ':') },
                    };
                case "hello":
                case "bye":
                    return new BridgeMessage { Type = type! };
                default:
                    return null;
            }
        }
        catch (Exception)
        {
            return null; // malformed datagram: drop silently
        }
    }
}
