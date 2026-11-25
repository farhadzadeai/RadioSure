using System.Windows;
using RadioClassic.Audio;
using RadioClassic.Core;
using RadioClassic.Data;
using RadioClassic.Recording;
using RadioClassic.Services;
using RadioClassic.ViewModels;
using RadioClassic.Views;

namespace RadioClassic;

/// <summary>All application services, handed to the windows that need them.</summary>
public sealed class AppServices
{
    public required SettingsService Settings { get; init; }
    public required Database Database { get; init; }
    public required StationRepository Stations { get; init; }
    public required RecordingRepository RecordingStore { get; init; }
    public required AudioEngine Engine { get; init; }
    public required RecordingManager Recordings { get; init; }
    public required RecordingScheduler Scheduler { get; init; }
    public required StationSyncService Sync { get; init; }
    public required RadioBrowserClient Client { get; init; }
    public required TrayService Tray { get; init; }
    public required HotkeyService Hotkeys { get; init; }
}

public partial class App : Application
{
    private AppServices? _services;
    private MainWindow? _mainWindow;
    private bool _shutdownStarted;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!SingleInstance.TryBecomePrimary())
        {
            // Another instance is already running; it has been asked to show its window.
            Shutdown();
            return;
        }
        base.OnStartup(e);

        AppPaths.EnsureDataDir();
        Log.Info("RadioClassic starting");

        // ---- settings, language, theme
        var settings = new SettingsService();
        settings.Load();
        Loc.SetLanguage(settings.Current.Language);

        var boolToVis = new BoolToVisibilityConverter();
        Resources["BoolToVis"] = boolToVis;
        Resources["InverseBool"] = new InverseBoolConverter();
        Resources["InverseBoolToVis"] = new InverseBoolToVisibilityConverter();

        ThemeService.Apply(settings.Current.Theme, settings.Current.AccentColor);
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/RadioClassic;component/Themes/Controls.xaml", UriKind.Relative)
        });

        // ---- data
        var database = new Database();
        var stations = new StationRepository(database);
        var recordingStore = new RecordingRepository(database);
        var interrupted = recordingStore.MarkInterrupted();
        if (interrupted > 0)
            Log.Warn($"{interrupted} recording(s) from the previous session did not finish cleanly and were marked interrupted");

        // ---- audio + recording
        var engine = new AudioEngine(settings);
        var recordings = new RecordingManager(recordingStore, settings);
        var scheduler = new RecordingScheduler(recordingStore, recordings, stations);

        // ---- network services
        var client = new RadioBrowserClient(settings);
        var sync = new StationSyncService(client, stations, settings);

        // ---- windows integration
        var tray = new TrayService();
        tray.Initialize();
        var hotkeys = new HotkeyService();
        hotkeys.Initialize();
        hotkeys.Apply(settings.Current.GlobalHotkeys, settings.Current.MediaKeys);

        _services = new AppServices
        {
            Settings = settings,
            Database = database,
            Stations = stations,
            RecordingStore = recordingStore,
            Engine = engine,
            Recordings = recordings,
            Scheduler = scheduler,
            Sync = sync,
            Client = client,
            Tray = tray,
            Hotkeys = hotkeys
        };

        // ---- react to settings changes centrally
        settings.Changed += () =>
        {
            var s = settings.Current;
            engine.ApplyNetworkSettings();
            engine.Equalizer.Update(s.Equalizer);
            hotkeys.Apply(s.GlobalHotkeys, s.MediaKeys);
            StartupService.SetEnabled(s.StartWithWindows);
        };

        scheduler.ScheduledRecordingStarted += name =>
        {
            if (!settings.Current.NotifyScheduledRecordings || !settings.Current.ShowNotifications) return;
            Dispatcher.BeginInvoke(() => tray.Balloon("RadioClassic", Loc.T("Sch.Started", name)));
        };

        // ---- main window
        var vm = new MainViewModel(settings, stations, recordingStore, engine, recordings, sync, client);
        _mainWindow = new MainWindow(vm, _services);
        _mainWindow.ApplyFlowDirection();
        MainWindow = _mainWindow;
        _mainWindow.Show();

        SingleInstance.ListenForActivation(() => _mainWindow?.RestoreFromTray());

        // ---- station database + optional resume
        vm.StartSyncIfDue();
        var s2 = settings.Current;
        if (s2.ResumeOnStart && s2.RememberLastStation && s2.LastStationId != 0)
        {
            Task.Run(() =>
            {
                var station = stations.Get(s2.LastStationId);
                if (station is not null)
                    Dispatcher.BeginInvoke(() => vm.Play(station));
            });
        }
    }

    /// <summary>Clean, idempotent shutdown: stop audio and recordings, persist everything.</summary>
    public void ShutdownGracefully()
    {
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        try
        {
            if (_services is not null)
            {
                Log.Info("RadioClassic shutting down");
                _services.Engine.Dispose();
                _services.Recordings.StopAll();
                foreach (var session in _services.Recordings.Sessions)
                    session.Dispose();
                _services.Settings.Save();
                _services.Tray.Dispose();
                _services.Hotkeys.Dispose();
                _services.Scheduler.Dispose();
                _services.Client.Dispose();
                _services.Database.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error while shutting down", ex);
        }
        SingleInstance.Release();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Covers exits that did not go through ShutdownGracefully (e.g. Windows session end).
        if (!_shutdownStarted) ShutdownGracefully();
        base.OnExit(e);
    }
}
