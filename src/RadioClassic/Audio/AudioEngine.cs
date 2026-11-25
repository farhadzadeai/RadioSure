using RadioClassic.Core;
using RadioClassic.Models;
using RadioClassic.Services;

namespace RadioClassic.Audio;

public enum PlayerState
{
    Stopped,
    Connecting,
    Playing,
    Paused,
    Reconnecting,
    Error
}

public sealed record AudioDevice(int Index, string Name);

/// <summary>
/// Internet radio playback on top of BASS. Connections are made on background threads so the
/// user interface never blocks; events may be raised on any thread.
/// </summary>
public sealed unsafe class AudioEngine : IDisposable
{
    private readonly SettingsService _settings;
    private readonly object _gate = new();
    private readonly Bass.SyncProc _metaSync, _endSync;
    private readonly Bass.DspProc _dsp;

    private uint _stream;
    private int _generation;
    private bool _initialized;
    private int _volume = 70;
    private bool _muted;
    private DateTime _playingSince;
    private int _sampleRate = 44100, _channels = 2;
    private Timer? _watchdog;
    private int _silentTicks;
    private bool _silenceRetried;
    private bool _stalled;

    public Equalizer Equalizer { get; } = new();
    public PlayerState State { get; private set; } = PlayerState.Stopped;
    public Station? Station { get; private set; }
    public StreamMetadata Metadata { get; private set; } = StreamMetadata.Empty;
    public string Codec { get; private set; } = "";
    /// <summary>Station name announced by the stream itself (icy-name), if any.</summary>
    public string StreamName { get; private set; } = "";
    /// <summary>Localization key of the last error.</summary>
    public string LastErrorKey { get; private set; } = "";
    public int ReconnectAttempt { get; private set; }
    public bool NoAudioDevice { get; private set; }

    public event Action? StateChanged;
    public event Action? MetadataChanged;

    public AudioEngine(SettingsService settings)
    {
        _settings = settings;
        _metaSync = (_, channel, _, _) => ReadMetadata(channel);
        _endSync = (_, channel, _, _) => OnStreamEnded(channel);
        _dsp = (_, _, buffer, length, _) =>
            Equalizer.Process((float*)buffer, (int)(length / sizeof(float)), _channels, _sampleRate);
        _volume = settings.Current.Volume;
        _muted = settings.Current.Muted;
        Equalizer.Update(settings.Current.Equalizer);
    }

    // ------------------------------------------------------------------ setup

