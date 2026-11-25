using System.Windows;
using RadioClassic.Core;
using RadioClassic.Data;

namespace RadioClassic.Views;

/// <summary>Small picker for the favorites folder when adding a station to favorites.</summary>
public partial class FavoriteFolderWindow : Window
{
    public string Folder => FolderCombo.Text.Trim();

    public FavoriteFolderWindow(StationRepository stations)
    {
        InitializeComponent();
        ThemeServiceFixer.Apply(this);
        try
        {
            FolderCombo.Items.Add("");
            foreach (var folder in stations.FavoriteFolders()) FolderCombo.Items.Add(folder);
        }
        catch { }
        FolderCombo.Text = "";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

/// <summary>Applies the dark title bar to dialog windows when they open.</summary>
internal static class ThemeServiceFixer
{
    public static void Apply(Window window)
    {
        window.FlowDirection = Loc.CurrentLanguage == "fa" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.SourceInitialized += (_, _) => Services.ThemeService.ApplyTitleBar(window);
    }
}
