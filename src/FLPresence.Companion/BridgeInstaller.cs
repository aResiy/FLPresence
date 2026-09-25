using FLPresence.Core;
using Microsoft.Win32;

namespace FLPresence.Companion;

/// <summary>
/// Detects FL Studio (install + user data), deploys the MIDI bridge script
/// and manages the "start with Windows" run key. Only ever writes inside
/// the FLPresence device folder — existing FL Studio scripts are untouched.
/// </summary>
public static class BridgeInstaller
{
    public const string DeviceFolderName = "FLPresence";
    public const string ScriptFileName = "device_FLPresence.py";
    private const string RunKeyName = "FLPresence";

    /// <summary>Full path of the deployed script, or null if user data not found.</summary>
    public static string? BridgePath()
    {
        var hw = HardwareDir();
        return hw is null ? null : Path.Combine(hw, DeviceFolderName, ScriptFileName);
    }

    public static string? HardwareDir()
    {
        // FL Studio's user data folder is configurable; the active location is
        // stored at HKCU\Software\Image-Line\Shared\Paths ("Shared data") and
        // is NOT always Documents (e.g. "C:\фл студио\"). Check it first.
        var candidates = new List<string>();
        try
        {
            using var paths = Registry.CurrentUser.OpenSubKey(@"Software\Image-Line\Shared\Paths");
            var shared = paths?.GetValue("Shared data") as string;
            if (!string.IsNullOrWhiteSpace(shared))
                candidates.Add(Path.Combine(shared, "FL Studio"));
        }
        catch { /* registry unreadable - fall through to Documents */ }
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        candidates.AddRange(new[]
        {
            Path.Combine(docs, "Image-Line", "FL Studio"),
            Path.Combine(docs, "Image-Line", "FL Studio 21"),
            Path.Combine(docs, "Image-Line", "FL Studio 20"),
        });
        foreach (var root in candidates)
            if (Directory.Exists(Path.Combine(root, "Settings")))
                return Path.Combine(root, "Settings", "Hardware");
        return null;
    }

    /// <summary>FL Studio install info from the uninstall registry (version string).</summary>
    public static (string Name, string Version)? DetectFlInstall()
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var view in new[] { @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
                                         @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                using var key = root.OpenSubKey(view);
                if (key is null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var item = key.OpenSubKey(sub);
                    var name = item?.GetValue("DisplayName") as string;
                    if (name is null || !name.StartsWith("FL Studio", StringComparison.OrdinalIgnoreCase))
                        continue;
                    return (name, (item!.GetValue("DisplayVersion") as string) ?? "");
                }
            }
        }
        return null;
    }

    public static bool IsFlRunning()
    {
        foreach (var name in new[] { "FL64", "FL32", "FL" })
            if (System.Diagnostics.Process.GetProcessesByName(name).Length > 0)
                return true;
        return false;
    }

    /// <summary>Write the embedded bridge script into the FL user data folder.</summary>
    public static string DeployBridge(FileLogger log)
    {
        var hw = HardwareDir() ?? throw new DirectoryNotFoundException(
            "FL Studio user data not found (Documents\\Image-Line\\FL Studio\\Settings).");
        var dir = Path.Combine(hw, DeviceFolderName);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, ScriptFileName);

        var asm = typeof(BridgeInstaller).Assembly;
        using var stream = asm.GetManifestResourceStream("FLPresence.bridge_script.py")
            ?? throw new InvalidOperationException("Embedded bridge script missing from assembly.");
        using var file = File.Create(target);
        stream.CopyTo(file);

        // The .ini marker must sit NEXT TO the script folder (like FL's own
        // "Arturia MiniLab 3.ini"), not inside it — FL's scanner looks at
        // Hardware\<FolderName>.ini when listing/loading controller scripts.
        File.WriteAllText(Path.Combine(hw, DeviceFolderName + ".ini"), "[Ini]\r\nVersion=2\r\n");

        log.Info($"Bridge script deployed: {target}");
        return target;
    }

    public static bool BridgeInstalled() => BridgePath() is { } p && File.Exists(p);

    private const string MidiInputKey = @"Software\Image-Line\FL Studio 24\Devices\MIDI input";

    /// <summary>
    /// Assign the FLPresence script to every enabled MIDI input device that has
    /// no controller script yet (FL's own ScriptFolder registry value).
    /// Never overwrites an existing script assignment. Skipped while FL runs
    /// (it rewrites its registry on exit).
    /// </summary>
    public static (int Assigned, int AlreadyUsed) TryAutoAssignScript()
    {
        if (IsFlRunning()) return (0, 0);
        using var root = Registry.CurrentUser.OpenSubKey(MidiInputKey, writable: true);
        if (root is null) return (0, 0);
        int assigned = 0, used = 0;
        foreach (var name in root.GetSubKeyNames())
        {
            using var dev = root.OpenSubKey(name, writable: true);
            if (dev is null) continue;
            var virtualPort = IsVirtualPort(name);
            // Physical devices are only touched when enabled; the virtual port
            // is our permanent home and is force-enabled if FL left it off.
            if (!virtualPort && (dev.GetValue("Enabled") as string) == "0") continue;
            var current = dev.GetValue("ScriptFolder") as string;
            if (!string.IsNullOrWhiteSpace(current)) { used++; continue; }
            dev.SetValue("ScriptFolder", DeviceFolderName);
            if (virtualPort && (dev.GetValue("Enabled") as string) == "0")
                dev.SetValue("Enabled", "1");
            assigned++;
        }
        return (assigned, used);
    }

    /// <summary>The FLPresence virtual port and any loopMIDI port are treated
    /// as FLPresence's permanent home; physical controllers are optional.</summary>
    public static bool IsVirtualPort(string deviceName) =>
        deviceName == MidiDevices.VirtualPortName ||
        deviceName.Contains(MidiDevices.LoopMidiMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>Registry sub-key of the virtual port under FL's MIDI input
    /// devices, or null when FL has not seen the port yet.</summary>
    public static string? VirtualPortRegistryName()
    {
        using var root = Registry.CurrentUser.OpenSubKey(MidiInputKey);
        foreach (var name in root?.GetSubKeyNames() ?? Array.Empty<string>())
            if (IsVirtualPort(name))
                return name;
        return null;
    }

    // --- autostart ---------------------------------------------------------

    public static bool GetStartWithWindows()
    {
        using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return run?.GetValue(RunKeyName) is string;
    }

    public static void SetStartWithWindows(bool enabled)
    {
        using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)!;
        if (enabled)
            run.SetValue(RunKeyName, $"\"{Environment.ProcessPath}\" --watch");
        else
            run.DeleteValue(RunKeyName, false);
    }

    /// <summary>Migrate a pre-1.1 Run key (plain exe path) to the --watch
    /// launcher form, so autostart waits for FL Studio instead of always
    /// keeping the tray app alive. Safe to call on every start.</summary>
    public static void EnsureWatchAutostart()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            var value = run?.GetValue(RunKeyName) as string;
            if (value is { Length: > 0 } && !value.Contains("--watch"))
                run!.SetValue(RunKeyName, $"\"{Environment.ProcessPath}\" --watch");
        }
        catch { /* autostart migration is best-effort */ }
    }
}
