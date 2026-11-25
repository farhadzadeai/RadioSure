using System.IO;
using System.Text;
using RadioClassic.Models;

namespace RadioClassic.Recording;

/// <summary>Builds safe recording file names from user templates and prevents name collisions.</summary>
public static class RecordingFileNamer
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();
    private const int MaxNameLength = 120;

    /// <summary>Replaces characters that are invalid in Windows file names.</summary>
    public static string Sanitize(string? value, string fallback = "recording")
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var sb = new StringBuilder(Math.Min(value.Length, MaxNameLength + 20));
        foreach (var c in value.Trim())
        {
            if (sb.Length >= MaxNameLength + 20) break;
            sb.Append(Array.IndexOf(InvalidChars, c) >= 0 || char.IsControl(c) ? '-' : c);
        }
        var cleaned = sb.ToString().Trim(' ', '.');
        return cleaned.Length == 0 ? fallback : cleaned;
    }

    /// <summary>
    /// Expands a template like "{station}_{artist}_{title}_{date}".
    /// Tokens: station, artist, title, date, time, datetime. Empty tokens are dropped cleanly.
    /// </summary>
    public static string Expand(string template, string station, string artist, string title, DateTime when)
    {
        var result = template
            .Replace("{station}", Sanitize(station, "station"))
            .Replace("{artist}", Sanitize(artist, ""))
            .Replace("{title}", Sanitize(title, ""))
            .Replace("{date}", when.ToString("yyyy-MM-dd"))
            .Replace("{time}", when.ToString("HHmmss"))
            .Replace("{datetime}", when.ToString("yyyy-MM-dd_HHmmss"));
        // Tidy up separators left behind by empty tokens.
        while (result.Contains("__")) result = result.Replace("__", "_");
        return Sanitize(result.Trim('_', ' '), "recording");
    }

    /// <summary>
    /// Returns a full path that does not yet exist, appending " (2)", " (3)"... on collision,
    /// and creates the target directory.
    /// </summary>
    public static string UniquePath(string directory, string baseName, string extension)
    {
        Directory.CreateDirectory(directory);
        baseName = baseName.Length > MaxNameLength ? baseName[..MaxNameLength] : baseName;
        for (var i = 1; ; i++)
        {
            var name = i == 1 ? baseName : $"{baseName} ({i})";
            var path = Path.Combine(directory, name + extension);
            if (!File.Exists(path)) return path;
            if (i > 999) throw new IOException("Could not find a free file name.");
        }
    }

    /// <summary>Recording directory for a station (a subfolder per station when enabled).</summary>
    public static string StationDirectory(string root, string station, bool subfolders) =>
        subfolders ? Path.Combine(root, Sanitize(station, "station")) : root;
}
