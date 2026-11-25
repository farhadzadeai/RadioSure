using RadioClassic.Core;
using RadioClassic.Data;
using RadioClassic.Models;

namespace RadioClassic.Services;

/// <summary>
/// Keeps the local station cache up to date. The UI always reads from the local database, so
/// the application stays fully usable when the internet or the online database is unavailable.
/// </summary>
public sealed class StationSyncService(RadioBrowserClient client, StationRepository stations, SettingsService settings)
{
    private int _running;

    public bool IsRunning => _running != 0;

    /// <summary>Progress: number of stations received so far.</summary>
    public event Action<int>? Progress;
    /// <summary>Completion: (success, total stations or 0, error message).</summary>
    public event Action<bool, int, string>? Completed;

    public bool IsRefreshDue()
    {
        var last = settings.Current.LastDatabaseSync;
        return stations.Count() == 0 || last is null ||
               DateTime.Now - last.Value > TimeSpan.FromDays(settings.Current.DatabaseRefreshDays);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0) return;
        var received = 0;
        try
        {
            var generation = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await Task.Run(async () =>
            {
                var batch = new List<Station>(2000);
                await foreach (var station in client.GetAllStationsAsync(ct))
                {
                    batch.Add(station);
                    if (batch.Count < 2000) continue;
                    stations.UpsertRemote(batch, generation);
                    received += batch.Count;
                    batch.Clear();
                    Progress?.Invoke(received);
                }
                if (batch.Count > 0)
                {
                    stations.UpsertRemote(batch, generation);
                    received += batch.Count;
                }
                // Only prune when the download looks complete; a truncated response must never
                // wipe the cached list.
                if (received >= 1000) stations.FinishSync(generation);
            }, ct);

            if (received < 1000) throw new InvalidOperationException("The station list received was incomplete.");
            settings.Current.LastDatabaseSync = DateTime.Now;
            settings.Save();
            Log.Info($"Station database refreshed: {received} stations");
            Completed?.Invoke(true, received, "");
        }
        catch (Exception ex)
        {
            Log.Error("Station database refresh failed", ex);
            Completed?.Invoke(false, received, ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}
