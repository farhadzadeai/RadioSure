using RadioClassic.Audio;
using RadioClassic.Data;
using RadioClassic.Models;
using RadioClassic.Recording;
using RadioClassic.Services;

namespace RadioClassic.Tests;

/// <summary>
/// Integration tests that exercise the real audio engine and the real Radio-Browser service.
/// They require internet access and are marked with a longer timeout; they verify the core
/// promise of the application: playing and recording real internet radio.
/// </summary>
[Collection("Integration")]
public class IntegrationTests
{
    // A long-lived, reliable MP3 stream (SomaFM is a well-known always-on test stream).
    private const string TestStreamMp3 = "https://ice1.somafm.com/groovesalad-128-mp3";

    private static AudioEngine CreateEngine()
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "rc-itest-" + Guid.NewGuid().ToString("N") + ".json"));
        settings.Load();
        return new AudioEngine(settings);
    }

    [Fact(Timeout = 60000)]
    public async Task Engine_PlaysRealMp3Stream()
    {
        using var engine = CreateEngine();
        engine.Initialize();

        var stateChanges = new List<PlayerState>();
        engine.StateChanged += () => stateChanges.Add(engine.State);

        engine.Play(new Station { Name = "SomaFM Groove Salad", Url = TestStreamMp3 });

        // Wait up to 25 seconds for playback to start.
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (engine.State != PlayerState.Playing && DateTime.UtcNow < deadline)
            await Task.Delay(250);

        Assert.Equal(PlayerState.Playing, engine.State);

        // Let it actually play a few seconds and confirm the stream position advances.
        await Task.Delay(3000);
        Assert.True(engine.Elapsed > TimeSpan.FromSeconds(2), $"elapsed should advance, got {engine.Elapsed}");

        engine.Stop();
        Assert.Equal(PlayerState.Stopped, engine.State);
    }

    [Fact(Timeout = 60000)]
    public async Task Engine_HandlesDeadStreamGracefully()
    {
        using var engine = CreateEngine();
        engine.Initialize();
        engine.Play(new Station { Name = "Dead", Url = "http://192.0.2.1:9/nothing.mp3" }); // TEST-NET-1, unroutable

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (engine.State is PlayerState.Connecting && DateTime.UtcNow < deadline)
            await Task.Delay(300);

        // Must never crash; should end in error or still retrying - either way, stoppable.
        engine.Stop();
        Assert.Equal(PlayerState.Stopped, engine.State);
    }

    [Fact(Timeout = 90000)]
    public async Task Recording_CapturesRealStream()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rc-rec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dbFile = Path.Combine(Path.GetTempPath(), "rc-recdb-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var settingsFile = Path.Combine(dir, "settings.json");
            var settings = new SettingsService(settingsFile);
            settings.Load();
            settings.Current.RecordingDir = dir;
            settings.Current.RecordingFormat = RecordingFormat.Original;
            settings.Save();

            using var db = new Database(dbFile);
            var repo = new RecordingRepository(db);
            var manager = new RecordingManager(repo, settings);

            var session = manager.Start(new Station { Name = "SomaFM", Url = TestStreamMp3 });
            Assert.NotNull(session);

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (session!.TotalBytes < 20_000 && DateTime.UtcNow < deadline)
                await Task.Delay(500);

            Assert.True(session.TotalBytes > 10_000, $"expected audio data, got {session.TotalBytes} bytes");
            manager.StopAll();

            await Task.Delay(1500); // let the file flush/close
            var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".json")).ToArray();
            Assert.NotEmpty(files);
            Assert.True(new FileInfo(files[0]).Length > 5_000, "recorded file should contain data");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            try { File.Delete(dbFile); } catch { }
        }
    }

    [Fact(Timeout = 60000)]
    public async Task RadioBrowser_FetchesStationList()
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "rc-rb-" + Guid.NewGuid().ToString("N") + ".json"));
        settings.Load();
        using var client = new RadioBrowserClient(settings);

        var count = 0;
        await foreach (var station in client.GetAllStationsAsync())
        {
            count++;
            if (count >= 1200) break; // proves pagination works past the first 1000
        }
        Assert.True(count >= 1200, $"expected paged results beyond 1000, got {count}");
    }
}
