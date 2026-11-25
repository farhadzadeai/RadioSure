using System.Windows;
using System.Windows.Media;

namespace RadioClassic.Controls;

/// <summary>
/// The classic spectrum analyzer display: a row of vertical bars on a dark panel, refreshed
/// by the main window only while audio plays and the window is visible.
/// </summary>
public sealed class SpectrumControl : FrameworkElement
{
    private const int BarCount = 42;
    private readonly float[] _levels = new float[BarCount];
    private readonly float[] _peaks = new float[BarCount];
    private readonly int[] _peakHold = new int[BarCount];
    private bool _active;

    public static readonly DependencyProperty BarBrushProperty =
        DependencyProperty.Register(nameof(BarBrush), typeof(Brush), typeof(SpectrumControl),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PeakBrushProperty =
        DependencyProperty.Register(nameof(PeakBrush), typeof(Brush), typeof(SpectrumControl),
            new FrameworkPropertyMetadata(Brushes.LightBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PanelBrushProperty =
        DependencyProperty.Register(nameof(PanelBrush), typeof(Brush), typeof(SpectrumControl),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }
    public Brush PeakBrush
    {
        get => (Brush)GetValue(PeakBrushProperty);
        set => SetValue(PeakBrushProperty, value);
    }
    public Brush PanelBrush
    {
        get => (Brush)GetValue(PanelBrushProperty);
        set => SetValue(PanelBrushProperty, value);
    }

    /// <summary>Feeds 128 FFT magnitude bins and repaints. Pass null when idle.</summary>
    public void Update(float[]? fft)
    {
        if (fft is null || fft.Length < 128)
        {
            if (!_active) return;
            _active = false;
            Array.Clear(_levels);
        }
        else
        {
            _active = true;
            // Map the FFT bins logarithmically onto the bars; square-root compresses magnitudes.
            for (var bar = 0; bar < BarCount; bar++)
            {
                var from = (int)Math.Pow(1.6, bar * 0.34) - 1;
                var to = Math.Max(from + 1, (int)Math.Pow(1.6, (bar + 1) * 0.34) - 1);
                from = Math.Min(from, 127);
                to = Math.Min(to, 127);
                var max = 0f;
                for (var i = from; i <= to; i++) max = Math.Max(max, fft[i]);
                var level = Math.Min(1f, (float)Math.Sqrt(max) * 1.9f);
                _levels[bar] = Math.Max(level, _levels[bar] - 0.09f);
                if (level >= _peaks[bar])
                {
                    _peaks[bar] = level;
                    _peakHold[bar] = 12;
                }
                else if (--_peakHold[bar] <= 0)
                {
                    _peaks[bar] = Math.Max(0, _peaks[bar] - 0.03f);
                }
            }
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 4 || height < 4) return;

        dc.DrawRectangle(PanelBrush, null, new Rect(0, 0, width, height));
        if (!_active) return;

        var slot = width / BarCount;
        var barWidth = Math.Max(1, slot - 1);
        for (var i = 0; i < BarCount; i++)
        {
            var x = i * slot;
            var barHeight = Math.Max(_levels[i] * height, _levels[i] > 0.01 ? 1 : 0);
            if (barHeight > 0)
                dc.DrawRectangle(BarBrush, null, new Rect(x, height - barHeight, barWidth, barHeight));
            if (_peaks[i] > 0.02)
            {
                var py = height - _peaks[i] * height - 1;
                dc.DrawRectangle(PeakBrush, null, new Rect(x, Math.Max(0, py), barWidth, 1.5));
            }
        }
    }
}
