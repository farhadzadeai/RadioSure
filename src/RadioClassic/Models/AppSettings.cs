using RadioClassic.Core;

namespace RadioClassic.Models;

public enum RecordingFormat
{
    /// <summary>Keep the stream exactly as broadcast (no transcoding, no quality loss).</summary>
    Original,
    Mp3,
    Wav
}

public enum ProxyMode
{
    System,
    None,
    Manual
}

public sealed class EqSettings
{
    public static readonly int[] BandFrequencies = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public bool Enabled { get; set; }
    public string Preset { get; set; } = "Flat";
    /// <summary>Gain per band in dB (-12..+12), 10 bands.</summary>
    public double[] Bands { get; set; } = new double[10];
    public double Bass { get; set; }
    public double Treble { get; set; }
    public double Preamp { get; set; }
    /// <summary>-1 (left) .. +1 (right).</summary>
    public double Balance { get; set; }
}

public sealed class EqPreset
{
    public string Name { get; set; } = "";
    public double[] Bands { get; set; } = new double[10];
    public double Bass { get; set; }
    public double Treble { get; set; }
    public double Preamp { get; set; }

    public static readonly EqPreset[] BuiltIn =
    [
        new() { Name = "Flat" },
        new() { Name = "Rock", Bands = [5, 4, 3, 1, -1, -1, 1, 3, 4, 5] },
        new() { Name = "Pop", Bands = [-1, 1, 3, 4, 4, 3, 1, 0, -1, -1] },
        new() { Name = "Jazz", Bands = [3, 2, 1, 2, -1, -1, 0, 1, 2, 3] },
        new() { Name = "Classical", Bands = [4, 3, 2, 1, -1, -1, 0, 2, 3, 4] },
        new() { Name = "Vocal", Bands = [-3, -2, -1, 1, 4, 4, 3, 1, 0, -2] },
        new() { Name = "Bass Boost", Bands = [7, 6, 5, 3, 1, 0, 0, 0, 0, 0] },
    ];
}

public sealed class AppSettings
{
    // General
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; }
    public bool RememberLastStation { get; set; } = true;
    public bool ResumeOnStart { get; set; }
    public int Volume { get; set; } = 70;
    public bool Muted { get; set; }
    public string Language { get; set; } = "en";
    public bool ShowNotifications { get; set; } = true;

    // Playback
    public int BufferMs { get; set; } = 5000;
    public bool AutoReconnect { get; set; } = true;
    public int RetryAttempts { get; set; } = 5;
    /// <summary>BASS output device index; -1 = Windows default device.</summary>
    public int OutputDevice { get; set; } = -1;

    // Recording
    public string RecordingDir { get; set; } = AppPaths.DefaultRecordingDir;
    public RecordingFormat RecordingFormat { get; set; } = RecordingFormat.Original;
    public int Mp3Bitrate { get; set; } = 192;
    public bool SplitTracks { get; set; } = true;
    public string TrackFileTemplate { get; set; } = "{station}_{artist}_{title}_{date}";
    public string ContinuousFileTemplate { get; set; } = "{station}_{date}_{time}";
    public bool StationSubfolders { get; set; } = true;
    public int MaxConcurrentRecordings { get; set; } = 4;
    public bool NotifyScheduledRecordings { get; set; } = true;

    // Appearance
    public string Theme { get; set; } = "Classic";
    /// <summary>Optional accent override as #RRGGBB; null = theme default.</summary>
    public string? AccentColor { get; set; }
    public bool CompactMode { get; set; }
    public bool CompactAlwaysOnTop { get; set; }
    public bool ShowSpectrum { get; set; } = true;
    public bool ShowFilters { get; set; }

    // Network
    public int TimeoutSeconds { get; set; } = 10;
    public ProxyMode ProxyMode { get; set; } = ProxyMode.System;
    public string ProxyHost { get; set; } = "";
    public int ProxyPort { get; set; } = 8080;
    public int DatabaseRefreshDays { get; set; } = 7;
    /// <summary>Report station clicks to Radio-Browser (community popularity statistics). Off by default.</summary>
    public bool ReportClicks { get; set; }

    // Hotkeys
    public bool GlobalHotkeys { get; set; } = true;
    public bool MediaKeys { get; set; } = true;

    // Equalizer
    public EqSettings Equalizer { get; set; } = new();
    public List<EqPreset> CustomPresets { get; set; } = [];

    // Persistent UI state
    public long LastStationId { get; set; }
    public DateTime? LastDatabaseSync { get; set; }
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 560;
    public double WindowHeight { get; set; } = 440;
    public StationSort Sort { get; set; } = StationSort.Name;
    public bool SortDescending { get; set; }
    public List<string> SearchHistory { get; set; } = [];

    public void Sanitize()
    {
        Volume = Math.Clamp(Volume, 0, 100);
        BufferMs = Math.Clamp(BufferMs, 1000, 30000);
        RetryAttempts = Math.Clamp(RetryAttempts, 0, 50);
        Mp3Bitrate = Math.Clamp(Mp3Bitrate, 64, 320);
        MaxConcurrentRecordings = Math.Clamp(MaxConcurrentRecordings, 1, 10);
        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 3, 60);
        DatabaseRefreshDays = Math.Clamp(DatabaseRefreshDays, 1, 90);
        ProxyPort = Math.Clamp(ProxyPort, 1, 65535);
        if (string.IsNullOrWhiteSpace(RecordingDir)) RecordingDir = AppPaths.DefaultRecordingDir;
        if (string.IsNullOrWhiteSpace(TrackFileTemplate)) TrackFileTemplate = "{station}_{artist}_{title}_{date}";
        if (string.IsNullOrWhiteSpace(ContinuousFileTemplate)) ContinuousFileTemplate = "{station}_{date}_{time}";
        Equalizer ??= new EqSettings();
        if (Equalizer.Bands is not { Length: 10 }) Equalizer.Bands = new double[10];
        CustomPresets ??= [];
        SearchHistory ??= [];
        if (WindowWidth < 480 || double.IsNaN(WindowWidth)) WindowWidth = 560;
        if (WindowHeight < 300 || double.IsNaN(WindowHeight)) WindowHeight = 440;
    }
}
