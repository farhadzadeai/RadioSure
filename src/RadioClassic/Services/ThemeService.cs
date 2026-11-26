using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using RadioClassic.Core;

namespace RadioClassic.Services;

/// <summary>
/// Applies the classic skins. A skin is a small XAML resource dictionary with color keys only;
/// the control styling is shared, so themes add no dependencies.
/// </summary>
public static class ThemeService
{
    public static readonly string[] Themes = ["Classic", "Silver", "DarkClassic", "Graphite", "BlueClassic", "Minimal"];

    private static ResourceDictionary? _current;

    public static bool IsDark { get; private set; }

    public static void Apply(string theme, string? accentHex)
    {
        if (!Themes.Contains(theme)) theme = "Classic";
        var dict = new ResourceDictionary
        {
            Source = new Uri($"/RadioSure;component/Themes/{theme}.xaml", UriKind.Relative)
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        // Insert before the shared control styles so DynamicResource lookups see the skin first.
        merged.Insert(Math.Max(0, merged.Count - 1), dict);
        _current = dict;
        IsDark = dict.Contains("IsDarkTheme");

        ApplyAccent(accentHex);
        foreach (Window window in Application.Current.Windows)
            ApplyTitleBar(window);
    }

    /// <summary>
    /// Overrides the accent colors or restores the skin default (null). Direct entries in the
    /// application resources win over the merged skin dictionary, so removing them restores
    /// the skin value.
    /// </summary>
    public static void ApplyAccent(string? accentHex)
    {
        var res = Application.Current.Resources;
        if (accentHex is not null && TryParse(accentHex, out var color))
        {
            res["AccentBrush"] = new SolidColorBrush(color);
            res["AccentTextBrush"] = new SolidColorBrush(color);
        }
        else
        {
            res.Remove("AccentBrush");
            res.Remove("AccentTextBrush");
        }
    }

    private static bool TryParse(string hex, out Color color)
    {
        color = default;
        try
        {
            var obj = System.Windows.Media.ColorConverter.ConvertFromString(hex);
            if (obj is Color c) { color = c; return true; }
        }
        catch { }
        return false;
    }

    /// <summary>Gives the native title bar a dark appearance for dark skins (Windows 10 1809+ / 11).</summary>
    public static void ApplyTitleBar(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            var dark = IsDark ? 1 : 0;
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }
        catch { }
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
