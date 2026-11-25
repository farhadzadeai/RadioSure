using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using RadioClassic.Audio;
using RadioClassic.Core;
using RadioClassic.Models;
using RadioClassic.Services;
using RadioClassic.ViewModels;

namespace RadioClassic.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppServices _services;
    private readonly DispatcherTimer _spectrumTimer;
    private readonly float[] _fft = new float[128];
    private double _fullHeight = 440;
    private bool _compact;
    private bool _reallyClose;

    public MainWindow(MainViewModel vm, AppServices services)
    {
        InitializeComponent();
        _vm = vm;
        _services = services;
        DataContext = vm;

        InitViewCombo();
        InitCodecBitrateCombos();

        _spectrumTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _spectrumTimer.Tick += (_, _) => UpdateSpectrum();

        StationList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnHeaderClick));

        _vm.PlayerStateChanged += RefreshPlayerUi;
        _vm.TrackChanged += OnTrackChanged;
        _vm.RecordingLimitReached += () =>
            MessageBox.Show(this, Loc.T("Rec.LimitReached"), "RadioClassic", MessageBoxButton.OK, MessageBoxImage.Information);
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsFavorite)) RefreshFavoriteUi();
            if (e.PropertyName is nameof(MainViewModel.PlayingStationRecording)) RefreshRecordingUi();
            if (e.PropertyName is nameof(MainViewModel.StationCount)) UpdateEmptyHint();
            if (e.PropertyName is nameof(MainViewModel.FiltersVisible)) UpdateFilterVisibility();
        };
        _services.Recordings.SessionsChanged += () => Dispatcher.BeginInvoke(RefreshRecordingUi);
        // The batch-filled station list changes count asynchronously; keep the empty hint in sync.
        _vm.Stations.CollectionChanged += (_, _) => UpdateEmptyHint();

        WireTray();
        WireHotkeys();

        var s = _services.Settings.Current;
        _filtersVisibleBackup = s.ShowFilters;
        _vm.FiltersVisible = s.ShowFilters;
        UpdateFilterVisibility();
        if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop) && s.WindowLeft > -32000)
        {
            Left = s.WindowLeft;
            Top = s.WindowTop;
        }
        Width = Math.Max(MinWidth, s.WindowWidth);
        Height = Math.Max(MinHeight, s.WindowHeight);
        _fullHeight = Height;
        if (s.CompactMode) Dispatcher.BeginInvoke(() => SetCompact(true));
        RefreshPlayerUi();
        UpdateEmptyHint();
    }

    // ================================================================== setup helpers

    private void InitViewCombo()
    {
        ViewCombo.ItemsSource = new[]
        {
            new KeyValuePair<StationView, string>(StationView.All, Loc.T("View.All")),
            new KeyValuePair<StationView, string>(StationView.Favorites, Loc.T("View.Favorites")),
            new KeyValuePair<StationView, string>(StationView.Recent, Loc.T("View.Recent")),
            new KeyValuePair<StationView, string>(StationView.Custom, Loc.T("View.Custom")),
        };
        ViewCombo.DisplayMemberPath = "Value";
        ViewCombo.SelectedValuePath = "Key";
        ViewCombo.SelectedValue = StationView.All;
    }

    private void InitCodecBitrateCombos()
    {
        GenreCombo.ItemsSource = new[] { "", "pop", "rock", "dance", "electronic", "jazz", "classical", "news", "talk", "sport", "hip hop", "country", "latin", "metal", "oldies", "80s", "90s", "chillout", "ambient", "folk" };
        CodecCombo.ItemsSource = new[] { "", "MP3", "AAC", "AAC+", "OGG", "Opus", "FLAC" };
        CodecCombo.SelectedIndex = 0;
        BitrateCombo.ItemsSource = new[] { "", "64", "128", "192", "256", "320" };
        BitrateCombo.SelectedIndex = 0;
        // The country/language lists are bound collections whose first entry is empty (= no filter).
        CountryCombo.SelectionChanged -= OnFilterChanged;
        LanguageCombo.SelectionChanged -= OnFilterChanged;
        CountryCombo.SelectedIndex = 0;
        LanguageCombo.SelectedIndex = 0;
        CountryCombo.SelectionChanged += OnFilterChanged;
        LanguageCombo.SelectionChanged += OnFilterChanged;
    }

    // ================================================================== spectrum

    private void UpdateSpectrum()
    {
        if (!_services.Settings.Current.ShowSpectrum || _compact)
        {
            Spectrum.Update(null);
            return;
        }
        Spectrum.Update(_vm.Engine.GetSpectrum(_fft) ? _fft : null);
    }

    private void StartSpectrumIfNeeded()
    {
        var running = _vm.PlayerState == PlayerState.Playing && IsVisible && WindowState != WindowState.Minimized
                      && _services.Settings.Current.ShowSpectrum;
        if (running && !_spectrumTimer.IsEnabled) _spectrumTimer.Start();
        else if (!running && _spectrumTimer.IsEnabled)
        {
            _spectrumTimer.Stop();
            Spectrum.Update(null);
        }
    }

    // ================================================================== player UI refresh

    private void RefreshPlayerUi()
    {
        PlayButton.Tag = FindResource(_vm.PlayerState == PlayerState.Playing ? "G.Pause" : "G.Play");
        StartSpectrumIfNeeded();
        RefreshRecordingUi();
        RefreshFavoriteUi();
        UpdateTitle();
    }

    private void RefreshRecordingUi()
    {
        RecordButton.Style = (Style)FindResource(_vm.PlayingStationRecording ? "RecordButtonActive" : "RecordButton");
    }

    private void RefreshFavoriteUi()
    {
        FavoriteButton.Style = (Style)FindResource(_vm.IsFavorite ? "PlayerButtonActive" : "PlayerButton");
    }

    private void UpdateTitle()
    {
        var track = _vm.TrackLine;
        var station = _vm.StationLine;
        Title = track.Length > 0 ? track :
            station.Length > 0 && _vm.PlayerState is PlayerState.Playing or PlayerState.Paused ? station : "RadioClassic";
        var tip = track.Length > 0 && station.Length > 0 ? Loc.T("Tray.NowPlaying", station, track) :
            station.Length > 0 ? station : "RadioClassic";
        _services.Tray.SetTooltip(tip);
    }

    private void OnTrackChanged(string track)
    {
        UpdateTitle();
        if (_services.Settings.Current.ShowNotifications && track.Length > 0 &&
            (WindowState == WindowState.Minimized || !IsVisible) && _vm.PlayerState == PlayerState.Playing)
        {
            _services.Tray.Balloon(_vm.StationLine, track);
        }
    }

    // ================================================================== transport handlers

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _vm.PlayPause();
    private void OnStopClick(object sender, RoutedEventArgs e) => _vm.Stop();
    private void OnPreviousClick(object sender, RoutedEventArgs e) => _vm.Step(-1);
    private void OnNextClick(object sender, RoutedEventArgs e) => _vm.Step(1);

    private void OnRecordClick(object sender, RoutedEventArgs e) => _vm.ToggleRecord();

    private void OnMuteClick(object sender, RoutedEventArgs e)
    {
        _vm.ToggleMute();
        MuteButton.Tag = FindResource(_vm.MuteGlyphKey);
    }

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (_vm.IsFavorite) _vm.RemoveFavorite();
        else AskFavoriteFolder();
    }

    private void AskFavoriteFolder()
    {
        var station = _vm.PlayingStation ?? _vm.SelectedStation;
        if (station is null) return;
        var dialog = new FavoriteFolderWindow(_services.Stations) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _vm.SetFavorite(dialog.Folder);
            RefreshFavoriteUi();
        }
    }

    // ================================================================== list interaction

    private void OnStationDoubleClick(object sender, MouseButtonEventArgs e) => _vm.PlaySelected();

    private void OnStationListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { _vm.PlaySelected(); e.Handled = true; }
    }

    private void OnStationSelectionChanged(object sender, SelectionChangedEventArgs e) => _vm.NotifySelectionFavoriteChanged();

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _vm.ReloadStations();
            e.Handled = true;
        }
    }

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header) return;
        var index = ((GridView)StationList.View).Columns.IndexOf(header.Column);
        _vm.SortBy(index switch
        {
            1 => StationSort.Country,
            2 => StationSort.Genre,
            3 => StationSort.Language,
            5 => StationSort.Bitrate,
            _ => StationSort.Name
        });
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The title column takes the remaining width, like the classic list.
        var available = e.NewSize.Width - 330;
        ColTitle.Width = Math.Max(120, available);
        if (!_compact && WindowState == WindowState.Normal) _fullHeight = Height;
    }

    // ================================================================== filters

    private bool _filtersVisibleBackup;

    private void OnFilterToggleClick(object sender, RoutedEventArgs e)
    {
        _vm.FiltersVisible = !_vm.FiltersVisible;
        _services.Settings.Current.ShowFilters = _vm.FiltersVisible;
        _services.Settings.Save();
    }

    private void UpdateFilterVisibility() =>
        FilterPanel.Visibility = _vm.FiltersVisible ? Visibility.Visible : Visibility.Collapsed;

    private void OnViewComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewCombo.SelectedValue is StationView view)
        {
            _vm.View = view;
            FolderCombo.Visibility = view == StationView.Favorites ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var combo = (ComboBox)sender;
        var value = combo.SelectedItem as string ?? "";
        switch (combo.Tag as string)
        {
            case "country":
                _vm.FilterCountry = combo.SelectedIndex <= 0 ? "" : value;
                break;
            case "language":
                _vm.FilterLanguage = combo.SelectedIndex <= 0 ? "" : value;
                break;
            case "genre":
                if (combo.SelectedItem is string g) _vm.FilterGenre = g;
                break;
            case "codec":
                _vm.FilterCodec = value;
                break;
            case "bitrate":
                _vm.FilterMinBitrate = int.TryParse(value, out var br) ? br : 0;
                break;
        }
    }

    private void OnFolderChanged(object sender, SelectionChangedEventArgs e) =>
        _vm.FavoriteFolder = FolderCombo.SelectedItem as string ?? "";

    // ================================================================== compact mode

    private void OnCompactClick(object sender, RoutedEventArgs e) => SetCompact(!_compact);

    public void SetCompact(bool compact)
    {
        _compact = compact;
        var s = _services.Settings.Current;
        s.CompactMode = compact;
        _services.Settings.Save();

        SearchRow.Height = compact ? new GridLength(0) : GridLength.Auto;
        CountRow.Height = compact ? new GridLength(0) : GridLength.Auto;
        FilterRow.Height = compact ? new GridLength(0) : GridLength.Auto;
        ListRow.Height = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ListRow.MinHeight = compact ? 0 : 80;
        CompactButton.Tag = FindResource(compact ? "G.Expand" : "G.Compact");
        CompactButton.ToolTip = compact ? Loc.T("Tip.Expand") : Loc.T("Tip.Compact");
        Topmost = compact && s.CompactAlwaysOnTop;

        if (compact)
        {
            if (WindowState == WindowState.Normal) _fullHeight = Height;
            MinHeight = 120;
            SizeToContent = SizeToContent.Height;
        }
        else
        {
            SizeToContent = SizeToContent.Manual;
            MinHeight = 300;
            Height = Math.Max(MinHeight, _fullHeight);
        }
        StartSpectrumIfNeeded();
    }

    // ================================================================== context menu

    private void OnStationContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var station = _vm.SelectedStation;
        if (station is null) { StationContextMenu.IsOpen = false; return; }
        var recording = _services.Recordings.IsRecordingStation(station.Id, station.Url);
        CtxRecordItem.Header = Loc.T(recording ? "Ctx.StopRecording" : "Ctx.Record");
        var favorite = _services.Stations.IsFavorite(station.Id);
        CtxFavoriteItem.Header = Loc.T(favorite ? "Ctx.RemoveFromFavorites" : "Ctx.AddToFavorites");
        var url = station.Url.Length > 0 ? station.Url : _services.Stations.Get(station.Id)?.Url ?? "";
        CtxHomepageItem.IsEnabled = true;
        CtxDeleteItem.IsEnabled = station.IsCustom;
    }

    private Station? FullSelected()
    {
        var s = _vm.SelectedStation;
        if (s is null) return null;
        return s.Url.Length > 0 ? s : _services.Stations.Get(s.Id);
    }

    private void OnContextPlay(object sender, RoutedEventArgs e) => _vm.PlaySelected();

    private void OnContextRecord(object sender, RoutedEventArgs e)
    {
        var station = FullSelected();
        if (station is null) return;
        if (!_services.Recordings.StopStation(station.Id, station.Url) &&
            !_services.Recordings.IsRecordingStation(station.Id, station.Url))
            _services.Recordings.Start(station);
    }

    private void OnContextSchedule(object sender, RoutedEventArgs e)
    {
        var station = FullSelected();
        if (station is null) return;
        var window = new ScheduleEditWindow(_services, station) { Owner = this };
        window.ShowDialog();
    }

    private void OnContextFavorite(object sender, RoutedEventArgs e)
    {
        var station = _vm.SelectedStation;
        if (station is null) return;
        if (_services.Stations.IsFavorite(station.Id))
        {
            _vm.SelectedStation = station;
            _vm.RemoveFavorite();
        }
        else
        {
            AskFavoriteFolder();
        }
    }

    private void OnContextDetails(object sender, RoutedEventArgs e)
    {
        var station = FullSelected();
        if (station is null) return;
        new StationDetailsWindow(station, _services.Stations.IsFavorite(station.Id)) { Owner = this }.ShowDialog();
    }

    private void OnContextEdit(object sender, RoutedEventArgs e)
    {
        var station = FullSelected();
        if (station is null) return;
        var window = new StationEditWindow(_services, station) { Owner = this };
        if (window.ShowDialog() == true) _vm.ReloadStations();
    }

    private void OnContextDelete(object sender, RoutedEventArgs e)
    {
        var station = _vm.SelectedStation;
        if (station is null || !station.IsCustom) return;
        if (MessageBox.Show(this, Loc.T("St.DeleteConfirm", station.Name), "RadioClassic",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _services.Stations.Delete(station.Id);
        _vm.ReloadStations();
    }

    private void OnContextCopyUrl(object sender, RoutedEventArgs e)
    {
        var station = FullSelected();
        if (station is null) return;
        try { Clipboard.SetText(station.Url); } catch { }
    }

    private void OnContextHomepage(object sender, RoutedEventArgs e)
    {
        var station = FullSelected();
        if (station is null || station.Homepage.Length == 0) return;
        try { Process.Start(new ProcessStartInfo(station.Homepage) { UseShellExecute = true }); } catch { }
    }

    // ================================================================== main menu

    private void OnEqualizerClick(object sender, RoutedEventArgs e)
    {
        var window = Application.Current.Windows.OfType<EqualizerWindow>().FirstOrDefault();
        if (window is null)
        {
            window = new EqualizerWindow(_services) { Owner = this };
            window.Show();
        }
        window.Activate();
    }

    private void OnMenuClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        BuildMainMenu(menu);
        menu.PlacementTarget = MenuButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void BuildMainMenu(ContextMenu menu)
    {
        var playing = _vm.PlayerState == PlayerState.Playing;
        var paused = _vm.PlayerState == PlayerState.Paused;
        AddItem(menu, playing ? "Menu.Pause" : "Menu.Play", () => _vm.PlayPause());
        AddItem(menu, "Menu.Stop", () => _vm.Stop());
        AddItem(menu, "Menu.Mute", () => { _vm.ToggleMute(); MuteButton.Tag = FindResource(_vm.MuteGlyphKey); });
        var recording = _vm.PlayingStationRecording;
        AddItem(menu, recording ? "Menu.StopRecording" : "Menu.Record", () => _vm.ToggleRecord());
        menu.Items.Add(new Separator());
        AddItem(menu, "Menu.Details", () =>
        {
            var station = _vm.PlayingStation ?? FullSelected();
            if (station is not null)
                new StationDetailsWindow(station, _services.Stations.IsFavorite(station.Id)) { Owner = this }.ShowDialog();
        });
        AddItem(menu, "Menu.AddStation", () =>
        {
            var window = new StationEditWindow(_services, null) { Owner = this };
            if (window.ShowDialog() == true) _vm.ReloadStations();
        });
        AddItem(menu, "Menu.History", () => OpenSingleton<HistoryWindow>(() => new HistoryWindow(_services) { Owner = this }));
        AddItem(menu, "Menu.PlayedSongs", () => OpenSingleton<HistoryWindow>(() => new HistoryWindow(_services, songs: true) { Owner = this }));
        menu.Items.Add(new Separator());
        AddItem(menu, "Menu.Equalizer", () => OnEqualizerClick(this, new RoutedEventArgs()));
        AddItem(menu, "Menu.Recordings", () => OpenSingleton<RecordingsWindow>(() => new RecordingsWindow(_services, _vm) { Owner = this }));
        AddItem(menu, "Menu.CompactMode", () => SetCompact(!_compact));
        menu.Items.Add(new Separator());
        AddItem(menu, "Menu.ImportPlaylist", ImportPlaylist);
        AddItem(menu, "Menu.ExportFavorites", ExportFavorites);

        var language = new MenuItem { Header = Loc.T("Menu.Language") };
        foreach (var (code, name) in Loc.Languages)
        {
            var item = new MenuItem { Header = name, IsChecked = Loc.CurrentLanguage == code, IsCheckable = true };
            var captured = code;
            item.Click += (_, _) => ChangeLanguage(captured);
            language.Items.Add(item);
        }
        menu.Items.Add(language);

        AddItem(menu, "Menu.Options", () => OpenSingleton<SettingsWindow>(() => new SettingsWindow(_services) { Owner = this }));
        AddItem(menu, "Menu.About", () => new AboutWindow { Owner = this }.ShowDialog());
        menu.Items.Add(new Separator());
        AddItem(menu, "Menu.Exit", ExitApplication);
    }

    private static void AddItem(ItemsControl menu, string key, Action action)
    {
        var item = new MenuItem { Header = Loc.T(key) };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void OpenSingleton<T>(Func<T> create) where T : Window
    {
        var window = Application.Current.Windows.OfType<T>().FirstOrDefault();
        if (window is null)
        {
            window = create();
            window.Show();
        }
        window.Activate();
    }

    private void ChangeLanguage(string code)
    {
        _services.Settings.Current.Language = code;
        _services.Settings.Save();
        Loc.SetLanguage(code);
        ApplyFlowDirection();
        InitViewCombo();
    }

    public void ApplyFlowDirection()
    {
        FlowDirection = Loc.CurrentLanguage == "fa" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private void ImportPlaylist()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Loc.T("Io.Filter"),
            Title = Loc.T("Menu.ImportPlaylist")
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var stations = PlaylistIO.Import(dialog.FileName);
            var added = 0;
            foreach (var station in stations)
            {
                if (_services.Stations.FindByUrl(station.Url) is not null) continue;
                station.IsCustom = true;
                _services.Stations.AddCustom(station);
                added++;
            }
            _vm.ReloadStations();
            _vm.LoadFilterLists();
            MessageBox.Show(this, Loc.T("Io.Imported", added), "RadioClassic", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("Io.ImportFailed", ex.Message), "RadioClassic", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportFavorites()
    {
        var favorites = _services.Stations.Favorites();
        if (favorites.Count == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = Loc.T("Io.ExportFilter"),
            FileName = "favorites.m3u",
            Title = Loc.T("Menu.ExportFavorites")
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            PlaylistIO.Export(dialog.FileName, favorites);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("Io.ExportFailed", ex.Message), "RadioClassic", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ================================================================== empty list hint

    private void UpdateEmptyHint()
    {
        var empty = _vm.Stations.Count == 0 && !_vm.DatabaseBusy;
        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
            EmptyHint.Text = Loc.T("NoStationsYet") + "\n" + Loc.T("DbUpdateHint");
    }

    private void OnRefreshDatabaseClick(object sender, RoutedEventArgs e)
    {
        _vm.RefreshDatabase();
        UpdateEmptyHint();
    }

    // ================================================================== tray + hotkeys

    private void WireTray()
    {
        var tray = _services.Tray;
        tray.IsPlaying = () => _vm.PlayerState is PlayerState.Playing or PlayerState.Paused;
        tray.IsRecording = () => _vm.PlayingStationRecording;
        tray.IsMuted = () => _vm.IsMuted;
        tray.Favorites = () => _services.Stations.Favorites().Select(f => (f.Id, f.Name)).ToList();
        tray.ShowWindowRequested += () => Dispatcher.BeginInvoke(RestoreFromTray);
        tray.ExitRequested += () => Dispatcher.BeginInvoke(ExitApplication);
        tray.PlayPauseRequested += () => Dispatcher.BeginInvoke(() => _vm.PlayPause());
        tray.StopRequested += () => Dispatcher.BeginInvoke(() => _vm.Stop());
        tray.NextRequested += () => Dispatcher.BeginInvoke(() => _vm.Step(1));
        tray.PreviousRequested += () => Dispatcher.BeginInvoke(() => _vm.Step(-1));
        tray.RecordRequested += () => Dispatcher.BeginInvoke(() => _vm.ToggleRecord());
        tray.MuteRequested += () => Dispatcher.BeginInvoke(() => _vm.ToggleMute());
        tray.VolumeDelta += d => Dispatcher.BeginInvoke(() => _vm.ChangeVolume(d));
        tray.FavoriteChosen += id => Dispatcher.BeginInvoke(() =>
        {
            var station = _services.Stations.Get(id);
            if (station is not null) _vm.Play(station);
        });
    }

    private void WireHotkeys()
    {
        var hotkeys = _services.Hotkeys;
        hotkeys.PlayPauseRequested += () => Dispatcher.BeginInvoke(() => _vm.PlayPause());
        hotkeys.StopRequested += () => Dispatcher.BeginInvoke(() => _vm.Stop());
        hotkeys.NextRequested += () => Dispatcher.BeginInvoke(() => _vm.Step(1));
        hotkeys.PreviousRequested += () => Dispatcher.BeginInvoke(() => _vm.Step(-1));
        hotkeys.RecordRequested += () => Dispatcher.BeginInvoke(() => _vm.ToggleRecord());
        hotkeys.VolumeDelta += d => Dispatcher.BeginInvoke(() => _vm.ChangeVolume(d));
    }

    /// <summary>Lets dialog windows (history, favorites) start playback of a station.</summary>
    public void PlayStationFromDialog(Models.Station station) => _vm.Play(station);
    public void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = _compact && _services.Settings.Current.CompactAlwaysOnTop;
        Focus();
    }

    // ================================================================== lifetime

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeService.ApplyTitleBar(this);
        if (_services.Settings.Current.StartMinimized || Environment.CommandLine.Contains("--minimized"))
        {
            // Window.Show() has already happened; hide again right away.
            HideToTray();
        }
    }

    private void OnStateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            if (_services.Settings.Current.MinimizeToTray) HideToTray();
            StartSpectrumIfNeeded();
        }
        else
        {
            StartSpectrumIfNeeded();
        }
    }

    private void HideToTray()
    {
        Hide();
        StartSpectrumIfNeeded();
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (!_reallyClose && _services.Settings.Current.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        var s = _services.Settings.Current;
        if (WindowState == WindowState.Normal && !_compact)
        {
            s.WindowLeft = Left;
            s.WindowTop = Top;
            s.WindowWidth = Width;
            s.WindowHeight = Height;
        }
        _services.Settings.Save();
        if (!_reallyClose)
        {
            // Closing the main window exits the application unless close-to-tray is on.
            ExitApplication();
        }
    }

    public void ExitApplication()
    {
        _reallyClose = true;
        ((App)Application.Current).ShutdownGracefully();
    }
}
