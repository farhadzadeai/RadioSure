using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using RadioClassic.Audio;
using RadioClassic.Core;
using RadioClassic.Data;
using RadioClassic.Models;
using RadioClassic.Recording;
using RadioClassic.Services;

namespace RadioClassic.ViewModels;

/// <summary>
/// The view model behind the single compact main window: station browser, player state,
/// recording quick access and the menu actions.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly StationRepository _stations;
    private readonly RecordingRepository _recordings;
    private readonly AudioEngine _engine;
    private readonly RecordingManager _recordingManager;
    private readonly StationSyncService _sync;
    private readonly RadioBrowserClient _client;
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _searchDebounce;
    private CancellationTokenSource? _queryCts;

    public MainViewModel(SettingsService settings, StationRepository stations, RecordingRepository recordings,
        AudioEngine engine, RecordingManager recordingManager, StationSyncService sync, RadioBrowserClient client)
    {
        _settings = settings;
        _stations = stations;
        _recordings = recordings;
        _engine = engine;
        _recordingManager = recordingManager;
        _sync = sync;
        _client = client;

        Volume = settings.Current.Volume;
        _isMuted = settings.Current.Muted;
        _engine.Volume = Volume;
        _engine.Muted = _isMuted;

        _engine.StateChanged += () => Ui(RefreshPlayerState);
        _engine.MetadataChanged += () => Ui(OnMetadataChanged);
        _recordingManager.SessionsChanged += () => Ui(RefreshRecordingState);
        _sync.Progress += n => Ui(() => DatabaseStatus = Loc.T("UpdatingDbProgress", n));
        _sync.Completed += (ok, total, error) => Ui(() => OnSyncCompleted(ok, total, error));

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _uiTimer.Tick += (_, _) => Tick();
        _uiTimer.Start();

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); ReloadStations(); };

        LoadFilterLists();
        ReloadStations();
        RefreshPlayerState();
    }

    private static void Ui(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    // ================================================================== stations list

    public ObservableCollection<Station> Stations { get; } = [];

    private Station? _selectedStation;
    public Station? SelectedStation
    {
        get => _selectedStation;
        set { Set(ref _selectedStation, value); OnPropertyChanged(nameof(CanPlaySelected)); }
    }

    public bool CanPlaySelected => SelectedStation is not null;

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                _searchDebounce.Stop();
                _searchDebounce.Start();
            }
        }
    }

    private StationView _view;
    public StationView View
    {
        get => _view;
        set { if (Set(ref _view, value)) ReloadStations(); }
    }

    private string _stationCount = "";
    public string StationCount
    {
        get => _stationCount;
        set => Set(ref _stationCount, value);
    }

    private string _databaseStatus = "";
    public string DatabaseStatus
    {
        get => _databaseStatus;
        set => Set(ref _databaseStatus, value);
    }

    private bool _databaseBusy;
    public bool DatabaseBusy
    {
        get => _databaseBusy;
        set => Set(ref _databaseBusy, value);
    }

    // filters
    public ObservableCollection<string> Countries { get; } = [];
    public ObservableCollection<string> Languages { get; } = [];

    private string _filterCountry = "";
    public string FilterCountry
    {
        get => _filterCountry;
        set { if (Set(ref _filterCountry, value)) ReloadStations(); }
    }

    private string _filterLanguage = "";
    public string FilterLanguage
    {
        get => _filterLanguage;
        set { if (Set(ref _filterLanguage, value)) ReloadStations(); }
    }

    private string _filterGenre = "";
    public string FilterGenre
    {
        get => _filterGenre;
        set { if (Set(ref _filterGenre, value)) ReloadStations(); }
    }

    private string _filterCodec = "";
    public string FilterCodec
    {
        get => _filterCodec;
        set { if (Set(ref _filterCodec, value)) ReloadStations(); }
    }

    private int _filterMinBitrate;
    public int FilterMinBitrate
    {
        get => _filterMinBitrate;
        set { if (Set(ref _filterMinBitrate, value)) ReloadStations(); }
    }

    private string _favoriteFolder = "";
    public string FavoriteFolder
    {
        get => _favoriteFolder;
        set { if (Set(ref _favoriteFolder, value)) ReloadStations(); }
    }

    public ObservableCollection<string> FavoriteFolders { get; } = [];

    private bool _filtersVisible;
    public bool FiltersVisible
    {
        get => _filtersVisible;
        set => Set(ref _filtersVisible, value);
    }

    private StationQuery BuildQuery() => new()
    {
        View = View,
        Search = SearchText.Trim(),
        Country = FilterCountry,
        Language = FilterLanguage,
        Genre = FilterGenre,
        Codec = FilterCodec,
        MinBitrate = FilterMinBitrate,
        FavoriteFolder = FavoriteFolder,
        Sort = _settings.Current.Sort,
        Descending = _settings.Current.SortDescending
    };

    public void ReloadStations()
    {
        _queryCts?.Cancel();
        var cts = _queryCts = new CancellationTokenSource();
        var query = BuildQuery();
        Task.Run(() =>
        {
            List<Station> result;
            try { result = _stations.Search(query); }
            catch (Exception ex)
            {
                Log.Error("Station query failed", ex);
                return;
            }
            Ui(() =>
            {
                if (cts.IsCancellationRequested) return;
                FillStations(result);
            });
        });
    }

    /// <summary>Fills the list in small batches at background priority so the window never stalls.</summary>
    private void FillStations(List<Station> result)
    {
        Stations.Clear();
        StationCount = Loc.T("StationsFound", result.Count);
        var dispatcher = Application.Current.Dispatcher;
        var index = 0;
        void FillBatch()
        {
            var end = Math.Min(index + 1500, result.Count);
            for (; index < end; index++) Stations.Add(result[index]);
            if (index < result.Count)
                dispatcher.BeginInvoke((Action)FillBatch, DispatcherPriority.Background);
        }
        if (result.Count > 0) dispatcher.BeginInvoke((Action)FillBatch, DispatcherPriority.Background);
    }

    public void SortBy(StationSort sort)
    {
        var s = _settings.Current;
        if (s.Sort == sort) s.SortDescending = !s.SortDescending;
        else
        {
            s.Sort = sort;
            s.SortDescending = sort is StationSort.Votes or StationSort.Clicks or StationSort.Bitrate;
        }
        _settings.Save();
        ReloadStations();
    }

    public void LoadFilterLists()
    {
        Task.Run(() =>
        {
            List<string> countries, languages, folders;
            try
            {
                countries = _stations.Countries();
                languages = _stations.Languages();
                folders = _stations.FavoriteFolders();
            }
            catch { return; }
            Ui(() =>
            {
                Countries.Clear();
                Countries.Add(""); // empty first entry = no filter
                foreach (var c in countries) Countries.Add(c);
                Languages.Clear();
                Languages.Add("");
                foreach (var l in languages) Languages.Add(l);
                FavoriteFolders.Clear();
                FavoriteFolders.Add("");
                foreach (var f in folders) FavoriteFolders.Add(f);
            });
        });
    }

    // ================================================================== player state

    private string _stateText = "";
    public string StateText
    {
        get => _stateText;
        set => Set(ref _stateText, value);
    }

    private string _stationLine = "";
    public string StationLine
    {
        get => _stationLine;
        set => Set(ref _stationLine, value);
    }

    private string _trackLine = "";
    public string TrackLine
    {
        get => _trackLine;
        set => Set(ref _trackLine, value);
    }

    private string _qualityText = "";
    public string QualityText
    {
        get => _qualityText;
        set => Set(ref _qualityText, value);
    }

    private string _errorText = "";
    public string ErrorText
    {
        get => _errorText;
        set => Set(ref _errorText, value);
    }

    private int _bufferPercent;
    public int BufferPercent
    {
        get => _bufferPercent;
        set => Set(ref _bufferPercent, value);
    }

    private string _bufferText = "";
    public string BufferText
    {
        get => _bufferText;
        set => Set(ref _bufferText, value);
    }

    private string _elapsedText = "";
    public string ElapsedText
    {
        get => _elapsedText;
        set => Set(ref _elapsedText, value);
    }

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set => Set(ref _isPlaying, value);
    }

    private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused;
        set => Set(ref _isPaused, value);
    }

    private bool _isBuffering;
    public bool IsBuffering
    {
        get => _isBuffering;
        set => Set(ref _isBuffering, value);
    }

    public PlayerState PlayerState => _engine.State;
    public Station? PlayingStation => _engine.Station;
    public AudioEngine Engine => _engine;
    public RecordingManager Recordings => _recordingManager;
    public SettingsService Settings => _settings;
    public StationRepository StationStore => _stations;
    public RecordingRepository RecordingStore => _recordings;

    private void RefreshPlayerState()
    {
        var state = _engine.State;
        IsPlaying = state == PlayerState.Playing;
        IsPaused = state == PlayerState.Paused;
        IsBuffering = state is PlayerState.Connecting or PlayerState.Reconnecting;
        StateText = state switch
        {
            PlayerState.Connecting => Loc.T("State.Connecting"),
            PlayerState.Reconnecting => Loc.T("State.Reconnecting", _engine.ReconnectAttempt),
            PlayerState.Playing => Loc.T("State.Playing"),
            PlayerState.Paused => Loc.T("State.Paused"),
            PlayerState.Error => Loc.T("State.Error"),
            _ => Loc.T("State.Stopped")
        };
        ErrorText = state == PlayerState.Error ? Loc.T(_engine.LastErrorKey) : "";
        StationLine = _engine.Station?.Name ?? "";
        if (!IsPlaying && state is not (PlayerState.Paused)) { BufferPercent = 0; ElapsedText = ""; }
        if (state == PlayerState.Playing)
        {
            var kbps = _engine.BitrateKbps;
            QualityText = kbps > 0 ? Loc.T("QualityFmt", _engine.Codec, kbps) : _engine.Codec;
        }
        else if (state is PlayerState.Stopped or PlayerState.Error)
        {
            QualityText = "";
        }
        RefreshFavoriteState();
        OnPropertyChanged(nameof(PlayerState));
        PlayerStateChanged?.Invoke();
    }

    public event Action? PlayerStateChanged;
    public event Action<string>? TrackChanged;

    private void OnMetadataChanged()
    {
        var display = _engine.Metadata.Display;
        TrackLine = display;
        if (display.Length > 0 && _engine.Station is { } station)
        {
            Task.Run(() =>
            {
                try { _stations.AddSong(station.Name, display); } catch { }
            });
        }
        TrackChanged?.Invoke(display);
    }

    private void Tick()
    {
        if (_engine.State == PlayerState.Playing)
        {
            BufferPercent = _engine.BufferPercent;
            BufferText = Loc.T("BufferFmt", BufferPercent);
            var kbps = _engine.BitrateKbps;
            QualityText = kbps > 0 ? Loc.T("QualityFmt", _engine.Codec, kbps) : _engine.Codec;
            var elapsed = _engine.Elapsed;
            ElapsedText = $"({(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00})";
        }
        else if (IsBuffering)
        {
            BufferText = Loc.T("BufferFmt", _engine.BufferPercent);
        }
        TickRecording();
    }

    // ================================================================== volume

    private int _volume;
    public int Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (Set(ref _volume, value))
            {
                _engine.Volume = value;
                if (value > 0 && IsMuted) IsMuted = false;
                _settings.Current.Volume = value;
                _settings.Save();
            }
        }
    }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (Set(ref _isMuted, value))
            {
                _engine.Muted = value;
                _settings.Current.Muted = value;
                _settings.Save();
                OnPropertyChanged(nameof(MuteGlyphKey));
            }
        }
    }

    public string MuteGlyphKey => IsMuted || Volume == 0 ? "G.SpeakerMute" : "G.Speaker";

    // ================================================================== recording state

    private string _recordingText = "";
    public string RecordingText
    {
        get => _recordingText;
        set => Set(ref _recordingText, value);
    }

    private bool _isRecordingActive;
    public bool IsRecordingActive
    {
        get => _isRecordingActive;
        set => Set(ref _isRecordingActive, value);
    }

    private bool _playingStationRecording;
    public bool PlayingStationRecording
    {
        get => _playingStationRecording;
        set => Set(ref _playingStationRecording, value);
    }

    private void RefreshRecordingState()
    {
        IsRecordingActive = _recordingManager.ActiveCount > 0;
        var target = _engine.Station ?? SelectedStation;
        PlayingStationRecording = target is not null && _recordingManager.IsRecordingStation(target.Id, target.Url);
    }

    private void TickRecording()
    {
        if (!IsRecordingActive) { if (RecordingText.Length > 0) RecordingText = ""; return; }
        var session = _recordingManager.Sessions.FirstOrDefault(s => s.IsRecording);
        if (session is null) return;
        var duration = DateTime.Now - session.StartedAt;
        var count = _recordingManager.ActiveCount;
        RecordingText = count > 1
            ? $"REC {Formatting.Duration(duration)} (+{count - 1})"
            : $"REC {Formatting.Duration(duration)}";
    }

    // ================================================================== favorites

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set => Set(ref _isFavorite, value);
    }

    private void RefreshFavoriteState()
    {
        var station = _engine.Station ?? SelectedStation;
        if (station is null) { IsFavorite = false; return; }
        Task.Run(() =>
        {
            bool fav;
            try { fav = _stations.IsFavorite(station.Id); } catch { return; }
            Ui(() =>
            {
                if ((_engine.Station ?? SelectedStation)?.Id == station.Id) IsFavorite = fav;
            });
        });
    }

    public void NotifySelectionFavoriteChanged() => RefreshFavoriteState();

    // ================================================================== actions

    public void Play(Station station)
    {
        if (station.Url.Length == 0)
        {
            var full = _stations.Get(station.Id);
            if (full is null) return;
            station = full;
        }
        _engine.Play(station);
        Task.Run(() =>
        {
            try
            {
                _stations.AddHistory(station);
                if (_settings.Current.ReportClicks && station.Uuid is not null)
                    _ = _client.ReportClickAsync(station.Uuid);
            }
            catch { }
        });
        if (_settings.Current.RememberLastStation)
        {
            _settings.Current.LastStationId = station.Id;
            _settings.Save();
        }
        AddSearchHistory();
    }

    public void PlaySelected() { if (SelectedStation is not null) Play(SelectedStation); }

    public void PlayPause()
    {
        switch (_engine.State)
        {
            case PlayerState.Playing:
                _engine.Pause();
                break;
            case PlayerState.Paused:
                _engine.Resume();
                break;
            case PlayerState.Connecting or PlayerState.Reconnecting:
                _engine.Stop();
                break;
            default:
                if (SelectedStation is not null) PlaySelected();
                else if (Stations.Count > 0) Play(Stations[0]);
                break;
        }
    }

    public void Stop() => _engine.Stop();

    public void Step(int direction)
    {
        if (Stations.Count == 0) return;
        var reference = SelectedStation ?? _engine.Station;
        var index = reference is null ? -1 : IndexOfStation(reference.Id);
        index += direction;
        if (index < 0) index = Stations.Count - 1;
        if (index >= Stations.Count) index = 0;
        var station = Stations[index];
        SelectedStation = station;
        Play(station);
    }

    private int IndexOfStation(long id)
    {
        for (var i = 0; i < Stations.Count; i++)
            if (Stations[i].Id == id) return i;
        return -1;
    }

    /// <summary>Toggles recording for the playing (or otherwise the selected) station.</summary>
    public void ToggleRecord()
    {
        var station = _engine.Station ?? SelectedStation;
        if (station is null) return;
        if (_recordingManager.StopStation(station.Id, station.Url)) return;
        if (_recordingManager.IsRecordingStation(station.Id, station.Url)) return;
        var session = _recordingManager.Start(station);
        if (session is null)
            RecordingLimitReached?.Invoke();
    }

    public event Action? RecordingLimitReached;

    public void ToggleMute() => IsMuted = !IsMuted;

    public void ChangeVolume(int delta) => Volume += delta;

    /// <summary>Toggles the favorite state of the playing/selected station; the folder is asked by the view.</summary>
    public void SetFavorite(string folder)
    {
        var station = _engine.Station ?? SelectedStation;
        if (station is null) return;
        Task.Run(() =>
        {
            try { _stations.SetFavorite(station.Id, folder); } catch { }
            Ui(() =>
            {
                IsFavorite = true;
                LoadFilterLists();
                if (View == StationView.Favorites) ReloadStations();
            });
        });
    }

    public void RemoveFavorite()
    {
        var station = _engine.Station ?? SelectedStation;
        if (station is null) return;
        Task.Run(() =>
        {
            try { _stations.RemoveFavorite(station.Id); } catch { }
            Ui(() =>
            {
                IsFavorite = false;
                if (View == StationView.Favorites) ReloadStations();
            });
        });
    }

    // ================================================================== database sync

    public void RefreshDatabase() => StartSync(manual: true);

    public void StartSyncIfDue()
    {
        if (_sync.IsRefreshDue()) StartSync(manual: false);
        else
            DatabaseStatus = "";
    }

    private void StartSync(bool manual)
    {
        if (_sync.IsRunning) return;
        DatabaseBusy = true;
        DatabaseStatus = Loc.T("UpdatingDb");
        _ = _sync.RefreshAsync();
    }

    private void OnSyncCompleted(bool ok, int total, string error)
    {
        DatabaseBusy = false;
        DatabaseStatus = ok ? Loc.T("DbUpdated", total) : Loc.T("DbUpdateFailed", error);
        if (ok)
        {
            LoadFilterLists();
            ReloadStations();
        }
        // The status line clears itself after a while.
        var captured = DatabaseStatus;
        Task.Delay(12000).ContinueWith(_ => Ui(() =>
        {
            if (DatabaseStatus == captured) DatabaseStatus = "";
        }));
    }

    // ================================================================== misc

    private void AddSearchHistory()
    {
        var text = SearchText.Trim();
        if (text.Length < 2) return;
        var history = _settings.Current.SearchHistory;
        history.Remove(text);
        history.Insert(0, text);
        if (history.Count > 10) history.RemoveRange(10, history.Count - 10);
        _settings.Save();
    }

    public void OpenFolder(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch (Exception ex) { Log.Warn("Explorer could not be opened: " + ex.Message); }
    }

    public void Dispose()
    {
        _uiTimer.Stop();
        _queryCts?.Cancel();
    }
}
