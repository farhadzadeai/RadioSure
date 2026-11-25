using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using RadioClassic.Core;

namespace RadioClassic.Audio;

/// <summary>
/// Minimal P/Invoke layer for the BASS audio library (un4seen.com) and the BASSenc add-ons.
/// Only the functions RadioClassic needs are declared.
/// </summary>
internal static unsafe class Bass
{
    private const string Dll = "bass";
    private const string EncDll = "bassenc";
    private const string EncMp3Dll = "bassenc_mp3";

    // ---- config options
    public const int CONFIG_BUFFER = 0;
    public const int CONFIG_UPDATEPERIOD = 1;
    public const int CONFIG_NET_TIMEOUT = 11;
    public const int CONFIG_NET_BUFFER = 12;
    public const int CONFIG_NET_PREBUF = 15;
    public const int CONFIG_NET_AGENT = 16;
    public const int CONFIG_NET_PROXY = 17;
    public const int CONFIG_NET_PLAYLIST = 21;
    public const int CONFIG_NET_READTIMEOUT = 37;
    public const int CONFIG_UNICODE = 42;

    // ---- stream flags
    public const uint SAMPLE_FLOAT = 0x100;
    public const uint STREAM_BLOCK = 0x100000;
    public const uint STREAM_DECODE = 0x200000;
    public const uint UNICODE = 0x80000000;

    // ---- channel state
    public const int ACTIVE_STOPPED = 0;
    public const int ACTIVE_PLAYING = 1;
    public const int ACTIVE_STALLED = 2;
    public const int ACTIVE_PAUSED = 3;

    // ---- attributes
    public const int ATTRIB_VOL = 2;
    public const int ATTRIB_BITRATE = 12;

    // ---- tags
    public const int TAG_OGG = 2;
    public const int TAG_HTTP = 3;
    public const int TAG_ICY = 4;
    public const int TAG_META = 5;

    // ---- syncs
    public const uint SYNC_END = 2;
    public const uint SYNC_META = 4;
    public const uint SYNC_OGG_CHANGE = 12;

    // ---- data
    public const uint DATA_AVAILABLE = 0;
    public const uint DATA_FFT256 = 0x80000000;

    // ---- file positions
    public const int FILEPOS_END = 2;
    public const int FILEPOS_CONNECTED = 4;
    public const int FILEPOS_BUFFER = 5;

    // ---- channel types
    public const int CTYPE_STREAM_OGG = 0x10002;
    public const int CTYPE_STREAM_MP1 = 0x10003;
    public const int CTYPE_STREAM_MP2 = 0x10004;
    public const int CTYPE_STREAM_MP3 = 0x10005;
    public const int CTYPE_STREAM_MF = 0x10008;
    public const int CTYPE_STREAM_WMA = 0x10300;
    public const int CTYPE_STREAM_FLAC = 0x10900;
    public const int CTYPE_STREAM_FLAC_OGG = 0x10901;
    public const int CTYPE_STREAM_HLS = 0x10A00;
    public const int CTYPE_STREAM_AAC = 0x10B00;
    public const int CTYPE_STREAM_MP4 = 0x10B01;
    public const int CTYPE_STREAM_OPUS = 0x11200;

    // ---- errors
    public const int ERROR_FILEOPEN = 2;
    public const int ERROR_HANDLE = 5;
    public const int ERROR_FORMAT = 6;
    public const int ERROR_NONET = 32;
    public const int ERROR_TIMEOUT = 40;
    public const int ERROR_FILEFORM = 41;
    public const int ERROR_CODEC = 44;
    public const int ERROR_ENDED = 45;
    public const int ERROR_SSL = 10;

    // ---- device flags
    public const uint DEVICE_ENABLED = 1;

