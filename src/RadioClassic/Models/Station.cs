namespace RadioClassic.Models;

/// <summary>A radio station as stored in the local database.</summary>
public sealed class Station
{
    public long Id { get; set; }
    public string? Uuid { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Homepage { get; set; } = "";
    public string Favicon { get; set; } = "";
    public string Country { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string Language { get; set; } = "";
    public string Tags { get; set; } = "";
    public string Codec { get; set; } = "";
    public int Bitrate { get; set; }
    public int Votes { get; set; }
    public int Clicks { get; set; }
    public bool LastCheckOk { get; set; } = true;
    public bool IsHls { get; set; }
    public bool IsCustom { get; set; }

    // Populated only by favorites queries.
    public bool IsFavorite { get; set; }
    public string FavoriteFolder { get; set; } = "";

    // Display helpers for the station list (kept short on purpose, like the classic list).
    public string GenreDisplay => Text.TitleCaseList(Tags, 2);
    public string LanguageDisplay => Text.TitleCaseList(Language, 2);
    public string BitrateDisplay => Bitrate > 0 ? Bitrate.ToString() : "";

    public override string ToString() => Name;
}

public enum StationView
{
    All,
    Favorites,
    Recent,
    Custom
}

public enum StationSort
{
    Name,
    Country,
    Genre,
    Language,
    Bitrate,
    Votes,
    Clicks
}

public sealed class StationQuery
{
    public StationView View { get; set; } = StationView.All;
    public string Search { get; set; } = "";
    public string Country { get; set; } = "";
    public string Language { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Codec { get; set; } = "";
    public int MinBitrate { get; set; }
    public string FavoriteFolder { get; set; } = "";
    public StationSort Sort { get; set; } = StationSort.Name;
    public bool Descending { get; set; }
}

public static class Text
{
    /// <summary>Removes control characters and trims untrusted text (stream metadata, API fields).</summary>
    public static string Clean(string? value, int maxLength = 300)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var span = value.AsSpan().Trim();
        var buffer = new System.Text.StringBuilder(Math.Min(span.Length, maxLength));
        foreach (var c in span)
        {
            if (buffer.Length >= maxLength) break;
            buffer.Append(char.IsControl(c) ? ' ' : c);
        }
        return buffer.ToString().Trim();
    }

    /// <summary>"pop,rock,news" -> "Pop, Rock".</summary>
    public static string TitleCaseList(string? list, int maxItems)
    {
        if (string.IsNullOrWhiteSpace(list)) return "";
        var parts = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(", ", parts.Take(maxItems).Select(TitleCase));
    }

    public static string TitleCase(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
