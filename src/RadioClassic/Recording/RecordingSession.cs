using System.IO;
using RadioClassic.Audio;
using RadioClassic.Core;
using RadioClassic.Data;
using RadioClassic.Models;

namespace RadioClassic.Recording;

public enum RecordingState
{
    Connecting,
    Recording,
    Reconnecting,
    Ended
}

public sealed record RecordingOptions(
    RecordingFormat Format,
    int Mp3Bitrate,
    bool SplitTracks,
    string TrackTemplate,
    string ContinuousTemplate,
    string Directory,
    bool StationSubfolders,
    int RetryAttempts);

/// <summary>
/// One independent recording session for one station. Runs on its own thread, captures either
/// the original stream bytes (no quality loss) or re-encoded audio, splits files on track
/// changes, reconnects on failure and never throws into the rest of the application.
/// </summary>
public sealed unsafe class RecordingSession : IDisposable
{
    private static readonly byte* DecodeBuffer = (byte*)System.Runtime.InteropServices.NativeMemory.Alloc(32768);

    private readonly RecordingRepository _repo;
    private readonly RecordingOptions _options;
    private readonly DateTime? _endAt;
    private readonly bool _scheduled;
    private readonly object _io = new();
    private readonly Bass.SyncProc _metaSync;
    private readonly Bass.SyncProc _endSync;
    private readonly Bass.DownloadProc _downloadProc;

    private Thread? _thread;
    private volatile bool _stopRequested;
    private uint _stream;
    private int _generation;

    // Output state (all guarded by _io)
    private FileStream? _out;
    private long _rowId;
    private string _currentFile = "";
    private string _currentTitle = "";
    private string _extension = ".mp3";
    private bool _rawMode;
    private uint _encoder;

    public Guid Id { get; } = Guid.NewGuid();
    public string StationName { get; }
    public string StationUrl { get; }
    public long StationId { get; }
    public RecordingState State { get; private set; } = RecordingState.Connecting;
    /// <summary>Localization key or plain error text when State is Ended and Success is false.</summary>
    public string ErrorMessage { get; private set; } = "";
    public bool EndedByError { get; private set; }
    public DateTime StartedAt { get; private set; }
    public string CurrentTrack { get; private set; } = "";
    public string Codec { get; private set; } = "";
    public long TotalBytes { get; private set; }
    public int FilesWritten { get; private set; }
    public string EffectiveFormat { get; private set; } = "";
    public bool IsRecording => State is RecordingState.Recording or RecordingState.Connecting or RecordingState.Reconnecting;
    public DateTime? EndAt => _endAt;

    public string CurrentFilePath
    {
        get { lock (_io) return _currentFile; }
    }

    public long CurrentFileBytes
    {
        get
        {
            lock (_io)
            {
                try { return _out?.Length ?? 0; } catch { return 0; }
            }
        }
    }

    /// <summary>Progress/state changes; raised on background threads.</summary>
    public event Action<RecordingSession>? Changed;
    public event Action<RecordingSession>? Ended;

    public RecordingSession(long stationId, string stationName, string stationUrl,
        RecordingOptions options, RecordingRepository repo, DateTime? endAt = null, bool scheduled = false)
    {
        StationId = stationId;
        StationName = stationName;
        StationUrl = stationUrl;
        _options = options;
        _repo = repo;
        _endAt = endAt;
        _scheduled = scheduled;
        _metaSync = (_, channel, _, _) => { if (channel == _stream) OnMetadata(); };
        _endSync = (_, channel, _, _) => { };
        _downloadProc = OnDownload;
    }

    public void Start()
    {
        Bass.EnsureDecodeInit();
        _thread = new Thread(Run) { IsBackground = true, Name = "rec-" + StationName };
        _thread.Start();
    }

    /// <summary>Asks the session to finish. The file is finalized within a few seconds.</summary>
    public void Stop() => _stopRequested = true;

    // ------------------------------------------------------------------ main loop

