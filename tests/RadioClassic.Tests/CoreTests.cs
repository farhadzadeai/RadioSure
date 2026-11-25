using RadioClassic.Audio;
using RadioClassic.Models;
using RadioClassic.Recording;
using RadioClassic.Services;

namespace RadioClassic.Tests;

/// <summary>Unit tests for the pure-logic parts of the application.</summary>
public class CoreTests
{
    // ---------------------------------------------------------------- metadata parsing

    [Theory]
    [InlineData("Artist - Title", "Artist", "Title")]
    [InlineData("Just A Title", "", "Just A Title")]
    [InlineData("A - B - C", "A", "B - C")]
    [InlineData("", "", "")]
    public void Metadata_SplitsArtistTitle(string input, string artist, string title)
    {
        var meta = StreamMetadata.FromCombined(input);
        Assert.Equal(artist, meta.Artist);
        Assert.Equal(title, meta.Title);
    }

    [Fact]
    public void Metadata_ParsesIcyBlock()
    {
        var meta = StreamMetadata.FromIcy("StreamTitle='Queen - Bohemian Rhapsody';StreamUrl='';");
        Assert.Equal("Queen", meta.Artist);
        Assert.Equal("Bohemian Rhapsody", meta.Title);
    }

    [Fact]
    public void Metadata_IcyHandlesApostrophes()
    {
        var meta = StreamMetadata.FromIcy("StreamTitle='It's My Life - Bon Jovi';");
        Assert.False(meta.IsEmpty);
    }

    [Fact]
    public void Metadata_OggComments()
    {
        var meta = StreamMetadata.FromComments(new[] { "ARTIST=Pink Floyd", "TITLE=Time" });
        Assert.Equal("Pink Floyd", meta.Artist);
        Assert.Equal("Time", meta.Title);
    }

    // ---------------------------------------------------------------- file naming