    // ---- BASSenc
    public const uint ENCODE_FP_16BIT = 4;
    public const uint ENCODE_PCM = 64;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void SyncProc(uint handle, uint channel, uint data, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void DownloadProc(IntPtr buffer, uint length, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void DspProc(uint handle, uint channel, IntPtr buffer, uint length, IntPtr user);

    [StructLayout(LayoutKind.Sequential)]
    public struct ChannelInfo
    {
        public uint freq;
        public uint chans;
        public uint flags;
        public uint ctype;
        public uint origres;
        public uint plugin;
        public uint sample;
        public IntPtr filename;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInfo
    {
        public IntPtr name;
        public IntPtr driver;
        public uint flags;
    }

    [DllImport(Dll)] public static extern bool BASS_Init(int device, uint freq, uint flags, IntPtr win, IntPtr clsid);
    [DllImport(Dll)] public static extern bool BASS_Free();
    [DllImport(Dll)] public static extern bool BASS_SetDevice(uint device);
    [DllImport(Dll)] public static extern bool BASS_GetDeviceInfo(uint device, out DeviceInfo info);
    [DllImport(Dll)] public static extern int BASS_ErrorGetCode();
    [DllImport(Dll)] public static extern bool BASS_SetConfig(int option, uint value);
    [DllImport(Dll)] public static extern bool BASS_SetConfigPtr(int option, IntPtr value);
    [DllImport(Dll, CharSet = CharSet.Unicode)] public static extern uint BASS_PluginLoad(string file, uint flags);
    [DllImport(Dll)] public static extern uint BASS_ChannelGetLevel(uint handle);
    [DllImport(Dll, CharSet = CharSet.Unicode)]
    public static extern uint BASS_StreamCreateURL(string url, uint offset, uint flags, DownloadProc? proc, IntPtr user);
    [DllImport(Dll)] public static extern bool BASS_StreamFree(uint handle);
    [DllImport(Dll)] public static extern long BASS_StreamGetFilePosition(uint handle, int mode);
    [DllImport(Dll)] public static extern bool BASS_ChannelPlay(uint handle, bool restart);
    [DllImport(Dll)] public static extern bool BASS_ChannelPause(uint handle);
    [DllImport(Dll)] public static extern bool BASS_ChannelStop(uint handle);
    [DllImport(Dll)] public static extern int BASS_ChannelIsActive(uint handle);
    [DllImport(Dll)] public static extern bool BASS_ChannelGetInfo(uint handle, out ChannelInfo info);
    [DllImport(Dll)] public static extern bool BASS_ChannelSetAttribute(uint handle, int attrib, float value);
    [DllImport(Dll)] public static extern bool BASS_ChannelGetAttribute(uint handle, int attrib, out float value);
    [DllImport(Dll)] public static extern bool BASS_ChannelSlideAttribute(uint handle, int attrib, float value, uint time);
    [DllImport(Dll)] public static extern int BASS_ChannelGetData(uint handle, void* buffer, uint length);
    [DllImport(Dll)] public static extern IntPtr BASS_ChannelGetTags(uint handle, int tags);
    [DllImport(Dll)] public static extern uint BASS_ChannelSetSync(uint handle, uint type, long param, SyncProc proc, IntPtr user);
    [DllImport(Dll)] public static extern uint BASS_ChannelSetDSP(uint handle, DspProc proc, IntPtr user, int priority);

    [DllImport(EncDll, CharSet = CharSet.Unicode)]
    public static extern uint BASS_Encode_Start(uint handle, string cmdline, uint flags, IntPtr proc, IntPtr user);
    [DllImport(EncDll)] public static extern bool BASS_Encode_Stop(uint handle);
    [DllImport(EncDll)] public static extern int BASS_Encode_IsActive(uint handle);

    [DllImport(EncMp3Dll, CharSet = CharSet.Unicode)]
    public static extern uint BASS_Encode_MP3_StartFile(uint handle, string options, uint flags, string filename);

    // ------------------------------------------------------------------ helpers

    private static readonly object InitGate = new();
    private static bool _pluginsLoaded;
    private static IntPtr _agentPtr, _proxyPtr;

    /// <summary>Loads the format add-ons (AAC, HLS, Opus, FLAC) once per process.</summary>
    private static int _decodeInitState; // 0 = not tried, 1 = ready, -1 = failed

    /// <summary>
    /// Decode-only streams (used by the recording engine) still need BASS to be initialized.
    /// Device 0 is the silent "no sound" device, so this never touches the real audio output.
    /// </summary>
    public static bool EnsureDecodeInit()
    {
        if (_decodeInitState != 0) return _decodeInitState > 0;
        lock (InitGate)
        {
            if (_decodeInitState != 0) return _decodeInitState > 0;
            LoadPlugins();
            var ok = BASS_Init(0, 48000, 0, IntPtr.Zero, IntPtr.Zero) || BASS_ErrorGetCode() == 14; // 14 = already initialized
            _decodeInitState = ok ? 1 : -1;
            if (!ok) Log.Warn($"BASS decode init failed (error {BASS_ErrorGetCode()})");
            return ok;
        }
    }
    public static void LoadPlugins()
    {
        lock (InitGate)
        {
            if (_pluginsLoaded) return;
            _pluginsLoaded = true;
            var dir = NativeLibraryDir();
            foreach (var name in new[] { "bass_aac.dll", "basshls.dll", "bassopus.dll", "bassflac.dll" })
            {
                var path = Path.Combine(dir, name);
                if (BASS_PluginLoad(path, UNICODE) == 0)
                    Log.Warn($"BASS add-on {name} could not be loaded from {dir} (error {BASS_ErrorGetCode()})");
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, int nSize);

    /// <summary>
    /// The directory the native bass.dll actually lives in. Normally this is next to the exe,
    /// but single-file builds extract native libraries to a bundle-extraction directory instead;
    /// plugins must be loaded from wherever bass.dll really is or AAC/HLS/Opus/FLAC silently break.
    /// </summary>
    private static string NativeLibraryDir()
    {
        try
        {
            // A harmless call forces the CLR to load bass.dll through its own resolution logic,
            // which always works - including single-file extraction.
            BASS_ErrorGetCode();
            var h = GetModuleHandle("bass.dll");
            if (h != IntPtr.Zero)
            {
                var sb = new StringBuilder(1024);
                if (GetModuleFileName(h, sb, sb.Capacity) != 0)
                {
                    var dir = Path.GetDirectoryName(sb.ToString());
                    if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "bass_aac.dll")))
                        return dir;
                }
            }
        }
        catch { }
        return AppPaths.BaseDir;
    }

    public static void SetUserAgent(string agent)
    {
        lock (InitGate)
        {
            var ptr = Marshal.StringToHGlobalAnsi(agent);
            BASS_SetConfigPtr(CONFIG_NET_AGENT, ptr);
            if (_agentPtr != IntPtr.Zero) Marshal.FreeHGlobal(_agentPtr);
            _agentPtr = ptr;
        }
    }

    /// <summary>null = no proxy, "" = Windows proxy settings, "host:port" = manual proxy.</summary>
    public static void SetProxy(string? proxy)
    {
        lock (InitGate)
        {
            var ptr = proxy is null ? IntPtr.Zero : Marshal.StringToHGlobalAnsi(proxy);
            BASS_SetConfigPtr(CONFIG_NET_PROXY, ptr);
            if (_proxyPtr != IntPtr.Zero) Marshal.FreeHGlobal(_proxyPtr);
            _proxyPtr = ptr;
        }
    }

    /// <summary>Reads a null-terminated string, as UTF-8 when valid and as Latin-1 otherwise.</summary>
    public static string? PtrToString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        var p = (byte*)ptr;
        var len = 0;
        while (p[len] != 0 && len < 16384) len++;
        return Decode(new ReadOnlySpan<byte>(p, len));
    }

    /// <summary>Reads a series of null-terminated strings that ends with an empty string.</summary>
    public static List<string> PtrToStringList(IntPtr ptr)
    {
        var list = new List<string>();
        if (ptr == IntPtr.Zero) return list;
        var p = (byte*)ptr;
        while (*p != 0 && list.Count < 256)
        {
            var len = 0;
            while (p[len] != 0 && len < 16384) len++;
            list.Add(Decode(new ReadOnlySpan<byte>(p, len)));
            p += len + 1;
        }
        return list;
    }

    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    public static string CodecName(uint ctype) => (int)ctype switch
    {
        CTYPE_STREAM_MP3 => "MP3",
        CTYPE_STREAM_MP2 => "MP2",
        CTYPE_STREAM_MP1 => "MP1",
        CTYPE_STREAM_OGG => "OGG",
        CTYPE_STREAM_AAC or CTYPE_STREAM_MP4 => "AAC",
        CTYPE_STREAM_OPUS => "Opus",
        CTYPE_STREAM_FLAC or CTYPE_STREAM_FLAC_OGG => "FLAC",
        CTYPE_STREAM_HLS => "HLS",
        CTYPE_STREAM_WMA => "WMA",
        CTYPE_STREAM_MF => "MF",
        _ => "Stream"
    };

    /// <summary>Maps a BASS error code to a localization key with a clear explanation.</summary>
    public static string ErrorKey(int code) => code switch
    {
        ERROR_TIMEOUT => "Err.Timeout",
        ERROR_FILEOPEN => "Err.CannotOpen",
        ERROR_NONET => "Err.NoInternet",
        ERROR_FILEFORM or ERROR_CODEC or ERROR_FORMAT => "Err.Unsupported",
        ERROR_SSL => "Err.Ssl",
        _ => "Err.Unknown"
    };
}