    private void Run()
    {
        StartedAt = DateTime.Now;
        try
        {
            Directory.CreateDirectory(_options.Directory);
        }
        catch (Exception ex)
        {
            Fail("Err.RecDir", ex);
            return;
        }

        var generation = Interlocked.Increment(ref _generation);
        var failures = 0;
        while (!_stopRequested)
        {
            if (_endAt is { } endTime && DateTime.Now >= endTime) break;

            var handle = Bass.BASS_StreamCreateURL(StationUrl, 0,
                Bass.STREAM_DECODE | Bass.STREAM_BLOCK | Bass.SAMPLE_FLOAT | Bass.UNICODE, _downloadProc, IntPtr.Zero);
            if (handle == 0)
            {
                var error = Bass.BASS_ErrorGetCode();
                var retryable = error is not (Bass.ERROR_FILEFORM or Bass.ERROR_CODEC or Bass.ERROR_FORMAT);
                if (!retryable || ++failures > _options.RetryAttempts)
                {
                    Fail(Bass.ErrorKey(error), null);
                    return;
                }
                if (!Backoff(failures)) break;
                continue;
            }
            failures = 0;

            bool intentionalEnd;
            try
            {
                if (!BeginStream(handle, generation)) return; // stopped while connecting
                intentionalEnd = Pump(handle, generation);
                CloseStreamKeepFiles();
            }
            catch (Exception ex)
            {
                CloseStreamKeepFiles();
                Fail("Err.RecWrite", ex);
                return;
            }

            if (intentionalEnd || _stopRequested) break;
            // The connection dropped; try to reconnect.
            if (_endAt is { } end2 && DateTime.Now >= end2) break;
            SetState(RecordingState.Reconnecting);
            if (++failures > _options.RetryAttempts)
            {
                Fail("Err.ConnectionLost", null);
                return;
            }
            if (!Backoff(failures)) break;
        }
        Finish(success: true, "");
    }

    /// <summary>Waits between reconnect attempts. Returns false when a stop was requested.</summary>
    private bool Backoff(int attempt)
    {
        var delay = Math.Min(2000 + attempt * 1500, 8000);
        for (var waited = 0; waited < delay && !_stopRequested; waited += 100) Thread.Sleep(100);
        return !_stopRequested;
    }

    /// <summary>Sets up codec detection, syncs and the output mode for a connected stream.</summary>
    private bool BeginStream(uint handle, int generation)
    {
        if (_stopRequested || generation != _generation)
        {
            Bass.BASS_StreamFree(handle);
            return false;
        }
        _stream = handle;
        Bass.BASS_ChannelGetInfo(handle, out var info);
        Codec = Bass.CodecName(info.ctype);
        var ctype = (int)info.ctype;

        _rawMode = _options.Format == RecordingFormat.Original && ctype is
            Bass.CTYPE_STREAM_MP3 or Bass.CTYPE_STREAM_MP2 or Bass.CTYPE_STREAM_MP1 or
            Bass.CTYPE_STREAM_OGG or Bass.CTYPE_STREAM_AAC or Bass.CTYPE_STREAM_OPUS or
            Bass.CTYPE_STREAM_FLAC or Bass.CTYPE_STREAM_FLAC_OGG;

        if (_rawMode)
        {
            _extension = ctype switch
            {
                Bass.CTYPE_STREAM_OGG => ".ogg",
                Bass.CTYPE_STREAM_OPUS => ".opus",
                Bass.CTYPE_STREAM_FLAC or Bass.CTYPE_STREAM_FLAC_OGG => ".flac",
                Bass.CTYPE_STREAM_AAC => ".aac",
                _ => ".mp3"
            };
            EffectiveFormat = Codec;
        }
        else
        {
            // Containers that cannot be stored as-is (HLS, MP4, WMA/MF) or an explicit
            // transcode request both end up here.
            var fmt = _options.Format == RecordingFormat.Wav ? RecordingFormat.Wav : RecordingFormat.Mp3;
            _extension = fmt == RecordingFormat.Wav ? ".wav" : ".mp3";
            EffectiveFormat = fmt == RecordingFormat.Wav ? "WAV" : $"MP3 {_options.Mp3Bitrate}k";
        }

        Bass.BASS_ChannelSetSync(handle, Bass.SYNC_META, 0, _metaSync, IntPtr.Zero);
        Bass.BASS_ChannelSetSync(handle, Bass.SYNC_OGG_CHANGE, 0, _metaSync, IntPtr.Zero);
        Bass.BASS_ChannelSetSync(handle, Bass.SYNC_END, 0, _endSync, IntPtr.Zero);
        // Read metadata that arrived before the syncs were registered.
        OnMetadata();
        SetState(RecordingState.Recording);
        return true;
    }

