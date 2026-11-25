using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml;
using RadioClassic.Models;

namespace RadioClassic.Services;

/// <summary>
/// Import and export of station lists as M3U, PLS, XSPF and RadioClassic JSON.
/// Imported files are untrusted: size is limited, XML DTDs are refused and only http/https
/// stream addresses are accepted.
/// </summary>
public static class PlaylistIO
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxEntries = 5000;

    public static bool IsValidStreamUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.Length <= 2000 &&
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.Host.Length > 0;

    public static List<Station> Import(string file)
    {
        var info = new FileInfo(file);
        if (!info.Exists) throw new FileNotFoundException(file);
        if (info.Length > MaxFileBytes) throw new InvalidDataException("The playlist file is too large.");
        var text = File.ReadAllText(file);
        var ext = info.Extension.ToLowerInvariant();
        var head = text.AsSpan().TrimStart();

        var result = ext switch
        {
            ".pls" => ParsePls(text),
            ".xspf" => ParseXspf(text),
            ".json" => ParseJson(text),
            ".m3u" or ".m3u8" => ParseM3u(text),
            _ when head.StartsWith("[playlist]", StringComparison.OrdinalIgnoreCase) => ParsePls(text),
            _ when head.StartsWith("<") => ParseXspf(text),
            _ when head.StartsWith("[") || head.StartsWith("{") => ParseJson(text),
            _ => ParseM3u(text)
        };
        return result.Where(s => IsValidStreamUrl(s.Url)).Take(MaxEntries).ToList();
    }

    public static List<Station> ParseM3u(string text)
    {
        var list = new List<Station>();
        string? title = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                var comma = line.IndexOf(',');
                title = comma >= 0 ? line[(comma + 1)..] : null;
            }
            else if (!line.StartsWith('#'))
            {
                list.Add(Make(title, line));
                title = null;
            }
        }
        return list;
    }

    public static List<Station> ParsePls(string text)
    {
        var files = new SortedDictionary<int, string>();
        var titles = new Dictionary<int, string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (key.StartsWith("File", StringComparison.OrdinalIgnoreCase) && int.TryParse(key.AsSpan(4), out var fi))
                files[fi] = value;
            else if (key.StartsWith("Title", StringComparison.OrdinalIgnoreCase) && int.TryParse(key.AsSpan(5), out var ti))
                titles[ti] = value;
        }
        return files.Select(f => Make(titles.GetValueOrDefault(f.Key), f.Value)).ToList();
    }

    public static List<Station> ParseXspf(string text)
    {
        var list = new List<Station>();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxFileBytes
        };
        using var reader = XmlReader.Create(new StringReader(text), settings);
        var doc = System.Xml.Linq.XDocument.Load(reader);
        foreach (var track in doc.Descendants().Where(e => e.Name.LocalName == "track"))
        {
            var location = track.Elements().FirstOrDefault(e => e.Name.LocalName == "location")?.Value;
            var title = track.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value;
            if (!string.IsNullOrWhiteSpace(location)) list.Add(Make(title, location));
        }
        return list;
    }

    public static List<Station> ParseJson(string text)
    {
        var items = JsonSerializer.Deserialize<List<ExportedStation>>(text, JsonOptions) ?? [];
        return items.Select(i => new Station
        {
            Name = Text.Clean(i.Name, 200),
            Url = (i.Url ?? "").Trim(),
            Homepage = Text.Clean(i.Homepage, 500),
            Country = Text.Clean(i.Country, 80),
            Language = Text.Clean(i.Language, 120),
            Tags = Text.Clean(i.Genre, 200),
            FavoriteFolder = Text.Clean(i.Folder, 80),
            Uuid = Guid.TryParse(i.Uuid, out _) ? i.Uuid : null
        }).Select(s => { if (s.Name.Length == 0) s.Name = s.Url; return s; }).ToList();
    }

    private static Station Make(string? title, string url)
    {
        url = url.Trim();
        var name = Text.Clean(title, 200);
        if (name.Length == 0)
            name = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
        return new Station { Name = name, Url = url };
    }

    // ---------------------------------------------------------------- export

    public static void Export(string file, IReadOnlyList<Station> stations)
    {
        var text = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".pls" => ToPls(stations),
            ".xspf" => ToXspf(stations),
            ".json" => ToJson(stations),
            _ => ToM3u(stations)
        };
        File.WriteAllText(file, text, new UTF8Encoding(false));
    }

    public static string ToM3u(IReadOnlyList<Station> stations)
    {
        var sb = new StringBuilder("#EXTM3U\r\n");
        foreach (var s in stations)
            sb.Append("#EXTINF:-1,").Append(OneLine(s.Name)).Append("\r\n").Append(s.Url).Append("\r\n");
        return sb.ToString();
    }

    public static string ToPls(IReadOnlyList<Station> stations)
    {
        var sb = new StringBuilder("[playlist]\r\n");
        for (var i = 0; i < stations.Count; i++)
        {
            sb.Append($"File{i + 1}=").Append(stations[i].Url).Append("\r\n");
            sb.Append($"Title{i + 1}=").Append(OneLine(stations[i].Name)).Append("\r\n");
            sb.Append($"Length{i + 1}=-1\r\n");
        }
        sb.Append($"NumberOfEntries={stations.Count}\r\nVersion=2\r\n");
        return sb.ToString();
    }

    public static string ToXspf(IReadOnlyList<Station> stations)
    {
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false }))
        {
            w.WriteStartElement("playlist", "http://xspf.org/ns/0/");
            w.WriteAttributeString("version", "1");
            w.WriteStartElement("trackList");
            foreach (var s in stations)
            {
                w.WriteStartElement("track");
                w.WriteElementString("location", s.Url);
                w.WriteElementString("title", s.Name);
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }
        return sb.ToString();
    }

    public static string ToJson(IReadOnlyList<Station> stations) =>
        JsonSerializer.Serialize(stations.Select(s => new ExportedStation
        {
            Name = s.Name, Url = s.Url, Homepage = s.Homepage, Country = s.Country, Language = s.Language,
            Genre = s.Tags, Folder = s.FavoriteFolder, Uuid = s.Uuid
        }), JsonOptions);

    private static string OneLine(string s) => s.Replace('\r', ' ').Replace('\n', ' ');

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class ExportedStation
    {
        public string? Name { get; set; }
        public string? Url { get; set; }
        public string? Homepage { get; set; }
        public string? Country { get; set; }
        public string? Language { get; set; }
        public string? Genre { get; set; }
        public string? Folder { get; set; }
        public string? Uuid { get; set; }
    }
}
