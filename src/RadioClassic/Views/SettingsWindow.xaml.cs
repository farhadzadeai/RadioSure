using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using RadioClassic.Audio;
using RadioClassic.Core;
using RadioClassic.Models;
using RadioClassic.Services;

namespace RadioClassic.Views;

/// <summary>
/// Options dialog with sections on the left. Changes are saved immediately and the central
/// SettingsService.Changed event applies them (network, hotkeys, equalizer, autostart).
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppServices _services;
    private AppSettings S => _services.Settings.Current;

    private static readonly (string Key, string LabelKey)[] Sections =
    [
        ("general", "Opt.General"), ("playback", "Opt.Playback"), ("recording", "Opt.Recording"),
        ("appearance", "Opt.Appearance"), ("network", "Opt.Network")
    ];

    public SettingsWindow(AppServices services)
    {
        InitializeComponent();
        ThemeServiceFixer.Apply(this);
        _services = services;
        foreach (var (_, label) in Sections) SectionList.Items.Add(new ListBoxItem { Content = Loc.T(label) });
        SectionList.SelectedIndex = 0;
    }

    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PagePanel.Children.Clear();
        switch (Sections[Math.Max(0, SectionList.SelectedIndex)].Key)
        {
            case "general": BuildGeneral(); break;
            case "playback": BuildPlayback(); break;
            case "recording": BuildRecording(); break;
            case "appearance": BuildAppearance(); break;
            case "network": BuildNetwork(); break;
        }
    }

    // ------------------------------------------------------------------ ui helpers

    private CheckBox AddCheck(string labelKey, bool value, Action<bool> apply)
    {
        var box = new CheckBox { Content = Loc.T(labelKey), IsChecked = value, Margin = new Thickness(0, 0, 0, 7) };
        box.Click += (_, _) => { apply(box.IsChecked == true); Save(); };
        PagePanel.Children.Add(box);
        return box;
    }

    private TextBlock AddLabel(string text)
    {
        var label = new TextBlock { Text = text, Margin = new Thickness(0, 4, 0, 3) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
        PagePanel.Children.Add(label);
        return label;
    }

    private (TextBox Box, Action ApplyText) AddTextRow(string labelKey, string value, Action<string> apply, int width = 0)
    {
        AddLabel(Loc.T(labelKey));
        var box = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 7) };
        if (width > 0) { box.Width = width; box.HorizontalAlignment = HorizontalAlignment.Left; }
        box.LostFocus += (_, _) => { apply(box.Text.Trim()); Save(); };
        PagePanel.Children.Add(box);
        return (box, () => apply(box.Text.Trim()));
    }

    private ComboBox AddComboRow(string labelKey, IEnumerable<object> items, object? selected, Action<object?> apply)
    {
        if (labelKey.Length > 0) AddLabel(Loc.T(labelKey));
        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 170 };
        foreach (var item in items) combo.Items.Add(item);
        combo.SelectedItem = selected;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not null) { apply(combo.SelectedItem); Save(); }
        };
        PagePanel.Children.Add(combo);
        return combo;
    }

    private (TextBox, TextBlock) AddNumberRow(string labelKey, int value, int min, int max, string suffix, Action<int> apply)
    {
        AddLabel(Loc.T(labelKey));
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
        var box = new TextBox { Text = value.ToString(), Width = 56, HorizontalContentAlignment = HorizontalAlignment.Center };
        box.LostFocus += (_, _) =>
        {
            if (int.TryParse(box.Text, out var n) && n >= min && n <= max) { apply(n); Save(); }
            box.Text = Math.Clamp(int.TryParse(box.Text, out var v) ? v : value, min, max).ToString();
        };
        panel.Children.Add(box);
        if (suffix.Length > 0)
        {
            var unit = new TextBlock { Text = " " + suffix, VerticalAlignment = VerticalAlignment.Center };
            unit.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
            panel.Children.Add(unit);
        }
        PagePanel.Children.Add(panel);
        return (box, null!);
    }

    private void Save() => _services.Settings.SaveAndNotify();

    // ------------------------------------------------------------------ pages

    private void BuildGeneral()
    {
        AddCheck("Gen.StartWithWindows", S.StartWithWindows, v => S.StartWithWindows = v);
        AddCheck("Gen.StartMinimized", S.StartMinimized, v => S.StartMinimized = v);
        AddCheck("Gen.MinimizeToTray", S.MinimizeToTray, v => S.MinimizeToTray = v);
        AddCheck("Gen.CloseToTray", S.CloseToTray, v => S.CloseToTray = v);
        AddCheck("Gen.RememberStation", S.RememberLastStation, v => S.RememberLastStation = v);
        AddCheck("Gen.ResumeOnStart", S.ResumeOnStart, v => S.ResumeOnStart = v);
        AddCheck("Gen.Notifications", S.ShowNotifications, v => S.ShowNotifications = v);
        AddComboRow("Gen.Language",
            Loc.Languages.Select(l => new KeyValuePair<string, string>(l.Code, l.Name)).Cast<object>().ToList(),
            Loc.Languages.Select(l => new KeyValuePair<string, string>(l.Code, l.Name)).FirstOrDefault(x => x.Key == S.Language),
            sel =>
            {
                if (sel is KeyValuePair<string, string> pair && pair.Key != S.Language)
                {
                    S.Language = pair.Key;
                    Loc.SetLanguage(pair.Key);
                    ThemeServiceFixer.Apply(this);
                    SectionList.Items.Clear();
                    foreach (var (_, label) in Sections) SectionList.Items.Add(new ListBoxItem { Content = Loc.T(label) });
                    SectionList.SelectedIndex = 0;
                }
            }).DisplayMemberPath = "Value";
        // make the combo show names: items are KeyValuePair - set display path before filling; simpler: rebuild
    }

    private void BuildPlayback()
    {
        AddNumberRow("Pb.Buffer", S.BufferMs / 1000, 1, 30, "s", v => S.BufferMs = v * 1000);
        AddCheck("Pb.AutoReconnect", S.AutoReconnect, v => S.AutoReconnect = v);
        AddNumberRow("Pb.RetryAttempts", S.RetryAttempts, 0, 50, "", v => S.RetryAttempts = v);

        AddLabel(Loc.T("Pb.Output"));
        var devices = new List<object> { new KeyValuePair<int, string>(-1, Loc.T("Pb.OutputDefault")) };
        try
        {
            _services.Engine.Initialize();
            foreach (var d in AudioEngine.GetOutputDevices())
                devices.Add(new KeyValuePair<int, string>(d.Index, d.Name));
        }
        catch { }
        var combo = AddComboRow("", devices, devices.OfType<KeyValuePair<int, string>>()
                .Cast<object>().FirstOrDefault(x => ((KeyValuePair<int, string>)x).Key == S.OutputDevice) ?? devices[0],
            sel =>
            {
                if (sel is KeyValuePair<int, string> pair && pair.Key != S.OutputDevice)
                {
                    S.OutputDevice = pair.Key;
                    _services.Engine.ChangeOutputDevice();
                }
            });
        combo.DisplayMemberPath = "Value";

        AddCheck("Pb.Hotkeys", S.GlobalHotkeys, v => S.GlobalHotkeys = v);
        AddCheck("Pb.MediaKeys", S.MediaKeys, v => S.MediaKeys = v);
    }

    private void BuildRecording()
    {
        AddLabel(Loc.T("Rc.Folder"));
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
        var dirBox = new TextBox { Text = S.RecordingDir, Width = 280 };
        dirBox.LostFocus += (_, _) => { S.RecordingDir = dirBox.Text.Trim(); Save(); };
        var browse = new Button { Content = "...", MinWidth = 28, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(4, 1, 4, 1) };
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Rc.Folder") };
            if (dlg.ShowDialog(this) == true) { dirBox.Text = dlg.FolderName; S.RecordingDir = dlg.FolderName; Save(); }
        };
        var open = new Button { Content = Loc.T("Rc.OpenFolder"), MinWidth = 90, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(6, 1, 6, 1) };
        open.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(S.RecordingDir);
                Process.Start("explorer.exe", $"\"{S.RecordingDir}\"");
            }
            catch { }
        };
        panel.Children.Add(dirBox);
        panel.Children.Add(browse);
        panel.Children.Add(open);
        PagePanel.Children.Add(panel);

        var formats = new object[]
        {
            new KeyValuePair<RecordingFormat, string>(RecordingFormat.Original, Loc.T("Rc.FormatOriginal")),
            new KeyValuePair<RecordingFormat, string>(RecordingFormat.Mp3, "MP3"),
            new KeyValuePair<RecordingFormat, string>(RecordingFormat.Wav, "WAV"),
        };
        var formatCombo = AddComboRow("Rc.Format", formats,
            formats.OfType<KeyValuePair<RecordingFormat, string>>().Cast<object>()
                .First(x => ((KeyValuePair<RecordingFormat, string>)x).Key == S.RecordingFormat),
            sel => { if (sel is KeyValuePair<RecordingFormat, string> pair) S.RecordingFormat = pair.Key; });
        formatCombo.DisplayMemberPath = "Value";

        AddNumberRow("Rc.Mp3Quality", S.Mp3Bitrate, 64, 320, "kbps", v => S.Mp3Bitrate = v);
        AddCheck("Rc.Split", S.SplitTracks, v => S.SplitTracks = v);
        AddTextRow("Rc.TrackTemplate", S.TrackFileTemplate, v => S.TrackFileTemplate = v);
        AddTextRow("Rc.ContinuousTemplate", S.ContinuousFileTemplate, v => S.ContinuousFileTemplate = v);
        var hint = new TextBlock { Text = Loc.T("Rc.TemplateHint"), FontSize = 10, Margin = new Thickness(0, 0, 0, 7) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
        PagePanel.Children.Add(hint);
        AddCheck("Rc.StationFolders", S.StationSubfolders, v => S.StationSubfolders = v);
        AddNumberRow("Rc.MaxConcurrent", S.MaxConcurrentRecordings, 1, 10, "", v => S.MaxConcurrentRecordings = v);
        AddCheck("Rc.NotifyScheduled", S.NotifyScheduledRecordings, v => S.NotifyScheduledRecordings = v);
    }

    private void BuildAppearance()
    {
        var themeCombo = AddComboRow("Ap.Theme", ThemeService.Themes.Cast<object>(), S.Theme,
            sel =>
            {
                if (sel is string theme)
                {
                    S.Theme = theme;
                    ThemeService.Apply(theme, S.AccentColor);
                    ThemeServiceFixer.Apply(this);
                }
            });

        AddLabel(Loc.T("Ap.Accent"));
        var accentPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var swatches = new (string Name, string Hex)[]
        {
            ("", ""), ("Blue", "#2F6DB3"), ("Green", "#3E8F4E"), ("Orange", "#D8963C"),
            ("Red", "#C43E3E"), ("Purple", "#7A5FC4"), ("Teal", "#2E9E97"), ("Gray", "#6E7680")
        };
        foreach (var (name, hex) in swatches)
        {
            var item = new RadioButton
            {
                GroupName = "accent",
                Width = 22,
                Height = 18,
                Margin = new Thickness(0, 0, 4, 3),
                IsChecked = (S.AccentColor ?? "") == hex,
                ToolTip = name.Length == 0 ? Loc.T("Ap.AccentDefault") : name
            };
            if (hex.Length > 0)
                item.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
            var captured = hex;
            item.Click += (_, _) =>
            {
                S.AccentColor = captured.Length == 0 ? null : captured;
                ThemeService.ApplyAccent(S.AccentColor);
                Save();
            };
            accentPanel.Children.Add(item);
        }
        PagePanel.Children.Add(accentPanel);

        AddCheck("Ap.Spectrum", S.ShowSpectrum, v => S.ShowSpectrum = v);
        AddCheck("Ap.CompactOnTop", S.CompactAlwaysOnTop, v => S.CompactAlwaysOnTop = v);
        AddCheck("Ap.ShowFilters", S.ShowFilters, v => S.ShowFilters = v);
    }

    private void BuildNetwork()
    {
        AddNumberRow("Net.Timeout", S.TimeoutSeconds, 3, 60, "s", v => S.TimeoutSeconds = v);
        var proxies = new object[]
        {
            new KeyValuePair<ProxyMode, string>(ProxyMode.System, Loc.T("Net.ProxySystem")),
            new KeyValuePair<ProxyMode, string>(ProxyMode.None, Loc.T("Net.ProxyNone")),
            new KeyValuePair<ProxyMode, string>(ProxyMode.Manual, Loc.T("Net.ProxyManual")),
        };
        var proxyCombo = AddComboRow("Net.Proxy", proxies,
            proxies.OfType<KeyValuePair<ProxyMode, string>>().Cast<object>()
                .First(x => ((KeyValuePair<ProxyMode, string>)x).Key == S.ProxyMode),
            sel => { if (sel is KeyValuePair<ProxyMode, string> pair) S.ProxyMode = pair.Key; });
        proxyCombo.DisplayMemberPath = "Value";

        AddTextRow("Net.ProxyHost", S.ProxyHost, v => S.ProxyHost = v, 170);
        AddNumberRow("Net.ProxyPort", S.ProxyPort, 1, 65535, "", v => S.ProxyPort = v);
        AddNumberRow("Net.Refresh", S.DatabaseRefreshDays, 1, 90, "", v => S.DatabaseRefreshDays = v);
        AddCheck("Net.ReportClicks", S.ReportClicks, v => S.ReportClicks = v);
    }

    // ------------------------------------------------------------------ bottom buttons

    private void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, Loc.T("Opt.RestoreConfirm"), "RadioClassic",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _services.Settings.RestoreDefaults();
        ThemeService.Apply(S.Theme, S.AccentColor);
        OnSectionChanged(this, default!);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