    /// <summary>Pulls decoded audio (feeding any encoder) until stop, timeout or stream failure. Returns true when the session should end.</summary>
    private bool Pump(uint handle, int generation)
    {
        var lastSpaceCheck = DateTime.MinValue;
        var lastReport = DateTime.MinValue;
        while (!_stopRequested)
        {
            if (_endAt is { } end && DateTime.Now >= end)
                return true;
            var got = Bass.BASS_ChannelGetData(handle, DecodeBuffer, 32768);
            if (got < 0)
            {
                var error = Bass.BASS_ErrorGetCode();
                if (error == Bass.ERROR_ENDED || Bass.BASS_StreamGetFilePosition(handle, Bass.FILEPOS_CONNECTED) == 0)
                    return false; // connection lost
                Thread.Sleep(15);
                continue;
            }
            if (got == 0)
            {
                Thread.Sleep(10);
                continue;
            }

            var now = DateTime.Now;
            if ((now - lastSpaceCheck).TotalSeconds >= 3)
            {
                lastSpaceCheck = now;
                if (!CheckDiskSpace()) throw new IOException("Disk space is running out.");
                lock (_io) { try { _out?.Flush(); } catch { } UpdateOpenRow(); }
            }
            if ((now - lastReport).TotalMilliseconds >= 1000)
            {
                lastReport = now;
                Changed?.Invoke(this);
            }
        }
        return true;
    }

