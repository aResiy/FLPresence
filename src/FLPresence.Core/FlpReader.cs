using Microsoft.Win32;

namespace FLPresence.Core;

/// <summary>
/// Reads the tempo from a saved .flp and finds the file for the project name
/// shown in FL's title (FL's command line, then FL's own recent-files list).
/// The saved file is the only data FL exposes without a MIDI controller script.
/// </summary>
public static class FlpReader
{
    /// <summary>BPM stored in the project, or null if unreadable.</summary>
    public static double? ReadBpm(string path)
    {
        try
        {
            using var r = new BinaryReader(File.OpenRead(path));
            if (new string(r.ReadChars(4)) != "FLhd") return null;
            r.BaseStream.Seek(r.ReadInt32(), SeekOrigin.Current);
            if (new string(r.ReadChars(4)) != "FLdt") return null;
            r.ReadInt32();
            double? legacy = null;
            while (r.BaseStream.Position < r.BaseStream.Length)
            {
                int id = r.ReadByte();
                if (id < 64) r.ReadByte();
                else if (id < 128)
                {
                    int w = r.ReadUInt16();
                    if (id == 66) legacy = w;                 // old (FL < 3.4) tempo
                }
                else if (id < 192)
                {
                    uint d = r.ReadUInt32();
                    if (id == 156) return d / 1000.0;         // FL 3.4+ tempo * 1000
                }
                else
                {
                    long len = 0; int shift = 0; byte b;
                    do { b = r.ReadByte(); len |= (long)(b & 0x7F) << shift; shift += 7; } while ((b & 0x80) != 0);
                    r.BaseStream.Seek(len, SeekOrigin.Current);
                }
            }
            return legacy;
        }
        catch { return null; }
    }

    /// <summary>Full path of the open project named <paramref name="project"/>, or null.</summary>
    public static string? FindProject(string project, string? commandLine)
    {
        if (string.IsNullOrEmpty(project)) return null;
        var candidates = new List<string>();
        if (commandLine is { } cl)
            foreach (var part in cl.Split('"'))
                if (part.Trim().EndsWith(".flp", StringComparison.OrdinalIgnoreCase)) candidates.Add(part.Trim());
        try
        {
            using var il = Registry.CurrentUser.OpenSubKey(@"Software\Image-Line");
            foreach (var fl in il?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var mru = il!.OpenSubKey(fl + @"\MRU");
                foreach (var n in (mru?.GetValueNames() ?? Array.Empty<string>()).OrderBy(n => int.TryParse(n, out var i) ? i : 999))
                    if (mru!.GetValue(n) is string s) candidates.Add(s);
            }
        }
        catch { /* registry unreadable: command line only */ }
        return candidates.FirstOrDefault(c =>
            string.Equals(Path.GetFileNameWithoutExtension(c), project, StringComparison.OrdinalIgnoreCase) && File.Exists(c));
    }
}
