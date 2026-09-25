using System.Text.Json;
using System.Text.Json.Serialization;

namespace FLPresence.Core;

/// <summary>User settings, persisted as JSON in %APPDATA%\FLPresence\settings.json.</summary>
public sealed class AppSettings
{
    // Discord
    /// <summary>FLPresence's public Discord Application ID. Rich Presence uses
    /// the local IPC pipe and needs no secret, so the default works out of the
    /// box; override here only to show presence under your own Discord app.</summary>
    public string DiscordApplicationId { get; set; } = "1175189951097339976";
    public bool EnablePresence { get; set; } = true;

    // Identity / privacy
    public string ProducerName { get; set; } = "";
    public bool SecretMode { get; set; }
    public bool HideProjectOnly { get; set; }
    public List<string> ProjectBlacklist { get; set; } = new();

    // What to show
    public bool ShowProject { get; set; } = true;
    public bool ShowBpm { get; set; } = true;
    public bool ShowPattern { get; set; } = true;
    public bool ShowChannel { get; set; } = true;
    public bool ShowPlugin { get; set; } = true;
    public bool ShowMidiNotes { get; set; } = true;
    public bool ShowChords { get; set; } = true;
    public bool ShowSessionTimer { get; set; } = true;
    public bool HideFlpExtension { get; set; } = true;

    // Behaviour
    public int UpdateIntervalMs { get; set; } = 1000;   // min gap between Discord updates
    public int MidiHoldSeconds { get; set; } = 4;       // keep Live MIDI status after last note
    public int BridgeTimeoutSeconds { get; set; } = 15; // no packets => FL considered closed

    // Templates (empty => built-in default)
    public string TemplateDetailsIdle { get; set; } = "";
    public string TemplateDetailsEdit { get; set; } = "";
    public string TemplateStateEdit { get; set; } = "";
    public string TemplateDetailsPlay { get; set; } = "";
    public string TemplateStatePlay { get; set; } = "";
    public string TemplateDetailsMidi { get; set; } = "";
    public string TemplateStateMidi { get; set; } = "";
    public string TemplateDetailsRec { get; set; } = "";
    public string TemplateStateRec { get; set; } = "";

    [JsonIgnore]
    public bool DiscordConfigured => !string.IsNullOrWhiteSpace(DiscordApplicationId);

    public static string DefaultPath
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FLPresence");
            return Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings();
        }
        catch (Exception)
        {
            // corrupted settings: fall back to defaults
        }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }));
    }

    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
}