    private bool CheckDiskSpace()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_options.Directory));
            return root is not null && new DriveInfo(root).AvailableFreeSpace > 30L * 1024 * 1024;
        }
        catch
        {
            return true; // cannot determine; do not kill the recording
        }
    }

    private void CloseStreamKeepFiles()
    {
        var handle = _stream;
        _stream = 0;
        if (handle != 0) Bass.BASS_StreamFree(handle);
        lock (_io)
        {
            if (_encoder != 0)
            {
                // The encoder belongs to the dead stream; the next file starts fresh.
                Bass.BASS_Encode_Stop(handle);
                _encoder = 0;
                CloseFile(RecordingStatus.Completed);
            }
        }
    }

    // ------------------------------------------------------------------ output

    /// <summary>Receives the compressed stream exactly as broadcast. Fires on BASS download threads.</summary>
    private void OnDownload(IntPtr buffer, uint length, IntPtr user)
    {
        if (!_rawMode || length == 0 || buffer == IntPtr.Zero) return;
        lock (_io)
        {
            try
            {
                if (_out is null) OpenFile();
                if (_out is null) return;
                var bytes = new byte[length];
                System.Runtime.InteropServices.Marshal.Copy(buffer, bytes, 0, (int)length);
                _out.Write(bytes, 0, bytes.Length);
                TotalBytes += length;
            }
            catch (Exception ex)
            {
                Log.Error("Recording write failed", ex);
                _stopRequested = true;
                ErrorMessage = "Err.RecWrite";
                EndedByError = true;
            }
        }
    }

    private void OnMetadata()
    {
        var meta = StreamMetadata.FromIcy(Bass.PtrToString(Bass.BASS_ChannelGetTags(_stream, Bass.TAG_META)));
        if (meta.IsEmpty)
            meta = StreamMetadata.FromComments(Bass.PtrToStringList(Bass.BASS_ChannelGetTags(_stream, Bass.TAG_OGG)));
        var combined = meta.Display;
        if (combined.Length == 0 || combined == _currentTitle) return;
        var split = _currentTitle.Length > 0; // a real track change, not the first metadata
        _currentTitle = combined;
        CurrentTrack = combined;
        if (split && _options.SplitTracks)
        {
            lock (_io)
            {
                try
                {
                    CloseFile(RecordingStatus.Completed);
                    OpenFile();
                }
                catch (Exception ex)
                {
                    Log.Error("Track split failed, continuing in the current file", ex);
                }
            }
        }
        Changed?.Invoke(this);
    }

    /// <summary>Opens the next output file and registers it in the history immediately.</summary>
    private void OpenFile()
    {
        var dir = RecordingFileNamer.StationDirectory(_options.Directory, StationName, _options.StationSubfolders);
        var when = DateTime.Now;
        string baseName;
        if (_rawMode || FilesWritten > 0 || _currentTitle.Length == 0)
        {
            // Raw streams can only be split by track; encode mode uses track names from the first file on.
            baseName = _currentTitle.Length > 0 && (_rawMode || FilesWritten > 0)
                ? TrackName(when)
                : ContinuousName(when);
        }
        else
        {
            baseName = ContinuousName(when);
        }
        _currentFile = RecordingFileNamer.UniquePath(dir, baseName, _extension);
        FilesWritten++;

        if (_rawMode)
        {
            _out = new FileStream(_currentFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.SequentialScan);
        }
        else
        {
            _out = null;
            StartEncoder(_currentFile);
        }

        var entry = new RecordingEntry
        {
            StationName = StationName,
            StationUrl = StationUrl,
            Title = _currentTitle,
            File = _currentFile,
            Format = EffectiveFormat,
            Started = when,
            Status = RecordingStatus.Recording,
            Scheduled = _scheduled
        };
        _rowId = _repo.Insert(entry);
        Changed?.Invoke(this);
    }

    private string TrackName(DateTime when)
    {
        var meta = StreamMetadata.FromCombined(_currentTitle);
        return RecordingFileNamer.Expand(_options.TrackTemplate, StationName, meta.Artist, meta.Title, when);
    }

    private string ContinuousName(DateTime when) =>
        RecordingFileNamer.Expand(_options.ContinuousTemplate, StationName, "", "", when);

    private void StartEncoder(string file)
    {
        if (_extension == ".wav")
        {
            _encoder = Bass.BASS_Encode_Start(_stream, file, Bass.ENCODE_PCM | Bass.ENCODE_FP_16BIT | Bass.UNICODE, IntPtr.Zero, IntPtr.Zero);
        }
        else
        {
            var opts = $"--silent -b{_options.Mp3Bitrate}";
            _encoder = Bass.BASS_Encode_MP3_StartFile(_stream, opts, Bass.ENCODE_FP_16BIT | Bass.UNICODE, file);
        }
        if (_encoder == 0) throw new InvalidOperationException("The audio encoder could not be started.");
    }

    private void UpdateOpenRow()
    {
        if (_rowId == 0) return;
        var entry = new RecordingEntry
        {
            Id = _rowId,
            File = _currentFile,
            Title = _currentTitle,
            Bytes = _rawMode ? _out?.Length ?? 0 : SafeFileLength(_currentFile),
            Status = RecordingStatus.Recording
        };
        try { _repo.Update(entry); } catch { }
    }

    private static long SafeFileLength(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    /// <summary>Finalizes the current output file and its history entry.</summary>
    private void CloseFile(string status)
    {
        if (_rowId == 0 && _out is null) return;
        long bytes;
        try
        {
            if (_rawMode)
            {
                _out?.Flush();
                bytes = _out?.Length ?? 0;
                _out?.Dispose();
                _out = null;
            }
            else
            {
                if (_encoder != 0) Bass.BASS_Encode_Stop(_stream);
                _encoder = 0;
                bytes = SafeFileLength(_currentFile);
                // Give the encoder a moment to flush before measuring.
                if (bytes == 0) { Thread.Sleep(150); bytes = SafeFileLength(_currentFile); }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Closing a recording file failed", ex);
            bytes = 0;
        }
        if (_rowId != 0)
        {
            try
            {
                _repo.Update(new RecordingEntry
                {
                    Id = _rowId,
                    File = _currentFile,
                    Title = _currentTitle,
                    Ended = DateTime.Now,
                    Bytes = bytes,
                    Status = bytes == 0 ? RecordingStatus.Failed : status,
                    Error = bytes == 0 ? "The file is empty." : ""
                });
            }
            catch (Exception ex) { Log.Error("Updating recording history failed", ex); }
        }
        _rowId = 0;
    }

    // ------------------------------------------------------------------ lifecycle

    private void SetState(RecordingState state)
    {
        State = state;
        Changed?.Invoke(this);
    }

    private void Fail(string errorKey, Exception? ex)
    {
        if (ex is not null) Log.Error($"Recording failed ({StationName})", ex);
        lock (_io) CloseFile(RecordingStatus.Failed);
        EndedByError = true;
        Finish(success: false, errorKey);
    }

    private void Finish(bool success, string errorKey)
    {
        if (State == RecordingState.Ended) return;
        if (!EndedByError)
        {
            lock (_io) CloseFile(RecordingStatus.Completed);
        }
        var handle = _stream;
        _stream = 0;
        if (handle != 0) Bass.BASS_StreamFree(handle);
        ErrorMessage = EndedByError && ErrorMessage.Length > 0 ? ErrorMessage : errorKey;
        State = RecordingState.Ended;
        Log.Info($"Recording ended ({StationName}): {(EndedByError ? "failed - " + ErrorMessage : "ok")}, {FilesWritten} file(s), {Formatting.Size(TotalBytes)}");
        Ended?.Invoke(this);
        Changed?.Invoke(this);
    }

    public void Dispose()
    {
        Stop();
        if (_thread is { IsAlive: true } t && !t.Join(5000))
            Log.Warn($"Recording thread did not finish in time ({StationName})");
    }
}
