using System.IO;

namespace RadioClassic.Core;

/// <summary>
/// Locations of user data. In portable mode (a "portable.flag" file next to the executable)
/// everything is kept in a "Data" folder beside the application.
/// </summary>
public static class AppPaths
{
    public const string AppName = "RadioClassic";

    public static string BaseDir { get; } = AppContext.BaseDirectory;
    public static bool IsPortable { get; } = File.Exists(Path.Combine(BaseDir, "portable.flag"));

    public static string DataDir { get; } = IsPortable
        ? Path.Combine(BaseDir, "Data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string DatabaseFile => Path.Combine(DataDir, "radioclassic.db");
    public static string LogFile => Path.Combine(DataDir, "radioclassic.log");
    public static string LogoCacheDir => Path.Combine(DataDir, "logos");

    public static string DefaultRecordingDir => IsPortable
        ? Path.Combine(BaseDir, "Recordings")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "RadioClassic Recordings");

    public static void EnsureDataDir() => Directory.CreateDirectory(DataDir);
}
