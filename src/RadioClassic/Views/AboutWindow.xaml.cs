using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace RadioClassic.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        ThemeServiceFixer.Apply(this);
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        VersionText.Text = $"Version {version} — .NET {Environment.Version}";
    }

    private void OnLink(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { }
        e.Handled = true;
    }

    private void OnCopyWallet(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(WalletText.Text);
            CopyWalletBtn.Content = RadioClassic.Core.Loc.T("Abt.Copied");
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = System.TimeSpan.FromSeconds(2)
            };
            timer.Tick += (_, _) =>
            {
                CopyWalletBtn.Content = RadioClassic.Core.Loc.T("Abt.Copy");
                timer.Stop();
            };
            timer.Start();
        }
        catch { }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
