namespace RadioClassic.Models;

/// <summary>One recorded file, as kept in the recording history.</summary>
public sealed class RecordingEntry
{
    public long Id { get; set; }
    public string StationName { get; set; } = "";
    public string StationUrl { get; set; } = "";
    public string Title { get; set; } = "";
    public string File { get; set; } = "";
    public string Format { get; set; } = "";
    public DateTime Started { get; set; }
    public DateTime? Ended { get; set; }
    public long Bytes { get; set; }
    /// <summary>recording | completed | interrupted | failed</summary>
    public string Status { get; set; } = RecordingStatus.Recording;
    public string Error { get; set; } = "";
    public bool Scheduled { get; set; }

    public string FileName => System.IO.Path.GetFileName(File);
    public string SizeDisplay => Formatting.Size(Bytes);
    public string DurationDisplay => Ended is { } e ? Formatting.Duration(e - Started) : "";
    public string StartedDisplay => Started.ToString("yyyy-MM-dd HH:mm");
}

public static class RecordingStatus
{
    public const string Recording = "recording";
    public const string Completed = "completed";
    public const string Interrupted = "interrupted";
    public const string Failed = "failed";
}

public enum ScheduleRepeat
{
    Once,
    Daily,
    Weekly
}

/// <summary>A scheduled recording. Runs only while RadioClassic itself is running.</summary>
public sealed class RecordingSchedule
{
    public long Id { get; set; }
    public long StationId { get; set; }
    public string StationName { get; set; } = "";
    public string StationUrl { get; set; } = "";
    /// <summary>First start (local time). For repeating schedules the time-of-day part is reused.</summary>
    public DateTime Start { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public ScheduleRepeat Repeat { get; set; }
    /// <summary>Bit mask of <see cref="DayOfWeek"/> values (bit 0 = Sunday) for weekly schedules.</summary>
    public int Days { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>Start of the last occurrence that was handled (started, or detected as missed).</summary>
    public DateTime? LastRun { get; set; }

    public string StartDisplay => Start.ToString("yyyy-MM-dd HH:mm");
    public string DurationDisplay => $"{DurationMinutes} min";

    /// <summary>
    /// Returns the occurrence (start, end) that is active at <paramref name="now"/>, if any.
    /// </summary>
    public (DateTime Start, DateTime End)? ActiveOccurrence(DateTime now)
    {
        var duration = TimeSpan.FromMinutes(Math.Max(1, DurationMinutes));
        if (Repeat == ScheduleRepeat.Once)
            return now >= Start && now < Start + duration ? (Start, Start + duration) : null;

        // A repeating occurrence may have started yesterday and run past midnight.
        for (var back = 0; back <= 1; back++)
        {
            var day = now.Date.AddDays(-back);
            var start = day + Start.TimeOfDay;
            if (start < Start) continue;
            if (Repeat == ScheduleRepeat.Weekly && (Days & (1 << (int)day.DayOfWeek)) == 0) continue;
            if (now >= start && now < start + duration) return (start, start + duration);
        }
        return null;
    }

    /// <summary>Returns the next start time strictly after <paramref name="now"/>, or null if none.</summary>
    public DateTime? NextStart(DateTime now)
    {
        if (!Enabled) return null;
        if (Repeat == ScheduleRepeat.Once) return Start > now ? Start : null;
        if (Repeat == ScheduleRepeat.Weekly && (Days & 0x7F) == 0) return null;

        var day = now.Date > Start.Date ? now.Date : Start.Date;
        for (var i = 0; i < 9; i++, day = day.AddDays(1))
        {
            var start = day + Start.TimeOfDay;
            if (start <= now || start < Start) continue;
            if (Repeat == ScheduleRepeat.Weekly && (Days & (1 << (int)day.DayOfWeek)) == 0) continue;
            return start;
        }
        return null;
    }
}

public static class Formatting
{
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB"
    };

    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
}

public sealed class HistoryEntry
{
    public long StationId { get; set; }
    public string StationName { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime PlayedAt { get; set; }
    public string PlayedAtDisplay => PlayedAt.ToString("yyyy-MM-dd HH:mm");
}
