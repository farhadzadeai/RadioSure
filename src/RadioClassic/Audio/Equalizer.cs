using RadioClassic.Models;

namespace RadioClassic.Audio;

/// <summary>
/// A real equalizer: 10 peaking bands plus bass/treble shelves, preamp and balance, implemented
/// as cascaded biquad filters (RBJ Audio EQ Cookbook) that run on the decoded float samples
/// inside the audio engine's DSP callback. No add-on library is required.
/// </summary>
public sealed unsafe class Equalizer
{
    private const int MaxChannels = 8;
    private const int Sections = 12; // 10 bands + bass shelf + treble shelf
    private const double BassHz = 100, TrebleHz = 10000;

    private sealed class Plan
    {
        public int SampleRate;
        public bool Active;
        public int Count;
        // Normalized coefficients per section: b0, b1, b2, a1, a2
        public readonly float[] C = new float[Sections * 5];
        public float GainL = 1, GainR = 1;
    }

    private volatile Plan _plan = new();
    private volatile EqSettings _settings = new();
    // Filter state (Direct Form II transposed): [channel, section, 2]
    private readonly float[] _state = new float[MaxChannels * Sections * 2];

    /// <summary>Applies new settings. Safe to call from the UI thread while audio is playing.</summary>
    public void Update(EqSettings settings)
    {
        _settings = Clone(settings);
        _plan = Build(_settings, _plan.SampleRate);
    }

    private static EqSettings Clone(EqSettings s) => new()
    {
        Enabled = s.Enabled, Preset = s.Preset, Bands = (double[])s.Bands.Clone(),
        Bass = s.Bass, Treble = s.Treble, Preamp = s.Preamp, Balance = s.Balance
    };

    private static Plan Build(EqSettings s, int sampleRate)
    {
        var plan = new Plan { SampleRate = sampleRate };
        if (!s.Enabled || sampleRate <= 0) return plan;

        var preamp = Math.Pow(10, Math.Clamp(s.Preamp, -12, 12) / 20);
        var balance = Math.Clamp(s.Balance, -1, 1);
        plan.GainL = (float)(preamp * (balance > 0 ? 1 - balance : 1));
        plan.GainR = (float)(preamp * (balance < 0 ? 1 + balance : 1));

        for (var i = 0; i < 10; i++)
        {
            var gain = Math.Clamp(s.Bands[i], -12, 12);
            double f = EqSettings.BandFrequencies[i];
            if (Math.Abs(gain) < 0.05 || f >= sampleRate * 0.45) continue;
            AddPeaking(plan, f, gain, 1.41, sampleRate);
        }
        if (Math.Abs(s.Bass) >= 0.05) AddShelf(plan, BassHz, Math.Clamp(s.Bass, -12, 12), sampleRate, low: true);
        if (Math.Abs(s.Treble) >= 0.05 && TrebleHz < sampleRate * 0.45)
            AddShelf(plan, TrebleHz, Math.Clamp(s.Treble, -12, 12), sampleRate, low: false);

        plan.Active = true;
        return plan;
    }

    private static void AddPeaking(Plan p, double freq, double gainDb, double q, int sampleRate)
    {
        var a = Math.Pow(10, gainDb / 40);
        var w0 = 2 * Math.PI * freq / sampleRate;
        var alpha = Math.Sin(w0) / (2 * q);
        var cos = Math.Cos(w0);
        Store(p, 1 + alpha * a, -2 * cos, 1 - alpha * a, 1 + alpha / a, -2 * cos, 1 - alpha / a);
    }

    private static void AddShelf(Plan p, double freq, double gainDb, int sampleRate, bool low)
    {
        var a = Math.Pow(10, gainDb / 40);
        var w0 = 2 * Math.PI * freq / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / 2 * Math.Sqrt(2); // shelf slope S = 1
        var k = 2 * Math.Sqrt(a) * alpha;
        if (low)
            Store(p,
                a * ((a + 1) - (a - 1) * cos + k), 2 * a * ((a - 1) - (a + 1) * cos), a * ((a + 1) - (a - 1) * cos - k),
                (a + 1) + (a - 1) * cos + k, -2 * ((a - 1) + (a + 1) * cos), (a + 1) + (a - 1) * cos - k);
        else
            Store(p,
                a * ((a + 1) + (a - 1) * cos + k), -2 * a * ((a - 1) + (a + 1) * cos), a * ((a + 1) + (a - 1) * cos - k),
                (a + 1) - (a - 1) * cos + k, 2 * ((a - 1) - (a + 1) * cos), (a + 1) - (a - 1) * cos - k);
    }

    private static void Store(Plan p, double b0, double b1, double b2, double a0, double a1, double a2)
    {
        var i = p.Count++ * 5;
        p.C[i] = (float)(b0 / a0);
        p.C[i + 1] = (float)(b1 / a0);
        p.C[i + 2] = (float)(b2 / a0);
        p.C[i + 3] = (float)(a1 / a0);
        p.C[i + 4] = (float)(a2 / a0);
    }

    /// <summary>Returns the gain in dB the equalizer applies at a frequency (used by tests).</summary>
    public double ResponseDb(double freq, int sampleRate)
    {
        var plan = Build(_settings, sampleRate);
        if (!plan.Active) return 0;
        var w = 2 * Math.PI * freq / sampleRate;
        var z1 = System.Numerics.Complex.FromPolarCoordinates(1, -w);
        var z2 = z1 * z1;
        var h = System.Numerics.Complex.One;
        for (var s = 0; s < plan.Count; s++)
        {
            var i = s * 5;
            h *= (plan.C[i] + plan.C[i + 1] * z1 + plan.C[i + 2] * z2) / (1 + plan.C[i + 3] * z1 + plan.C[i + 4] * z2);
        }
        return 20 * Math.Log10(h.Magnitude * Math.Max(plan.GainL, plan.GainR));
    }

    /// <summary>Processes interleaved 32-bit float samples in place.</summary>
    public void Process(float* buffer, int sampleCount, int channels, int sampleRate)
    {
        var plan = _plan;
        if (plan.SampleRate != sampleRate)
        {
            _plan = plan = Build(_settings, sampleRate);
            Array.Clear(_state);
        }
        if (!plan.Active || channels <= 0) return;

        var chans = Math.Min(channels, MaxChannels);
        var frames = sampleCount / channels;
        fixed (float* state = _state)
        fixed (float* coef = plan.C)
        {
            for (var ch = 0; ch < chans; ch++)
            {
                var gain = channels == 2 ? (ch == 0 ? plan.GainL : plan.GainR) : Math.Max(plan.GainL, plan.GainR);
                var st = state + ch * Sections * 2;
                var p = buffer + ch;
                for (var n = 0; n < frames; n++, p += channels)
                {
                    var x = *p;
                    for (var s = 0; s < plan.Count; s++)
                    {
                        var c = coef + s * 5;
                        var z = st + s * 2;
                        var y = c[0] * x + z[0];
                        z[0] = c[1] * x - c[3] * y + z[1];
                        z[1] = c[2] * x - c[4] * y;
                        x = y;
                    }
                    *p = x * gain;
                }
                // Flush denormals so silent passages do not burn CPU.
                for (var s = 0; s < plan.Count * 2; s++)
                    if (MathF.Abs(st[s]) < 1e-20f) st[s] = 0;
            }
        }
    }
}