    /// <summary>Initializes BASS. Falls back to the "no sound" device so recording still works without a sound card.</summary>
    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized) return;
            Bass.BASS_SetConfig(Bass.CONFIG_UNICODE, 1);
            Bass.LoadPlugins();
            ApplyNetworkSettings();
            var device = _settings.Current.OutputDevice;
            if (!Bass.BASS_Init(device, 48000, 0, IntPtr.Zero, IntPtr.Zero) &&
                !(device != -1 && Bass.BASS_Init(-1, 48000, 0, IntPtr.Zero, IntPtr.Zero)))
            {
                Log.Warn($"No audio output device (BASS error {Bass.BASS_ErrorGetCode()}); using silent device");
                NoAudioDevice = true;
                Bass.BASS_Init(0, 48000, 0, IntPtr.Zero, IntPtr.Zero);
            }
            _initialized = true;
        }
    }

    public void ApplyNetworkSettings()
    {
        var s = _settings.Current;
        Bass.BASS_SetConfig(Bass.CONFIG_NET_TIMEOUT, (uint)s.TimeoutSeconds * 1000);
        Bass.BASS_SetConfig(Bass.CONFIG_NET_READTIMEOUT, (uint)Math.Max(s.TimeoutSeconds, 10) * 1000);
        Bass.BASS_SetConfig(Bass.CONFIG_NET_BUFFER, (uint)s.BufferMs);
        Bass.BASS_SetConfig(Bass.CONFIG_NET_PREBUF, 30);
        Bass.BASS_SetConfig(Bass.CONFIG_NET_PLAYLIST, 1);
        Bass.SetUserAgent(RadioBrowserClient.UserAgent);
        Bass.SetProxy(s.ProxyMode switch
        {
            ProxyMode.None => null,
            ProxyMode.Manual when !string.IsNullOrWhiteSpace(s.ProxyHost) => $"{s.ProxyHost.Trim()}:{s.ProxyPort}",
            _ => ""
        });
    }

    public static List<AudioDevice> GetOutputDevices()
    {
        var list = new List<AudioDevice>();
        for (uint i = 1; Bass.BASS_GetDeviceInfo(i, out var info); i++)
            if ((info.flags & Bass.DEVICE_ENABLED) != 0)
                list.Add(new AudioDevice((int)i, Bass.PtrToString(info.name) ?? $"Device {i}"));
        return list;
    }

    /// <summary>Switches the output device by re-initializing BASS; the current station is resumed.</summary>
    public void ChangeOutputDevice()
    {
        var station = State is PlayerState.Playing or PlayerState.Connecting or PlayerState.Reconnecting ? Station : null;
        Stop();
        lock (_gate)
        {
            if (_initialized) Bass.BASS_Free();
            _initialized = false;
            NoAudioDevice = false;
        }
        Initialize();
        if (station is not null) Play(station);
    }

    // ------------------------------------------------------------------ transport

    public void Play(Station station)
    {
        if (!PlaylistIO.IsValidStreamUrl(station.Url))
        {
            Station = station;
            LastErrorKey = "Err.InvalidUrl";
            SetState(PlayerState.Error);
            return;
        }
        Initialize();
        var generation = Interlocked.Increment(ref _generation);
        Station = station;
        Metadata = StreamMetadata.Empty;
        StreamName = "";
        ReconnectAttempt = 0;
        _silenceRetried = false;
        _silentTicks = 0;
        SetState(PlayerState.Connecting);
        MetadataChanged?.Invoke();
        _ = Task.Run(() => ConnectLoop(station, generation, reconnect: false));
    }

    private void ConnectLoop(Station station, int generation, bool reconnect)
    {
        var settings = _settings.Current;
        var maxAttempts = settings.AutoReconnect ? settings.RetryAttempts : 0;
        for (var attempt = 0; ; attempt++)
        {
            if (generation != _generation) return;
            if (attempt > 0 || reconnect)
            {
                ReconnectAttempt = attempt + (reconnect ? 1 : 0);
                SetState(PlayerState.Reconnecting);
            }

            var handle = Bass.BASS_StreamCreateURL(station.Url, 0,
                Bass.STREAM_BLOCK | Bass.SAMPLE_FLOAT | Bass.UNICODE, null, IntPtr.Zero);
            var error = handle == 0 ? Bass.BASS_ErrorGetCode() : 0;

            if (generation != _generation)
            {
                if (handle != 0) Bass.BASS_StreamFree(handle);
                return;
            }
            if (handle != 0)
            {
                Activate(handle, generation);
                return;
            }

            Log.Warn($"Connect failed ({station.Name}): BASS error {error}");
            // Format problems will not fix themselves; only network errors are retried.
            var retryable = error is not (Bass.ERROR_FILEFORM or Bass.ERROR_CODEC or Bass.ERROR_FORMAT);
            if (!retryable || attempt >= maxAttempts)
            {
                LastErrorKey = Bass.ErrorKey(error);
                SetState(PlayerState.Error);
                return;
            }
            // Back off a little more on each attempt, but stay responsive to Stop/Play.
            var delay = Math.Min(2000 + attempt * 1500, 10000);
            for (var waited = 0; waited < delay && generation == _generation; waited += 100) Thread.Sleep(100);
        }
    }

    private void Activate(uint handle, int generation)
    {
        uint old;
        lock (_gate)
        {
            if (generation != _generation)
            {
                Bass.BASS_StreamFree(handle);
                return;
            }
            old = _stream;
            _stream = handle;

            if (Bass.BASS_ChannelGetInfo(handle, out var info))
            {
                _sampleRate = (int)info.freq;
                _channels = (int)info.chans;
                Codec = Bass.CodecName(info.ctype);
            }
            Bass.BASS_ChannelSetSync(handle, Bass.SYNC_META, 0, _metaSync, IntPtr.Zero);
            Bass.BASS_ChannelSetSync(handle, Bass.SYNC_OGG_CHANGE, 0, _metaSync, IntPtr.Zero);
            Bass.BASS_ChannelSetSync(handle, Bass.SYNC_END, 0, _endSync, IntPtr.Zero);
            Bass.BASS_ChannelSetDSP(handle, _dsp, IntPtr.Zero, 0);
            Bass.BASS_ChannelSetAttribute(handle, Bass.ATTRIB_VOL, EffectiveVolume());
            Bass.BASS_ChannelPlay(handle, false);
            _playingSince = DateTime.Now;
            _silentTicks = 0;
            StartWatchdog();
        }
        FadeOutAndFree(old);
        ReadStreamHeaders(handle);
        ReadMetadata(handle);
        ReconnectAttempt = 0;
        SetState(PlayerState.Playing);
    }

    /// <summary>Lets the previous station fade out briefly for a smooth switch.</summary>
    private static void FadeOutAndFree(uint handle)
    {
        if (handle == 0) return;
        Bass.BASS_ChannelSlideAttribute(handle, Bass.ATTRIB_VOL, 0, 250);
        _ = Task.Delay(300).ContinueWith(_ => Bass.BASS_StreamFree(handle));
    }

    // ------------------------------------------------------------------ silence / stall watchdog

    /// <summary>True while BASS reports the stream as stalled (starved of data).</summary>
    public bool IsStalled => _stalled;

    /// <summary>
    /// Some stations connect fine but deliver digital silence (off-air encoders, broken feeds),
    /// and some stall mid-stream without ever ending. A fooled "Playing" state is worse than an
    /// honest error, so a watchdog watches the audio level: after ~8 silent seconds it reconnects
    /// once, and if the stream stays silent it fails with Err.NoAudio instead.
    /// </summary>
    private void StartWatchdog()
    {
        StopWatchdog();
        var generation = _generation;
        _watchdog = new System.Threading.Timer(_ => WatchdogTick(generation), null, 2000, 2000);
    }

    private void StopWatchdog()
    {
        var timer = Interlocked.Exchange(ref _watchdog, null);
        timer?.Dispose();
    }

    private void WatchdogTick(int generation)
    {
        uint handle;
        lock (_gate)
        {
            if (generation != _generation || State != PlayerState.Playing) return;
            handle = _stream;
        }
        if (handle == 0) return;

        var active = Bass.BASS_ChannelIsActive(handle);
        var stalled = active == 2; // BASS_ACTIVE_STALLED
        if (stalled != _stalled)
        {
            _stalled = stalled;
            StateChanged?.Invoke();
        }

        var level = Bass.BASS_ChannelGetLevel(handle);
        var silent = level == 0 || stalled;
        if (!silent)
        {
            // Healthy audio again: future silences earn a fresh reconnect chance.
            _silentTicks = 0;
            _silenceRetried = false;
            return;
        }

        var ticks = Interlocked.Increment(ref _silentTicks);
        if (ticks < 4) return; // allow up to ~8 seconds of silence before acting

        var station = Station;
        if (station is null) return;
        if (!_silenceRetried && _settings.Current.AutoReconnect)
        {
            _silenceRetried = true;
            _silentTicks = 0;
            Log.Warn($"Stream silent for 8s, reconnecting: {station.Name}");
            _ = Task.Run(() => ConnectLoop(station, generation, reconnect: true));
        }
        else
        {
            Log.Warn($"Stream stayed silent, giving up: {station.Name}");
            LastErrorKey = stalled ? "Err.StreamStalled" : "Err.NoAudio";
            uint old;
            lock (_gate)
            {
                old = _stream;
                _stream = 0;
            }
            Bass.BASS_StreamFree(old);
            SetState(PlayerState.Error);
        }
    }

    private void OnStreamEnded(uint channel)
    {
        // Called on a BASS thread when the connection was lost and the buffer ran dry.
        _ = Task.Run(() =>
        {
            Station? station;
            int generation;
            lock (_gate)
            {
                if (channel != _stream || State != PlayerState.Playing) return;
                station = Station;
                generation = _generation;
            }
            if (station is null) return;
            if (!_settings.Current.AutoReconnect)
            {
                LastErrorKey = "Err.ConnectionLost";
                SetState(PlayerState.Error);
                return;
            }
            Log.Info($"Stream ended, reconnecting: {station.Name}");
            ConnectLoop(station, generation, reconnect: true);
        });
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        StopWatchdog();
        _stalled = false;
        uint old;
        lock (_gate)
        {
            old = _stream;
            _stream = 0;
        }
        FadeOutAndFree(old);
        if (!Metadata.IsEmpty)
        {
            Metadata = StreamMetadata.Empty;
            MetadataChanged?.Invoke();
        }
        SetState(PlayerState.Stopped);
    }

    /// <summary>
    /// Pauses playback. A live stream cannot really be paused for long: when resuming after the
    /// buffer has been lost the engine simply reconnects.
    /// </summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (State != PlayerState.Playing || _stream == 0) return;
            Bass.BASS_ChannelPause(_stream);
        }
        SetState(PlayerState.Paused);
    }

    public void Resume()
    {
        var station = Station;
        if (station is null) return;
        if (State == PlayerState.Paused)
        {
            lock (_gate)
            {
                if (_stream != 0 && Bass.BASS_ChannelPlay(_stream, false) &&
                    Bass.BASS_StreamGetFilePosition(_stream, Bass.FILEPOS_CONNECTED) == 1)
                {
                    _playingSince = DateTime.Now;
                    SetState(PlayerState.Playing);
                    return;
                }
            }
        }
        Play(station);
    }

    // ------------------------------------------------------------------ volume

    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            ApplyVolume();
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            ApplyVolume();
        }
    }

    // A squared curve feels linear to the ear.
    private float EffectiveVolume() => _muted ? 0f : _volume / 100f * (_volume / 100f);

    private void ApplyVolume()
    {
        lock (_gate)
        {
            if (_stream != 0) Bass.BASS_ChannelSetAttribute(_stream, Bass.ATTRIB_VOL, EffectiveVolume());
        }
    }

    // ------------------------------------------------------------------ status

    public TimeSpan Elapsed => State == PlayerState.Playing ? DateTime.Now - _playingSince : TimeSpan.Zero;

    /// <summary>Download buffer fill level, 0-100.</summary>
    public int BufferPercent
    {
        get
        {
            lock (_gate)
            {
                if (_stream == 0) return 0;
                var size = Bass.BASS_StreamGetFilePosition(_stream, Bass.FILEPOS_END);
                var used = Bass.BASS_StreamGetFilePosition(_stream, Bass.FILEPOS_BUFFER);
                if (size <= 0 || used < 0) return State == PlayerState.Playing ? 100 : 0;
                return (int)Math.Clamp(used * 100 / size, 0, 100);
            }
        }
    }

    public int BitrateKbps
    {
        get
        {
            lock (_gate)
            {
                if (_stream == 0) return 0;
                return Bass.BASS_ChannelGetAttribute(_stream, Bass.ATTRIB_BITRATE, out var kbps) ? (int)Math.Round(kbps) : 0;
            }
        }
    }

    public int SampleRate => _sampleRate;
    public int Channels => _channels;

    /// <summary>Fills <paramref name="fft"/> (128 values) with the current spectrum. Returns false when idle.</summary>
    public bool GetSpectrum(float[] fft)
    {
        lock (_gate)
        {
            if (_stream == 0 || State != PlayerState.Playing || fft.Length < 128) return false;
            fixed (float* p = fft)
                return Bass.BASS_ChannelGetData(_stream, p, Bass.DATA_FFT256) > 0;
        }
    }

    // ------------------------------------------------------------------ metadata

    private void ReadStreamHeaders(uint handle)
    {
        var headers = Bass.PtrToStringList(Bass.BASS_ChannelGetTags(handle, Bass.TAG_ICY));
        headers.AddRange(Bass.PtrToStringList(Bass.BASS_ChannelGetTags(handle, Bass.TAG_HTTP)));
        foreach (var h in headers)
        {
            if (h.StartsWith("icy-name:", StringComparison.OrdinalIgnoreCase))
            {
                StreamName = Text.Clean(h[9..], 120);
                break;
            }
        }
    }

    private void ReadMetadata(uint handle)
    {
        if (handle != _stream) return;
        var meta = StreamMetadata.FromIcy(Bass.PtrToString(Bass.BASS_ChannelGetTags(handle, Bass.TAG_META)));
        if (meta.IsEmpty)
            meta = StreamMetadata.FromComments(Bass.PtrToStringList(Bass.BASS_ChannelGetTags(handle, Bass.TAG_OGG)));
        if (meta == Metadata) return;
        Metadata = meta;
        MetadataChanged?.Invoke();
    }

    private void SetState(PlayerState state)
    {
        State = state;
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        Interlocked.Increment(ref _generation);
        StopWatchdog();
        lock (_gate)
        {
            if (_stream != 0) Bass.BASS_StreamFree(_stream);
            _stream = 0;
            if (_initialized) Bass.BASS_Free();
            _initialized = false;
        }
    }
}
