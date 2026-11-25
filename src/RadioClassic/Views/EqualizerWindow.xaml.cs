using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RadioClassic.Core;
using RadioClassic.Models;

namespace RadioClassic.Views;

/// <summary>
/// Compact classic equalizer. Every control writes into the shared equalizer settings, which the
/// audio engine applies live through its DSP biquads - nothing here is decorative.
/// </summary>
public partial class EqualizerWindow : Window
{
    private readonly AppServices _services;
    private readonly Slider[] _bands = new Slider[10];
    private bool _loading;

    public EqualizerWindow(AppServices services)
    {
        InitializeComponent();
        ThemeServiceFixer.Apply(this);
        _services = services;

        for (var i = 0; i < 10; i++)
        {
            var slider = new Slider
            {
                Style = (Style)FindResource("VerticalSlider"),
                Minimum = -12,
                Maximum = 12,
                TickFrequency = 1,
                Height = 110,
                HorizontalAlignment = HorizontalAlignment.Center,
                Tag = i
            };
            slider.ValueChanged += OnAnyChange;
            _bands[i] = slider;
            Grid.SetRow(slider, 0);
            Grid.SetColumn(slider, i);
            BandsGrid.Children.Add(slider);

            var freq = EqSettings.BandFrequencies[i];
            var label = new TextBlock
            {
                Text = freq >= 1000 ? $"{freq / 1000}k" : freq.ToString(),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            label.SetResourceReference(TextBlock.StyleProperty, "DimText");
            Grid.SetRow(label, 1);
            Grid.SetColumn(label, i);
            BandsGrid.Children.Add(label);
        }

        LoadFromSettings();
        RefreshPresets();
    }

    private EqSettings Eq => _services.Settings.Current.Equalizer;

    private void LoadFromSettings()
    {
        _loading = true;
        EnabledBox.IsChecked = Eq.Enabled;
        for (var i = 0; i < 10; i++) _bands[i].Value = Eq.Bands[i];
        BassSlider.Value = Eq.Bass;
        TrebleSlider.Value = Eq.Treble;
        PreampSlider.Value = Eq.Preamp;
        BalanceSlider.Value = Eq.Balance * 10;
        _loading = false;
    }

    private void RefreshPresets(string? select = null)
    {
        _loading = true;
        PresetCombo.Items.Clear();
        foreach (var preset in EqPreset.BuiltIn) PresetCombo.Items.Add(preset.Name);
        foreach (var preset in _services.Settings.Current.CustomPresets) PresetCombo.Items.Add(preset.Name);
        PresetCombo.SelectedItem = select ?? (PresetCombo.Items.Contains(Eq.Preset) ? Eq.Preset : "Flat");
        _loading = false;
    }

    private void Apply()
    {
        if (_loading) return;
        Eq.Enabled = EnabledBox.IsChecked == true;
        for (var i = 0; i < 10; i++) Eq.Bands[i] = _bands[i].Value;
        Eq.Bass = BassSlider.Value;
        Eq.Treble = TrebleSlider.Value;
        Eq.Preamp = PreampSlider.Value;
        Eq.Balance = BalanceSlider.Value / 10;
        if (PresetCombo.SelectedItem is string preset) Eq.Preset = preset;
        _services.Engine.Equalizer.Update(Eq);
        _services.Settings.Save();
    }

    private void OnAnyChange(object sender, RoutedPropertyChangedEventArgs<double> e) => Apply();
    private void OnAnyChange(object sender, RoutedEventArgs e) => Apply();

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PresetCombo.SelectedItem is not string name) return;
        var preset = EqPreset.BuiltIn.FirstOrDefault(p => p.Name == name)
                     ?? _services.Settings.Current.CustomPresets.FirstOrDefault(p => p.Name == name);
        if (preset is null) return;
        Eq.Bands = (double[])preset.Bands.Clone();
        Eq.Bass = preset.Bass;
        Eq.Treble = preset.Treble;
        Eq.Preamp = preset.Preamp;
        Eq.Preset = name;
        LoadFromSettings();
        Apply();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _loading = true;
        for (var i = 0; i < 10; i++) _bands[i].Value = 0;
        BassSlider.Value = TrebleSlider.Value = PreampSlider.Value = BalanceSlider.Value = 0;
        _loading = false;
        Apply();
    }

    private void OnSavePreset(object sender, RoutedEventArgs e)
    {
        var dialog = new InputWindow(Loc.T("Eq.SavePreset"), Loc.T("Eq.SavePresetPrompt")) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Value.Length == 0) return;
        var name = dialog.Value;
        var custom = _services.Settings.Current.CustomPresets;
        custom.RemoveAll(p => p.Name == name);
        custom.Add(new EqPreset
        {
            Name = name,
            Bands = (double[])Eq.Bands.Clone(),
            Bass = Eq.Bass,
            Treble = Eq.Treble,
            Preamp = Eq.Preamp
        });
        Eq.Preset = name;
        _services.Settings.Save();
        RefreshPresets(name);
    }

    private void OnDeletePreset(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not string name) return;
        var custom = _services.Settings.Current.CustomPresets;
        if (custom.RemoveAll(p => p.Name == name) == 0) return; // built-in presets stay
        _services.Settings.Save();
        RefreshPresets();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
