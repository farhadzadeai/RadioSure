using System.Windows;
using System.Windows.Input;
using RadioClassic.Core;
using RadioClassic.Models;
using RadioClassic.ViewModels;

namespace RadioClassic.Views;

/// <summary>Playback history (stations) or song history, depending on <paramref name="songs"/>.</summary>
public partial class HistoryWindow : Window
{
    private readonly AppServices _services;
    private readonly bool _songs;

    public HistoryWindow(AppServices services, bool songs = false)
    {
        InitializeComponent();
        ThemeServiceFixer.Apply(this);
        _services = services;
        _songs = songs;
        if (songs) Title = Loc.T("Menu.PlayedSongs");
        Reload();
    }

    private void Reload()
    {
        List.ItemsSource = _songs
            ? _services.Stations.Songs(500)
            : _services.Stations.History(500);
    }

    private void OnPlay(object sender, RoutedEventArgs e) => PlaySelected();
    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => PlaySelected();

    private void PlaySelected()
    {
        if (List.SelectedItem is not HistoryEntry entry) return;
        var station = _services.Stations.Get(entry.StationId);
        if (station is null) return;
        if (Owner is MainWindow mw) mw.PlayStationFromDialog(station);
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _services.Stations.ClearHistory();
        Reload();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
