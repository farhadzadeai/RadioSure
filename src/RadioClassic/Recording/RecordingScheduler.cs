using RadioClassic.Core;
using RadioClassic.Data;
using RadioClassic.Models;

namespace RadioClassic.Recording;

/// <summary>
/// Starts recordings from the schedules stored in the database. The scheduler only works while
/// RadioClassic is running (possibly minimized in the tray); it never pretends to record while
/// the application or the computer is off.
/// </summary>
public sealed class RecordingScheduler : IDisposable
{
    private readonly RecordingRepository _repo;
    private readonly RecordingManager _manager;
    private readonly StationRepository _stations;
    private readonly Timer _timer;
    private readonly HashSet<long> _activeScheduleIds = [];
    private int _busy;

    /// <summary>Raised when a scheduled recording starts; string is the station name.</summary>
    public event Action<string>? ScheduledRecordingStarted;

    public RecordingScheduler(RecordingRepository repo, RecordingManager manager, StationRepository stations)
    {
        _repo = repo;
        _manager = manager;
        _stations = stations;
        _timer = new Timer(Tick, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15));
    }

    private void Tick(object? state)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;
        try
        {
            var now = DateTime.Now;
            List<RecordingSchedule> schedules;
            try { schedules = _repo.Schedules(); }
            catch (Exception ex) { Log.Error("Reading schedules failed", ex); return; }

            foreach (var schedule in schedules)
            {
                if (!schedule.Enabled) continue;
                var occurrence = schedule.ActiveOccurrence(now);
                if (occurrence is not { } occ) continue;

                // Each occurrence is started once. When an occurrence is still in progress it is
                // recorded for its remaining time; windows that already ended never trigger.
                if (schedule.LastRun is { } last && last >= occ.Start) continue;

                schedule.LastRun = occ.Start;
                try { _repo.SaveSchedule(schedule); } catch { }

                StartOccurrence(schedule, occ.End);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private void StartOccurrence(RecordingSchedule schedule, DateTime end)
    {
        // Scheduled recordings may exceed the interactive concurrency cap by design: the user
        // explicitly asked for them.
        var station = _stations.Get(schedule.StationId) ?? new Station
        {
            Id = schedule.StationId,
            Name = schedule.StationName,
            Url = schedule.StationUrl
        };
        station.Name = schedule.StationName.Length > 0 ? schedule.StationName : station.Name;
        if (station.Url.Length == 0) station.Url = schedule.StationUrl;

        if (_manager.IsRecordingStation(station.Id, station.Url)) return;
        var session = _manager.Start(station, endAt: end, scheduled: true);
        if (session is not null)
        {
            _activeScheduleIds.Add(schedule.Id);
            ScheduledRecordingStarted?.Invoke(station.Name);
        }
        else
        {
            Log.Warn($"Scheduled recording could not start ({schedule.StationName})");
        }
    }

    public void Dispose() => _timer.Dispose();
}
