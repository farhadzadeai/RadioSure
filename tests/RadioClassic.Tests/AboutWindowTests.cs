using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RadioClassic.Core;
using RadioClassic.Views;

namespace RadioClassic.Tests;

/// <summary>
/// Regression tests for the About window: all localization keys must resolve in both
/// languages (the original bug showed the raw key "Abt.Credits"), and the window must render.
/// </summary>
public class AboutWindowTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null) _ = new Application();
    }

    [Fact]
    public void AboutKeys_ResolveInBothLanguages()
    {
        Exception? error = null;
        string report = "";
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApp();
                var keys = new[] { "Abt.Title", "Abt.Tagline", "Abt.Contact", "Abt.Donation", "Abt.DonationHint", "Abt.Copy", "Abt.Copied", "Abt.StationData" };
                foreach (var lang in new[] { "en", "fa" })
                {
                    Loc.SetLanguage(lang);
                    foreach (var key in keys)
                    {
                        var val = Loc.T(key);
                        if (val == key) report += $"MISSING {lang}:{key}; ";
                    }
                }
                Loc.SetLanguage("en");
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
        Assert.True(report == "", report);
    }

    [Fact]
    public void AboutWindow_Renders()
    {
        Exception? error = null;
        string? pngPath = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureApp();
                try
                {
                    var merged = Application.Current.Resources.MergedDictionaries;
                    merged.Add(new ResourceDictionary { Source = new Uri("/RadioClassic;component/Themes/Classic.xaml", UriKind.Relative) });
                    merged.Add(new ResourceDictionary { Source = new Uri("/RadioClassic;component/Themes/Controls.xaml", UriKind.Relative) });
                    Loc.SetLanguage("en");
                }
                catch { }
                var window = new AboutWindow();
                window.Show();
                window.UpdateLayout();
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(window.Width, double.PositiveInfinity));
                content.Arrange(new Rect(content.DesiredSize));
                content.UpdateLayout();
                int w = (int)content.ActualWidth + 32;
                int h = (int)content.ActualHeight + 32;
                if (w < 10 || h < 10) { w = 420; h = 380; }
                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(content);
                pngPath = Path.Combine(Path.GetTempPath(), "about-render.png");
                using var fs = File.Create(pngPath);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                enc.Save(fs);
                window.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
        Assert.NotNull(pngPath);
        Assert.True(File.Exists(pngPath));
    }
}
