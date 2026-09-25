using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace FLPresence.Core;

/// <summary>
/// Parses the FL Studio main window title into (project, version).
/// Known shapes: "my song - FL Studio 24", "*my song - FL Studio 24" (unsaved
/// changes), "song.flp - FL Studio 21.2", "FL Studio 24" (no project).
/// Anything unrecognized maps to no project — never guess garbage.
/// </summary>
public static partial class FlTitleParser
{
    [GeneratedRegex(@"^(?<name>.+?)\s*[–—-]\s*FL Studio\s*[\d.]*\s*$")]
    private static partial Regex WithProject();

    [GeneratedRegex(@"FL Studio\s*(?<v>[\d.]+)")]
    private static partial Regex VersionPart();

    public static (string Project, string FlVersion) Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return ("", "");
        var t = title.Trim();
        var m = WithProject().Match(t);
        var version = VersionPart().Match(t) is { Success: true } vm ? vm.Groups["v"].Value : "";
        if (!m.Success) return ("", version);          // "FL Studio 24" or unknown
        var name = m.Groups["name"].Value.Trim().TrimStart('*').Trim();
        if (name.EndsWith(".flp", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return (name, version);
    }
}

/// <summary>
/// Native (no-MIDI, no-bridge) state source: watches the FL Studio process and
/// its main window title. Gives project name, FL version and focus state —
/// everything FL exposes without a controller script. Transport/pattern/notes
/// remain bridge-only enrichments.
/// </summary>
public sealed class FlWindowMonitor
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    private delegate bool EnumWindowsProc(IntPtr h, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int max);

    private static readonly string[] FlProcessNames = { "FL64", "FL32", "FL" };

    private DateTime _lastPollUtc = DateTime.MinValue;
    private FlState? _last;
    private string _flpProject = "";
    private string? _flpPath;
    private DateTime _flpStamp;
    private double? _flpBpm;

    /// <summary>True when FL's main window had keyboard focus at the last poll.</summary>
    public bool Foreground { get; private set; }

    /// <summary>
    /// Best FL title among all top-level windows of the FL process(es).
    /// Process.MainWindowTitle is unreliable for FL: it flips to plugin/dialog
    /// windows, which made the presence flicker Editing ↔ "Producing music".
    /// </summary>
    private static string BestTitle(HashSet<uint> pids)
    {
        string best = "";
        var sb = new System.Text.StringBuilder(512);
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            if (!pids.Contains(pid)) return true;
            sb.Clear();
            GetWindowText(h, sb, sb.Capacity);
            var t = sb.ToString();
            if (!t.Contains("FL Studio")) return true;
            best = t;
            return FlTitleParser.Parse(t).Project.Length == 0; // stop on a project title
        }, IntPtr.Zero);
        return best;
    }

    /// <summary>Throttled poll (2 s). Returns a partial FlState while FL runs
    /// (cached between polls), null when it does not.</summary>
    public FlState? Poll(bool flRunning)
    {
        if (!flRunning) { _last = null; return null; }
        var now = DateTime.UtcNow;
        if (now - _lastPollUtc < TimeSpan.FromSeconds(2)) return _last;
        _lastPollUtc = now;

        var procs = FlProcessNames.SelectMany(Process.GetProcessesByName).ToList();
        if (procs.Count == 0) { _last = null; return null; }
        var pids = procs.Select(p => (uint)p.Id).ToHashSet();
        var startTime = procs.Select(p => { try { return p.StartTime; } catch { return DateTime.Now; } }).Min();

        var (project, version) = FlTitleParser.Parse(BestTitle(pids));
        GetWindowThreadProcessId(GetForegroundWindow(), out var fgPid);
        Foreground = pids.Contains(fgPid);

        var bpm = ProjectBpm(project, procs[0].Id);

        _last = new FlState
        {
            Bpm = bpm,
            T = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Session = (DateTime.Now - startTime).TotalSeconds,
            Source = "window",
            FlVersion = version,
            Project = project,
        };
        return _last;
    }

    /// <summary>Tempo from the saved .flp; re-read whenever the file is saved.</summary>
    private double? ProjectBpm(string project, int pid)
    {
        if (project != _flpProject)
        {
            _flpProject = project;
            _flpPath = FlpReader.FindProject(project, CommandLine(pid));
            _flpStamp = default;
            _flpBpm = null;
        }
        if (_flpPath is null) return null;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(_flpPath);
            if (stamp != _flpStamp) { _flpStamp = stamp; _flpBpm = FlpReader.ReadBpm(_flpPath); }
        }
        catch { }
        return _flpBpm;
    }

    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr h, int cls, byte[] buf, int len, out int ret);

    /// <summary>Process command line (ProcessCommandLineInformation, Win 8.1+).
    /// No WMI: WMI on the UI thread hung the whole pump.</summary>
    private static string? CommandLine(int pid)
    {
        var h = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var buf = new byte[65536];
            if (NtQueryInformationProcess(h, 60, buf, buf.Length, out _) != 0) return null;
            int len = BitConverter.ToUInt16(buf, 0);   // UNICODE_STRING.Length (bytes)
            int off = IntPtr.Size == 8 ? 16 : 8;       // string data follows the struct
            return System.Text.Encoding.Unicode.GetString(buf, off, len);
        }
        finally { CloseHandle(h); }
    }
}
