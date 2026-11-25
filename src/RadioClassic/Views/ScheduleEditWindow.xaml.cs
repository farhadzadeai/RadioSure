using System.Windows;
using System.Windows.Controls;
using RadioClassic.Core;
using RadioClassic.Models;

namespace RadioClassic.Views;

/// <summary>Creates or edits a scheduled recording for one station.</summary>
public partial class ScheduleEditWindow : Window
{
    private readonly AppServices _services;
    private readonly RecordingSchedule? _existing;
    private readonly Models.Station? _station;

    public ScheduleEditWindow(AppServices services, Models.Station station)
        : this(services, null, station) { }

    public ScheduleEditWindow(AppServices services, RecordingSchedule existing)
        : this(services, existing, null) { }

    private ScheduleEditWindow(AppServices services, RecordingSchedule? existing, Models.Station? station)
    {
        InitializeComponent();
        ThemeServiceFixer.Apply(this);
        _services = services;
        _existing = existing;
        _station = station;

        RepeatCombo.ItemsSource = new[]
        {
            new KeyValuePair<ScheduleRepeat, string>(ScheduleRepeat.Once, Loc.T("Sch.Once")),
            new KeyValuePair<ScheduleRepeat, string>(ScheduleRepeat.Daily, Loc.T("Sch.Daily")),
            new KeyValuePair<ScheduleRepeat, string>(ScheduleRepeat.Weekly, Loc.T("Sch.Weekly")),
        };
        RepeatCombo.DisplayMemberPath = "Value";
        RepeatCombo.SelectedValuePath = "Key";

        if (existing is not null)
        {
            Title = Loc.T("Sch.EditTitle");
            StationText.Text = existing.StationName;
            DatePicker.SelectedDate = existing.Start.Date;
            TimeBox.Text = existing.Start.ToString("HH:mm");
            DurationBox.Text = existing.DurationMinutes.ToString();
            RepeatCombo.SelectedValue = existing.Repeat;
            SetDay(DaySu, existing.Days, DayOfWeek.Sunday);
            SetDay(DayMo, existing.Days, DayOfWeek.Monday);
            SetDay(DayTu, existing.Days, DayOfWeek.Tuesday);
            SetDay(DayWe, existing.Days, DayOfWeek.Wednesday);
            SetDay(DayTh, existing.Days, DayOfWeek.Thursday);
            SetDay(DayFr, existing.Days, DayOfWeek.Friday);
            SetDay(DaySa, existing.Days, DayOfWeek.Saturday);
            EnabledBox.IsChecked = existing.Enabled;
        }
        else
        {
            StationText.Text = station?.Name ?? "";
            DatePicker.SelectedDate = DateTime.Today;
            RepeatCombo.SelectedValue = ScheduleRepeat.Once;
            DayMo.IsChecked = DayTu.IsChecked = DayWe.IsChecked = DayTh.IsChecked = DayFr.IsChecked = true;
        }
    }

    private static void SetDay(CheckBox box, int mask, DayOfWeek day) =>
        box.IsChecked = (mask & (1 << (int)day)) != 0;

    private void OnRepeatChanged(object sender, SelectionChangedEventArgs e)
    {
        var weekly = RepeatCombo.SelectedValue is ScheduleRepeat.Weekly;
        DaysPanel.IsEnabled = weekly;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(DurationBox.Text.Trim(), out var duration) || duration < 1 || duration > 1440)
        {
            ErrorText.Text = Loc.T("Sch.InvalidDuration");
            return;
        }
        if (!TimeSpan.TryParse(TimeBox.Text.Trim(), out var time) || time >= TimeSpan.FromDays(1))
        {
            ErrorText.Text = "HH:mm";
            return;
        }
        var repeat = RepeatCombo.SelectedValue is ScheduleRepeat r ? r : ScheduleRepeat.Once;
        var days = 0;
        if (repeat == ScheduleRepeat.Weekly)
        {
            if (DaySu.IsChecked == true) days |= 1 << (int)DayOfWeek.Sunday;
            if (DayMo.IsChecked == true) days |= 1 << (int)DayOfWeek.Monday;
            if (DayTu.IsChecked == true) days |= 1 << (int)DayOfWeek.Tuesday;
            if (DayWe.IsChecked == true) days |= 1 << (int)DayOfWeek.Wednesday;
            if (DayTh.IsChecked == true) days |= 1 << (int)DayOfWeek.Thursday;
            if (DayFr.IsChecked == true) days |= 1 << (int)DayOfWeek.Friday;
            if (DaySa.IsChecked == true) days |= 1 << (int)DayOfWeek.Saturday;
        }

        var schedule = _existing ?? new RecordingSchedule();
        if (_station is not null)
        {
            schedule.StationId = _station.Id;
            schedule.StationName = _station.Name;
            schedule.StationUrl = _station.Url;
        }
        schedule.Start = (DatePicker.SelectedDate ?? DateTime.Today).Date + time;
        schedule.DurationMinutes = duration;
        schedule.Repeat = repeat;
        schedule.Days = days;
        schedule.Enabled = EnabledBox.IsChecked == true;

        try
        {
            _services.RecordingStore.SaveSchedule(schedule);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            return;
        }
        DialogResult = true;
        Close();
    }
}
