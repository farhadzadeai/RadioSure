using System.Diagnostics;
using System.Reflection;
using System.Windows;
using RadioClassic.Core;

namespace RadioClassic.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        ThemeServiceFixer.Apply(this);
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        VersionText.Text = $"Version {version} — .NET {Environment.Version}";
        CreditsText.Text = Loc.T("Abt.Credits");
    }

    private void OnRbLink(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://www.radio-browser.info") { UseShellExecute = true }); }
        catch { }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
