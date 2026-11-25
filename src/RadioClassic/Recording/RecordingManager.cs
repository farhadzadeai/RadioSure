using RadioClassic.Core;
using RadioClassic.Data;
using RadioClassic.Models;
using RadioClassic.Services;

namespace RadioClassic.Recording;

/// <summary>
/// Owns all recording sessions. Any station can be recorded independently of what is playing;
/// sessions are limited by the configurable concurrency cap.
/// </summary>
public sealed class RecordingManager
{
    private readonly RecordingRepository _repo;
    private readonly SettingsService _settings;
    private readonly object _gate = new();
    private readonly List<RecordingSession> _sessions = [];

    public event Action? SessionsChanged;

    public RecordingManager(RecordingRepository repo, SettingsService settings)
    {
        _repo = repo;
        _settings = settings;
    }

    public IReadOnlyList<RecordingSession> Sessions
    {
        get { lock (_gate) return _sessions.ToArray(); }
    }

    public int ActiveCount
    {
        get { lock (_gate) return _sessions.Count(s => s.IsRecording); }
    }

    public bool IsRecordingStation(long stationId, string url)
    {
        lock (_gate)
            return _sessions.Any(s => s.IsRecording &&
                (stationId != 0 && s.StationId == stationId || s.StationUrl == url));
    }

    public RecordingSession? FindByStation(long stationId, string url)
    {
        lock (_gate)
            return _sessions.FirstOrDefault(s => s.IsRecording &&
                (stationId != 0 && s.StationId == stationId || s.StationUrl == url));
    }

    /// <summary>Starts a recording. Returns null with an error key when the limits or input prevent it.</summary>
    public RecordingSession? Start(Station station, DateTime? endAt = null, bool scheduled = false)
    {
        if (!Services.PlaylistIO.IsValidStreamUrl(station.Url)) return null;
        var s = _settings.Current;
        lock (_gate)
        {
            _sessions.RemoveAll(x => !x.IsRecording);
            if (endAt is null && _sessions.Count(x => x.IsRecording) >= s.MaxConcurrentRecordings) return null;
            if (_sessions.Any(x => x.IsRecording && x.StationUrl == station.Url)) return null;
        }

        var options = new RecordingOptions(
            s.RecordingFormat, s.Mp3Bitrate, s.SplitTracks,
            s.TrackFileTemplate, s.ContinuousFileTemplate,
            s.RecordingDir, s.StationSubfolders, Math.Max(1, s.RetryAttempts));

        var session = new RecordingSession(station.Id, station.Name, station.Url, options, _repo, endAt, scheduled);
        session.Changed += _ => SessionsChanged?.Invoke();
        session.Ended += ended =>
        {
            lock (_gate) _sessions.Remove(ended);
            SessionsChanged?.Invoke();
        };
        lock (_gate) _sessions.Add(session);
        session.Start();
        Log.Info($"Recording started: {station.Name}{(scheduled ? " (scheduled)" : "")}");
        SessionsChanged?.Invoke();
        return session;
    }

    /// <summary>Stops the recording of a station, if one is active. Returns true when something was stopped.</summary>
    public bool StopStation(long stationId, string url)
    {
        var session = FindByStation(stationId, url);
        if (session is null) return false;
        session.Stop();
        return true;
    }

    public void StopAll()
    {
        foreach (var session in Sessions) session.Stop();
    }
}
