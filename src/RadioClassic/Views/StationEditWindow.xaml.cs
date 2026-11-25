using System.Windows;
using RadioClassic.Core;
using RadioClassic.Models;
using RadioClassic.Services;

namespace RadioClassic.Views;

/// <summary>Adds a custom station or edits an existing one. Edited stations become custom stations.</summary>
public partial class StationEditWindow : Window
{
    private readonly AppServices _services;
    private readonly Station? _station;

    public StationEditWindow(AppServices services, Station? station)
    {
        InitializeComponent();
        ThemeServiceFixer.Apply(this);
        _services = services;
        _station = station;
        if (station is not null)
        {
            Title = Loc.T("St.EditTitle");
            NameBox.Text = station.Name;
            UrlBox.Text = station.Url;
            HomepageBox.Text = station.Homepage;
            CountryBox.Text = station.Country;
            LanguageBox.Text = station.Language;
            GenreBox.Text = station.Tags;
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var url = UrlBox.Text.Trim();
        if (name.Length == 0)
        {
            ErrorText.Text = Loc.T("St.NameRequired");
            return;
        }
        if (!PlaylistIO.IsValidStreamUrl(url))
        {
            ErrorText.Text = Loc.T("St.UrlInvalid");
            return;
        }

        try
        {
            if (_station is null)
            {
                _services.Stations.AddCustom(new Station
                {
                    Name = name,
                    Url = url,
                    Homepage = HomepageBox.Text.Trim(),
                    Country = CountryBox.Text.Trim(),
                    Language = LanguageBox.Text.Trim(),
                    Tags = GenreBox.Text.Trim()
                });
            }
            else
            {
                _station.Name = name;
                _station.Url = url;
                _station.Homepage = HomepageBox.Text.Trim();
                _station.Country = CountryBox.Text.Trim();
                _station.Language = LanguageBox.Text.Trim();
                _station.Tags = GenreBox.Text.Trim();
                _services.Stations.Update(_station);
            }
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
