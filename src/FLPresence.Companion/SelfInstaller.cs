using System.Diagnostics;
using Microsoft.Win32;

namespace FLPresence.Companion;

/// <summary>
/// One-exe install for GitHub Releases users: running the downloaded
/// FLPresence.exe copies it to %LOCALAPPDATA%\Programs\FLPresence, registers
/// autostart + an "Installed apps" entry, and starts the watchdog. No admin,
/// no SDK, no PowerShell.
/// </summary>
internal static class SelfInstaller
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FLPresence";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string InstallDir
    {
        get
        {
            var local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FLPresence");
            // Full system drive: fall back to "<drive of the downloaded exe>:\Programs\FLPresence".
            try { if (new DriveInfo(Path.GetPathRoot(local)!).AvailableFreeSpace > 300L << 20) return local; }
            catch { return local; }
            return Path.Combine(Path.GetPathRoot(Environment.ProcessPath!)!, "Programs", "FLPresence");
        }
    }

    private static string InstalledExe => Path.Combine(InstallDir, "FLPresence.exe");

    /// <summary>True when this process installed/updated a copy and should exit.</summary>
    public static bool InstallIfNeeded()
    {
        var self = Environment.ProcessPath!;
        if (File.Exists(Path.Combine(Path.GetDirectoryName(self)!, "uninstall.ps1"))) return false; // install.ps1 layout
        // Running from the install dir, or from any dir that already holds an
        // install (e.g. install.ps1 -InstallDir, dev build) — nothing to do.
        if (string.Equals(Path.GetDirectoryName(self), InstallDir, StringComparison.OrdinalIgnoreCase)
            || Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue("FLPresence") is string runVal
               && runVal.Contains(self, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            foreach (var p in Process.GetProcessesByName("FLPresence"))
                if (p.Id != Environment.ProcessId) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
            Directory.CreateDirectory(InstallDir);
            File.Copy(self, InstalledExe, overwrite: true);

            using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
                run.SetValue("FLPresence", $"\"{InstalledExe}\" --watch");
            using (var u = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                var ver = typeof(SelfInstaller).Assembly.GetName().Version;
                u.SetValue("DisplayName", "FLPresence");
                u.SetValue("DisplayVersion", ver is null ? "" : $"{ver.Major}.{ver.Minor}.{ver.Build}");
                u.SetValue("Publisher", "FLPresence");
                u.SetValue("DisplayIcon", InstalledExe);
                u.SetValue("InstallLocation", InstallDir);
                u.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
                u.SetValue("NoModify", 1, RegistryValueKind.DWord);
                u.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            Process.Start(new ProcessStartInfo(InstalledExe, "--watch") { UseShellExecute = false, WorkingDirectory = InstallDir });

            MessageBox.Show(
                "FLPresence is installed.\n\nIt starts automatically together with FL Studio and closes " +
                "when FL Studio closes. While FL Studio is in focus, Discord shows your project.\n\n" +
                "Uninstall: Settings → Apps → Installed apps → FLPresence.",
                "FLPresence", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Install failed: " + ex.Message, "FLPresence",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return true;
    }

    public static void Uninstall()
    {
        foreach (var p in Process.GetProcessesByName("FLPresence"))
            if (p.Id != Environment.ProcessId) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
        try { Registry.CurrentUser.OpenSubKey(RunKey, true)?.DeleteValue("FLPresence", false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        try
        {
            if (BridgeInstaller.HardwareDir() is { } hw)
            {
                var dir = Path.Combine(hw, BridgeInstaller.DeviceFolderName);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                File.Delete(Path.Combine(hw, BridgeInstaller.DeviceFolderName + ".ini"));
            }
        }
        catch { }
        // The running exe can't delete itself: let cmd do it after we exit.
        Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{Path.GetDirectoryName(Environment.ProcessPath)}\"")
        { CreateNoWindow = true, UseShellExecute = false });
        MessageBox.Show("FLPresence was removed.\nSettings and logs were kept in %APPDATA% / %LOCALAPPDATA%\\FLPresence.",
            "FLPresence", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
