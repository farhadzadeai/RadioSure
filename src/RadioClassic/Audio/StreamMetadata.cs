using RadioClassic.Models;

namespace RadioClassic.Audio;

/// <summary>Track information extracted from a stream (Shoutcast/Icecast "StreamTitle" or Ogg comments).</summary>
public sealed record StreamMetadata(string Artist, string Title)
{
    public static readonly StreamMetadata Empty = new("", "");

    public bool IsEmpty => Artist.Length == 0 && Title.Length == 0;

    /// <summary>"Artist - Title", or whichever part is available.</summary>
    public string Display =>
        Artist.Length > 0 && Title.Length > 0 ? $"{Artist} - {Title}" : Artist.Length > 0 ? Artist : Title;

    /// <summary>Parses a Shoutcast metadata block such as <c>StreamTitle='Artist - Song';StreamUrl='';</c>.</summary>
    public static StreamMetadata FromIcy(string? meta)
    {
        if (string.IsNullOrEmpty(meta)) return Empty;
        const string key = "StreamTitle='";
        var start = meta.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return Empty;
        start += key.Length;
        // The title itself may contain apostrophes, so look for the terminating "';".
        var end = meta.IndexOf("';", start, StringComparison.Ordinal);
        if (end < 0) end = meta.LastIndexOf('\'');
        if (end < start) return Empty;
        return FromCombined(meta[start..end]);
    }

    /// <summary>Splits "Artist - Title" at the first " - ".</summary>
    public static StreamMetadata FromCombined(string? combined)
    {
        var text = Text.Clean(combined);
        if (text.Length == 0 || text == "-") return Empty;
        var sep = text.IndexOf(" - ", StringComparison.Ordinal);
        return sep > 0
            ? new StreamMetadata(text[..sep].Trim(), text[(sep + 3)..].Trim())
            : new StreamMetadata("", text);
    }

    /// <summary>Builds metadata from Ogg/Opus/FLAC comments ("ARTIST=..", "TITLE=..").</summary>
    public static StreamMetadata FromComments(IEnumerable<string> comments)
    {
        string artist = "", title = "";
        foreach (var c in comments)
        {
            if (c.StartsWith("ARTIST=", StringComparison.OrdinalIgnoreCase)) artist = Text.Clean(c[7..]);
            else if (c.StartsWith("TITLE=", StringComparison.OrdinalIgnoreCase)) title = Text.Clean(c[6..]);
        }
        if (artist.Length == 0 && title.Contains(" - ")) return FromCombined(title);
        return artist.Length == 0 && title.Length == 0 ? Empty : new StreamMetadata(artist, title);
    }
}