    [Fact]
    public void FileNamer_SanitizesInvalidCharacters()
    {
        var name = RecordingFileNamer.Sanitize("AC/DC: Back <in> Black? |*\"");
        foreach (var bad in new[] { '/', ':', '<', '>', '?', '|', '*', '"' })
            Assert.DoesNotContain(bad, name);
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void FileNamer_EmptyFallsBack()
    {
        Assert.Equal("recording", RecordingFileNamer.Sanitize(""));
        Assert.Equal("recording", RecordingFileNamer.Sanitize(null));
    }

    [Fact]
    public void FileNamer_TemplateExpands()
    {
        var result = RecordingFileNamer.Expand("{station}_{artist}_{title}_{date}",
            "Radio X", "Queen", "Bohemian Rhapsody", new DateTime(2025, 3, 4, 15, 30, 0));
        Assert.Contains("Radio X", result);
        Assert.Contains("Queen", result);
        Assert.Contains("Bohemian Rhapsody", result);
        Assert.Contains("2025-03-04", result);
    }

    [Fact]
    public void FileNamer_TemplateTidiesEmptyTokens()
    {
        var result = RecordingFileNamer.Expand("{station}_{artist}_{title}", "Radio X", "", "", new DateTime(2025, 3, 4));
        Assert.DoesNotContain("__", result);
    }

    [Fact]
    public void FileNamer_DuplicatesGetUniqueSuffix()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rc-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var first = RecordingFileNamer.UniquePath(dir, "song", ".mp3");
            File.WriteAllBytes(first, [1]);
            var second = RecordingFileNamer.UniquePath(dir, "song", ".mp3");
            Assert.NotEqual(first, second);
            File.WriteAllBytes(second, [1]);
            var third = RecordingFileNamer.UniquePath(dir, "song", ".mp3");
            Assert.NotEqual(first, third);
            Assert.NotEqual(second, third);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---------------------------------------------------------------- equalizer frequency response

    [Fact]
    public void Equalizer_FlatHasUnityResponse()
    {
        var eq = new Equalizer();
        eq.Update(new EqSettings { Enabled = true });
        // At flat settings the response at any frequency should be ~0 dB.
        Assert.True(Math.Abs(eq.ResponseDb(1000, 44100)) < 0.5);
    }

    [Fact]
    public void Equalizer_BandBoostRaisesItsFrequency()
    {
        var eq = new Equalizer();
        var settings = new EqSettings { Enabled = true };
        settings.Bands[0] = 12; // +12 dB at 32 Hz
        eq.Update(settings);
        var atTarget = eq.ResponseDb(32, 44100);
        var farAway = eq.ResponseDb(10000, 44100);
        Assert.True(atTarget > 6, $"32 Hz should be boosted, got {atTarget:F1} dB");
        Assert.True(Math.Abs(farAway) < atTarget, $"10 kHz ({farAway:F1} dB) should be far below 32 Hz ({atTarget:F1} dB)");
    }

    [Fact]
    public void Equalizer_PreampScalesEverything()
    {
        var eq = new Equalizer();
        eq.Update(new EqSettings { Enabled = true, Preamp = -6 });
        Assert.True(eq.ResponseDb(1000, 44100) < -4);
    }

    // ---------------------------------------------------------------- playlists

    [Fact]
    public void PlaylistIO_ParsesM3U()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".m3u");
        File.WriteAllText(file, "#EXTM3U\n#EXTINF:-1,Test Radio\nhttp://example.com/stream.mp3\n");
        try
        {
            var entries = PlaylistIO.Import(file);
            Assert.Single(entries);
            Assert.Equal("Test Radio", entries[0].Name);
            Assert.Equal("http://example.com/stream.mp3", entries[0].Url);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void PlaylistIO_ParsesPLS()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pls");
        File.WriteAllText(file, "[playlist]\nNumberOfEntries=1\nFile1=http://example.com/aac\nTitle1=PLS Radio\n");
        try
        {
            var entries = PlaylistIO.Import(file);
            Assert.Single(entries);
            Assert.Equal("PLS Radio", entries[0].Name);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void PlaylistIO_RejectsInvalidUrls()
    {
        Assert.False(PlaylistIO.IsValidStreamUrl("javascript:alert(1)"));
        Assert.False(PlaylistIO.IsValidStreamUrl("file:///c:/windows/system32/calc.exe"));
        Assert.False(PlaylistIO.IsValidStreamUrl(""));
        Assert.False(PlaylistIO.IsValidStreamUrl(null));
        Assert.True(PlaylistIO.IsValidStreamUrl("http://example.com/stream"));
        Assert.True(PlaylistIO.IsValidStreamUrl("https://example.com/stream"));
    }

    // ---------------------------------------------------------------- settings

    [Fact]
    public void Settings_RoundTrips()
    {
        var file = Path.Combine(Path.GetTempPath(), "rc-set-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var service = new SettingsService(file);
            service.Load();
            service.Current.BufferMs = 4321;
            service.Current.Theme = "DarkClassic";
            service.Save();
            var again = new SettingsService(file);
            again.Load();
            Assert.Equal(4321, again.Current.BufferMs);
            Assert.Equal("DarkClassic", again.Current.Theme);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void Settings_CorruptFileFallsBackToDefaults()
    {
        var file = Path.Combine(Path.GetTempPath(), "rc-set-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, "{ this is not json !!!");
        try
        {
            var service = new SettingsService(file);
            service.Load();
            Assert.Equal(new AppSettings().BufferMs, service.Current.BufferMs);
        }
        finally { File.Delete(file); }
    }

    // ---------------------------------------------------------------- database

    [Fact]
    public void Database_StationCrudAndFavorites()
    {
        var file = Path.Combine(Path.GetTempPath(), "rc-db-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var db = new Data.Database(file);
            var repo = new Data.StationRepository(db);
            var id = repo.AddCustom(new Station { Name = "Test FM", Url = "http://example.com/x", Country = "Germany" });
            Assert.True(id > 0);
            var station = repo.Get(id);
            Assert.NotNull(station);
            Assert.Equal("Test FM", station!.Name);

            repo.SetFavorite(id);
            Assert.True(repo.IsFavorite(id));
            Assert.Single(repo.Favorites());
            repo.RemoveFavorite(id);
            Assert.False(repo.IsFavorite(id));

            station.Name = "Renamed";
            repo.Update(station);
            Assert.Equal("Renamed", repo.Get(id)!.Name);

            repo.AddHistory(station);
            Assert.Single(repo.History());
            repo.ClearHistory();
            Assert.Empty(repo.History());
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void Database_SearchFindsByNameAndFilters()
    {
        var file = Path.Combine(Path.GetTempPath(), "rc-db-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var db = new Data.Database(file);
            var repo = new Data.StationRepository(db);
            repo.AddCustom(new Station { Name = "Jazz Lounge", Url = "http://example.com/1", Country = "France", Tags = "jazz" });
            repo.AddCustom(new Station { Name = "Rock Antenne", Url = "http://example.com/2", Country = "Germany", Tags = "rock" });

            var jazz = repo.Search(new StationQuery { Search = "jazz" });
            Assert.Single(jazz);
            Assert.Equal("Jazz Lounge", jazz[0].Name);

            var germany = repo.Search(new StationQuery { Country = "Germany" });
            Assert.Single(germany);
            Assert.Equal("Rock Antenne", germany[0].Name);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    // ---------------------------------------------------------------- schedule logic

    [Fact]
    public void Schedule_DailyOccurrenceComputes()
    {
        var schedule = new RecordingSchedule
        {
            Start = DateTime.Today.AddHours(14).AddMinutes(30),
            DurationMinutes = 60,
            Repeat = ScheduleRepeat.Daily
        };
        var next = schedule.NextStart(DateTime.Today.AddHours(10));
        Assert.NotNull(next);
        Assert.Equal(14, next!.Value.Hour);
        Assert.Equal(30, next.Value.Minute);
    }

    [Fact]
    public void Schedule_WeeklyRespectsDays()
    {
        var monday = new DateTime(2025, 3, 3, 9, 0, 0); // a Monday
        while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(1);
        var schedule = new RecordingSchedule
        {
            Start = monday.AddHours(1), // 10:00
            DurationMinutes = 30,
            Repeat = ScheduleRepeat.Weekly,
            Days = (1 << (int)DayOfWeek.Monday) | (1 << (int)DayOfWeek.Friday)
        };
        var wednesday = monday.AddDays(2).AddHours(12);
        var next = schedule.NextStart(wednesday);
        Assert.NotNull(next);
        Assert.Equal(DayOfWeek.Friday, next!.Value.DayOfWeek);
    }

    [Fact]
    public void Schedule_DisabledNeverFires()
    {
        var schedule = new RecordingSchedule
        {
            Enabled = false,
            Start = DateTime.Now.AddHours(1),
            Repeat = ScheduleRepeat.Daily
        };
        Assert.Null(schedule.NextStart(DateTime.Now));
    }

    [Fact]
    public void Schedule_OnceFiresOnlyInFuture()
    {
        var future = new RecordingSchedule { Start = DateTime.Now.AddHours(2), Repeat = ScheduleRepeat.Once };
        Assert.NotNull(future.NextStart(DateTime.Now));
        var past = new RecordingSchedule { Start = DateTime.Now.AddHours(-2), Repeat = ScheduleRepeat.Once };
        Assert.Null(past.NextStart(DateTime.Now));
    }
}
