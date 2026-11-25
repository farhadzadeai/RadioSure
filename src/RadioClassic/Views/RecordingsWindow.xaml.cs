using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using RadioClassic.Core;
using RadioClassic.Models;
using RadioClassic.Recording;
using RadioClassic.ViewModels;

namespace RadioClassic.Views;

/// <summary>The recording manager window: active sessions, schedules and the recording history.</summary>
public partial class RecordingsWindow : Window
{
    private readonly AppServices _services;
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _timer;

    private sealed class ActiveRow
    {
        public required RecordingSession Session { get; init; }
        public string StationName => Session.StationName;
        public string CurrentTrack => Session.CurrentTrack;
        public string DurationDisplay => Formatting.Duration(DateTime.Now - Session.StartedAt);
        public string SizeDisplay => Formatting.Size(Session.TotalBytes);
        public string EffectiveFormat => Session.EffectiveFormat;
        public string StatusDisplay => Session.State switch
        {
            RecordingState.Recording => Loc.T("Rec.StatusRecording"),
            RecordingState.Connecting => Loc.T("Rec.StatusConnecting"),
            RecordingState.Reconnecting => Loc.T("Rec.StatusReconnecting"),
            _ => ""
        };
    }

    private sealed class ScheduleRow
    {
        public required RecordingSchedule Schedule { get; init; }
        public string StationName => Schedule.StationName;
        public string StartDisplay => Schedule.NextStart(DateTime.Now)?.ToString("yyyy-MM-dd HH:mm") ?? Schedule.StartDisplay;
        public string DurationDisplay => Schedule.DurationDisplay;
        public string RepeatDisplay => Schedule.Repeat switch
        {
            ScheduleRepeat.Daily => Loc.T("Sch.Daily"),
            ScheduleRepeat.Weekly => Loc.T("Sch.Weekly"),
            _ => Loc.T("Sch.Once")
        };
        public string EnabledDisplay => Schedule.Enabled ? Loc.T("Yes") : Loc.T("No");
    }

    private readonly ObservableCollection<ActiveRow> _activeRows = [];
    private readonly ObservableCollection<ScheduleRow> _scheduleRows = [];
    private readonly ObservableCollection<RecordingEntryRow> _historyRows = [];

    private sealed class RecordingEntryRow
    {
        public required RecordingEntry Entry { get; init; }
        public string StationName => Entry.StationName;
        public string Title => Entry.Title;
        public string FileName => Entry.FileName;
        public string StartedDisplay => Entry.StartedDisplay;
        public string DurationDisplay => Entry.DurationDisplay;
        public string SizeDisplay => Entry.SizeDisplay;
        public string StatusDisplay => Entry.Status switch
        {
            RecordingStatus.Completed => Loc.T("Rec.StatusCompleted"),
            RecordingStatus.Interrupted => Loc.T("Rec.StatusInterrupted"),
            RecordingStatus.Failed => Loc.T("Rec.StatusFailed"),
            _ => Loc.T("Rec.StatusRecording")
        };
    }

    public RecordingsWindow(AppServices services, MainViewModel vm)
    {
        InitializeComponent();
        ThemeServiceFixer.Apply(this);
        _services = services;
        _vm = vm;
        ActiveList.ItemsSource = _activeRows;
        ScheduleList.ItemsSource = _scheduleRows;
        HistoryList.ItemsSource = _historyRows;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshActive();
        _timer.Start();

        RefreshActive();
        RefreshSchedules();
        RefreshHistory();
        services.Recordings.SessionsChanged += () => Dispatcher.BeginInvoke(RefreshActive);
        Closed += (_, _) => _timer.Stop();
    }

    // ------------------------------------------------------------------ active

    private void RefreshActive()
    {
        var sessions = _services.Recordings.Sessions.Where(s => s.IsRecording).ToList();
        for (var i = _activeRows.Count - 1; i >= 0; i--)
            if (!sessions.Contains(_activeRows[i].Session)) _activeRows.RemoveAt(i);
        foreach (var session in sessions)
            if (_activeRows.All(r => r.Session != session)) _activeRows.Add(new ActiveRow { Session = session });
        // Values refresh through the timer; force a lightweight rebuild of bindings.
        ActiveList.Items.Refresh();
        ActiveEmpty.Visibility = _activeRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnStopSession(object sender, RoutedEventArgs e)
    {
        if (ActiveList.SelectedItem is ActiveRow row) row.Session.Stop();
    }

    private void OnOpenSessionFolder(object sender, RoutedEventArgs e)
    {
        if (ActiveList.SelectedItem is ActiveRow row && row.Session.CurrentFilePath.Length > 0)
            _vm.OpenFolder(row.Session.CurrentFilePath);
        else
            OpenRecordingRoot();
    }

    private void OpenRecordingRoot()
    {
        try
        {
            var dir = _services.Settings.Current.RecordingDir;
            Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", $"\"{dir}\"");
        }
        catch { }
    }

