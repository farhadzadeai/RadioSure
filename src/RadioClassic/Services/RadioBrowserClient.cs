using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using RadioClassic.Core;
using RadioClassic.Models;

namespace RadioClassic.Services;

/// <summary>
/// Client for the community Radio-Browser API (https://docs.radio-browser.info/).
/// Only three kinds of requests are made: server discovery, the station list download and,
/// if the user enabled it, a click report for the station being played.
/// </summary>
public sealed class RadioBrowserClient : IDisposable
{
    private static readonly string[] FallbackServers =
        ["de1.api.radio-browser.info", "de2.api.radio-browser.info", "fi1.api.radio-browser.info"];

    private readonly SettingsService _settings;
    private HttpClient _http;
    private string? _server;

    public static string UserAgent { get; } =
        $"RadioClassic/{typeof(RadioBrowserClient).Assembly.GetName().Version?.ToString(3) ?? "1.0"}";

    public RadioBrowserClient(SettingsService settings)
    {
        _settings = settings;
        _http = CreateClient();
        settings.Changed += () =>
        {
            var old = _http;
            _http = CreateClient();
            old.Dispose();
        };
    }

    private HttpClient CreateClient()
    {
        var s = _settings.Current;
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(s.TimeoutSeconds),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        switch (s.ProxyMode)
        {
            case ProxyMode.None:
                handler.UseProxy = false;
                break;
            case ProxyMode.Manual when !string.IsNullOrWhiteSpace(s.ProxyHost):
                handler.Proxy = new WebProxy(s.ProxyHost.Trim(), s.ProxyPort);
                break;
        }
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    /// <summary>Asks the API for its server list and picks one at random, as the documentation recommends.</summary>
    private async Task<List<string>> GetServersAsync(CancellationToken ct)
    {
        var servers = new List<string>();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_settings.Current.TimeoutSeconds));
            await using var stream = await _http.GetStreamAsync("https://all.api.radio-browser.info/json/servers", cts.Token);
            var list = await JsonSerializer.DeserializeAsync<List<ServerInfo>>(stream, cancellationToken: cts.Token);
            servers.AddRange((list ?? []).Select(x => x.Name).Where(IsValidHost).Distinct()!);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.Warn("Radio-Browser server discovery failed: " + ex.Message);
        }
        Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(servers));
        if (_server is not null) { servers.Remove(_server); servers.Insert(0, _server); }
        servers.AddRange(FallbackServers.Where(f => !servers.Contains(f)));
        return servers;
    }

    private static bool IsValidHost(string? host) =>
        !string.IsNullOrEmpty(host) && host.EndsWith(".radio-browser.info", StringComparison.OrdinalIgnoreCase)
        && Uri.CheckHostName(host) == UriHostNameType.Dns;

    /// <summary>
    /// Streams the complete list of working stations. The API paginates the list (default page
    /// size is 1000), so pages are fetched in sequence and each page's JSON is parsed
    /// incrementally - the download never has to be held in memory as a whole.
    /// </summary>
    public async IAsyncEnumerable<Station> GetAllStationsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        const int pageSize = 5000;
        string? server = null;
        Exception? last = null;
        foreach (var candidate in await GetServersAsync(ct))
        {
            if (await ProbeAsync(candidate, ct))
            {
                server = candidate;
                _server = candidate;
                break;
            }
            last = new HttpRequestException($"Server {candidate} did not respond");
        }
        if (server is null) throw new HttpRequestException("No Radio-Browser server could be reached.", last);

        for (var offset = 0; ; offset += pageSize)
        {
            var pageCount = 0;
            Stream? stream = null;
            try
            {
                var response = await _http.GetAsync(
                    $"https://{server}/json/stations?hidebroken=true&limit={pageSize}&offset={offset}",
                    HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                stream = await response.Content.ReadAsStreamAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                throw new HttpRequestException($"Station list download failed at offset {offset}.", ex);
            }

            await using (stream)
            {
                await foreach (var dto in JsonSerializer.DeserializeAsyncEnumerable<StationDto>(stream, cancellationToken: ct))
                {
                    if (dto is null) continue;
                    pageCount++;
                    var station = dto.ToStation();
                    if (station is not null) yield return station;
                }
            }
            if (pageCount < pageSize) yield break; // short page = end of the list
        }
    }

    private async Task<bool> ProbeAsync(string server, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_settings.Current.TimeoutSeconds));
            using var response = await _http.GetAsync($"https://{server}/json/stats", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.Warn($"Radio-Browser server {server} failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Counts a click for the station (optional, user-controlled).</summary>
    public async Task ReportClickAsync(string uuid)
    {
        if (!Guid.TryParse(uuid, out _)) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.Current.TimeoutSeconds));
            var server = _server ?? FallbackServers[0];
            using var response = await _http.GetAsync($"https://{server}/json/url/{uuid}", cts.Token);
        }
        catch (Exception ex)
        {
            Log.Warn("Click report failed: " + ex.Message);
        }
    }

    /// <summary>Downloads a small file (station logo) with a hard size limit.</summary>
    public async Task<byte[]?> DownloadSmallAsync(string url, int maxBytes, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_settings.Current.TimeoutSeconds));
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int n;
            while ((n = await stream.ReadAsync(chunk, cts.Token)) > 0)
            {
                if (buffer.Length + n > maxBytes) return null;
                buffer.Write(chunk, 0, n);
            }
            return buffer.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();

    private sealed class ServerInfo
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class StationDto
    {
        [JsonPropertyName("stationuuid")] public string? Uuid { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("url_resolved")] public string? UrlResolved { get; set; }
        [JsonPropertyName("homepage")] public string? Homepage { get; set; }
        [JsonPropertyName("favicon")] public string? Favicon { get; set; }
        [JsonPropertyName("tags")] public string? Tags { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
        [JsonPropertyName("countrycode")] public string? CountryCode { get; set; }
        [JsonPropertyName("language")] public string? Language { get; set; }
        [JsonPropertyName("codec")] public string? Codec { get; set; }
        [JsonPropertyName("bitrate")] public int Bitrate { get; set; }
        [JsonPropertyName("votes")] public int Votes { get; set; }
        [JsonPropertyName("clickcount")] public int Clicks { get; set; }
        [JsonPropertyName("lastcheckok")] public int LastCheckOk { get; set; }
        [JsonPropertyName("hls")] public int Hls { get; set; }

        public Station? ToStation()
        {
            var url = string.IsNullOrWhiteSpace(UrlResolved) ? Url : UrlResolved;
            var name = Text.Clean(Name, 200);
            if (string.IsNullOrEmpty(Uuid) || name.Length == 0 || !PlaylistIO.IsValidStreamUrl(url)) return null;
            return new Station
            {
                Uuid = Uuid,
                Name = name,
                Url = url!.Trim(),
                Homepage = Text.Clean(Homepage, 500),
                Favicon = Text.Clean(Favicon, 500),
                Tags = Text.Clean(Tags, 200),
                Country = Text.Clean(Country, 80),
                CountryCode = Text.Clean(CountryCode, 4),
                Language = Text.Clean(Language, 120),
                Codec = Text.Clean(Codec, 20),
                Bitrate = Math.Clamp(Bitrate, 0, 10000),
                Votes = Math.Max(0, Votes),
                Clicks = Math.Max(0, Clicks),
                LastCheckOk = LastCheckOk != 0,
                IsHls = Hls != 0
            };
        }
    }
}
