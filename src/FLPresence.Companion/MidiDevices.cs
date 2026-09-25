using System.Runtime.InteropServices;

namespace FLPresence.Companion;

/// <summary>
/// WinMM MIDI input enumeration (device-independent: shows every source the
/// system sees, never assumes any particular controller).
/// </summary>
public static class MidiDevices
{
    public sealed record MidiInDevice(int Index, string Name);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MIDIINCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public ushort wTechnology;
        public ushort wVoices;
    }

    [DllImport("winmm.dll")]
    private static extern uint midiInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint midiInGetDevCapsW(uint index, ref MIDIINCAPS caps, int size);

    /// <summary>All currently present MIDI input devices.</summary>
    public static List<MidiInDevice> GetInputs()
    {
        var list = new List<MidiInDevice>();
        try
        {
            uint n = midiInGetNumDevs();
            for (uint i = 0; i < n; i++)
            {
                var caps = new MIDIINCAPS();
                if (midiInGetDevCapsW(i, ref caps, Marshal.SizeOf<MIDIINCAPS>()) == 0 &&
                    !string.IsNullOrEmpty(caps.szPname))
                    list.Add(new MidiInDevice((int)i, caps.szPname));
            }
        }
        catch (Exception)
        {
            // winmm unavailable (should not happen): report empty, never crash.
        }
        return list;
    }

    public const string VirtualPortName = "FLPresence";
    public const string LoopMidiMarker = "loopMIDI";

    /// <summary>True when a virtual FLPresence port (or any loopMIDI port) exists.</summary>
    public static bool HasVirtualPort() =>
        GetInputs().Any(d => d.Name == VirtualPortName ||
                             d.Name.Contains(LoopMidiMarker, StringComparison.OrdinalIgnoreCase));

    /// <summary>Download the official loopMIDI installer to dir; returns the exe path.</summary>
    public static string? DownloadLoopMidiInstaller(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, "loopMIDI.zip");
            using (var http = new System.Net.Http.HttpClient())
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("FLPresence/1.1");
                using var stream = http.GetStreamAsync(LoopMidiUrl).GetAwaiter().GetResult();
                using var file = File.Create(zip);
                stream.CopyTo(file);
            }
            System.IO.Compression.ZipFile.ExtractToDirectory(zip, dir, overwriteFiles: true);
            var exe = Directory.EnumerateFiles(dir, "loopMIDISetup.exe", SearchOption.AllDirectories).First();
            return exe;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Launches the loopMIDI installer elevated (UAC prompt). Returns false when declined.</summary>
    public static bool RunLoopMidiInstaller(string exe)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            return p is not null;
        }
        catch (Exception)
        {
            return false; // UAC declined / cancelled
        }
    }

    public const string LoopMidiUrl =
        "https://www.tobias-erichsen.de/wp-content/uploads/2020/01/loopMIDISetup_1_0_16_27.zip";
}