    // ------------------------------------------------------------------ schedules

    private void RefreshSchedules()
    {
        _scheduleRows.Clear();
        try
        {
            foreach (var schedule in _services.RecordingStore.Schedules())
                _scheduleRows.Add(new ScheduleRow { Schedule = schedule });
        }
        catch { }
        ScheduleEmpty.Visibility = _scheduleRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAddSchedule(object sender, RoutedEventArgs e)
    {
        var station = _vm.PlayingStation ?? _vm.SelectedStation;
        if (station is null)
        {
            MessageBox.Show(this, Loc.T("Sch.Station"), "RadioClassic", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (station.Url.Length == 0) station = _services.Stations.Get(station.Id);
        if (station is null) return;
        var window = new ScheduleEditWindow(_services, station) { Owner = this };
        if (window.ShowDialog() == true) RefreshSchedules();
    }

    private void OnEditSchedule(object sender, RoutedEventArgs e)
    {
        if (ScheduleList.SelectedItem is not ScheduleRow row) return;
        var window = new ScheduleEditWindow(_services, row.Schedule) { Owner = this };
        if (window.ShowDialog() == true) RefreshSchedules();
    }

    private void OnDeleteSchedule(object sender, RoutedEventArgs e)
    {
        if (ScheduleList.SelectedItem is not ScheduleRow row) return;
        if (MessageBox.Show(this, Loc.T("Sch.DeleteConfirm"), "RadioClassic",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _services.RecordingStore.DeleteSchedule(row.Schedule.Id);
        RefreshSchedules();
    }

    // ------------------------------------------------------------------ history

    private void RefreshHistory()
    {
        _historyRows.Clear();
        try
        {
            foreach (var entry in _services.RecordingStore.List(SearchBox.Text.Trim()))
                _historyRows.Add(new RecordingEntryRow { Entry = entry });
        }
        catch { }
        HistoryEmpty.Visibility = _historyRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RefreshHistory();

    private void OnHistoryDoubleClick(object sender, MouseButtonEventArgs e) => PlaySelectedFile();

    private void PlaySelectedFile()
    {
        if (HistoryList.SelectedItem is not RecordingEntryRow row) return;
        try
        {
            if (File.Exists(row.Entry.File))
                Process.Start(new ProcessStartInfo(row.Entry.File) { UseShellExecute = true });
        }
        catch { }
    }

    private void OnOpenEntryFolder(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is RecordingEntryRow row) _vm.OpenFolder(row.Entry.File);
        else OpenRecordingRoot();
    }

    private void OnRenameEntry(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not RecordingEntryRow row) return;
        var entry = row.Entry;
        if (!File.Exists(entry.File)) return;
        var dialog = new InputWindow(Loc.T("Rec.Rename"), Loc.T("Rec.RenamePrompt"),
            Path.GetFileNameWithoutExtension(entry.File)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var newName = RecordingFileNamer.Sanitize(dialog.Value, "");
        if (newName.Length == 0) return;
        try
        {
            var target = Path.Combine(Path.GetDirectoryName(entry.File)!, newName + Path.GetExtension(entry.File));
            if (!target.Equals(entry.File, StringComparison.OrdinalIgnoreCase))
                File.Move(entry.File, target);
            entry.File = target;
            _services.RecordingStore.Update(entry);
            RefreshHistory();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "RadioClassic", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDeleteEntry(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not RecordingEntryRow row) return;
        var entry = row.Entry;
        if (MessageBox.Show(this, Loc.T("Rec.DeleteConfirm", entry.FileName), "RadioClassic",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            if (File.Exists(entry.File)) File.Delete(entry.File);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "RadioClassic", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _services.RecordingStore.Delete(entry.Id);
        RefreshHistory();
    }

    private void OnExportList(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = "recordings.csv"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var sb = new StringBuilder("Station;Title;File;Started;Duration;Size;Status\n");
            foreach (var row in _historyRows)
            {
                string Esc(string s) => s.Contains(';') || s.Contains('"') ? '"' + s.Replace("\"", "\"\"") + '"' : s;
                sb.Append(Esc(row.StationName)).Append(';')
                    .Append(Esc(row.Title)).Append(';')
                    .Append(Esc(row.Entry.File)).Append(';')
                    .Append(row.StartedDisplay).Append(';')
                    .Append(row.DurationDisplay).Append(';')
                    .Append(row.SizeDisplay).Append(';')
                    .Append(row.StatusDisplay).Append('\n');
            }
            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "RadioClassic", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
