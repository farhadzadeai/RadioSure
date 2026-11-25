using System.Windows;
using System.Windows.Controls;
using RadioClassic.Core;
using RadioClassic.Models;

namespace RadioClassic.Views;

/// <summary>Read-only details of one station.</summary>
public partial class StationDetailsWindow : Window
{
    public StationDetailsWindow(Station station, bool isFavorite)
    {
        InitializeComponent();
        ThemeServiceFixer.Apply(this);

        var rows = new (string Key, string Value)[]
        {
            ("St.Name", station.Name),
            ("St.Url", station.Url),
            ("St.Homepage", station.Homepage),
            ("St.Country", station.Country),
            ("St.Language", Text.TitleCaseList(station.Language, 4)),
            ("St.Genre", Text.TitleCaseList(station.Tags, 8)),
            ("St.Codec", station.Codec),
            ("St.Bitrate", station.Bitrate > 0 ? $"{station.Bitrate} kbps" : ""),
            ("St.Votes", station.Votes > 0 ? station.Votes.ToString() : ""),
            ("St.Source", Loc.T(station.IsCustom ? "St.SourceCustom" : "St.SourceRadioBrowser")),
            ("Menu.Favorites", isFavorite ? Loc.T("Yes") : Loc.T("No"))
        };

        var row = 0;
        foreach (var (key, value) in rows)
        {
            if (value.Length == 0) continue;
            FieldsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock
            {
                Text = Loc.T(key),
                Margin = new Thickness(0, 0, 0, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
            var content = new TextBlock
            {
                Text = value,
                Margin = new Thickness(0, 0, 0, 5),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 60
            };
            Grid.SetRow(label, row);
            Grid.SetRow(content, row);
            Grid.SetColumn(content, 1);
            FieldsGrid.Children.Add(label);
            FieldsGrid.Children.Add(content);
            row++;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
