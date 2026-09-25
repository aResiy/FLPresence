namespace FLPresence.Core;

/// <summary>Result of privacy filtering — empty string means "do not show".</summary>
public sealed class PrivacyResult
{
    public string Project { get; init; } = "";
    public string Channel { get; init; } = "";
    public string Plugin { get; init; } = "";
    public string Pattern { get; init; } = "";
    public string MixerTrackName { get; init; } = "";
    public bool SecretActive { get; init; }
    public bool BlacklistTriggered { get; init; }
}

/// <summary>Applies Secret Mode / Hide-project-only / blacklist to raw names.</summary>
public static class PrivacyFilter
{
    public static PrivacyResult Apply(FlState state, AppSettings settings)
    {
        var project = settings.ShowProject ? Clean(state.Project) : "";
        if (settings.HideFlpExtension && project.EndsWith(".flp", StringComparison.OrdinalIgnoreCase))
            project = project[..^4];

        var blacklistHit = settings.ProjectBlacklist.Any(b => !string.IsNullOrWhiteSpace(b)
            && state.Project.Contains(b.Trim(), StringComparison.OrdinalIgnoreCase));

        var secret = settings.SecretMode || blacklistHit;

        if (secret && settings.HideProjectOnly)
            project = ""; // only the project name is hidden
        else if (secret)
        {
            project = "";
            return new PrivacyResult
            {
                Project = project,
                Channel = settings.ShowChannel ? "Channel" : "",
                Plugin = settings.ShowPlugin ? "Plugin" : "",
                Pattern = settings.ShowPattern ? "Pattern" : "",
                MixerTrackName = "",
                SecretActive = true,
                BlacklistTriggered = blacklistHit,
            };
        }

        return new PrivacyResult
        {
            Project = project,
            Channel = settings.ShowChannel ? Clean(state.Channel) : "",
            Plugin = settings.ShowPlugin ? Clean(state.Plugin) : "",
            Pattern = settings.ShowPattern ? Clean(state.Pattern) : "",
            MixerTrackName = settings.ShowChannel ? Clean(state.MixerTrackName) : "",
            SecretActive = false,
            BlacklistTriggered = blacklistHit,
        };
    }

    private static string Clean(string? s) => (s ?? "").Trim();
}
