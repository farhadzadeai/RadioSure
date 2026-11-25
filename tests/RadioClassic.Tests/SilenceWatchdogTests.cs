using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using RadioClassic.Audio;
using RadioClassic.Models;
using RadioClassic.Services;
using Xunit;

namespace RadioClassic.Tests;

/// <summary>
/// Verifies the silence watchdog end-to-end against a deterministic silent stream:
/// a local HTTP server looping real captured silent AAC frames (an off-air station's
/// actual output). The engine must connect, detect the silence, reconnect once, and
/// then fail honestly with Err.NoAudio instead of pretending to play forever.
/// </summary>
[Collection("Integration")]
public class SilenceWatchdogTests
{
    private static readonly string LogFile = Environment.ExpandEnvironmentVariables(@"%TEMP%\watchdog-probe.txt");
    private static void WriteLine(string s) { lock (LogFile) File.AppendAllText(LogFile, s + "\n"); }

    /// <summary>Serves the silent AAC payload in an endless loop until stopped.</summary>
    private sealed class SilentStreamServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        public int Port { get; }

        public SilentStreamServer()
        {
            // pick a free port
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _ = Task.Run(ServeLoop);
        }

        private async Task ServeLoop()
        {
            var payload = File.ReadAllBytes("silence.aac");
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentType = "audio/aac";
                        ctx.Response.SendChunked = true;
                        var stream = ctx.Response.OutputStream;
                        while (!_cts.IsCancellationRequested)
                        {
                            await stream.WriteAsync(payload, _cts.Token);
                            await stream.FlushAsync();
                            // simulate a 64kbps stream: 16KB ~= 2 seconds of audio
                            await Task.Delay(500, _cts.Token);
                        }
                    }
                    catch { }
                    finally { try { ctx.Response.Close(); } catch { } }
                });
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
        }
    }

    [Fact]
    public async Task SilentStreamEventuallyErrors()
    {
        File.WriteAllText(LogFile, "");
        using var server = new SilentStreamServer();
        var settings = new SettingsService();
        using var engine = new AudioEngine(settings);
        engine.Initialize();
        engine.Play(new Station { Name = "SilenceTest", Url = $"http://127.0.0.1:{server.Port}/stream" });

        var deadline = DateTime.Now.AddSeconds(75);
        var lastState = "";
        while (DateTime.Now < deadline)
        {
            var desc = $"{engine.State}/{engine.LastErrorKey}";
            if (desc != lastState) { WriteLine($"{DateTime.Now:HH:mm:ss} -> {desc}"); lastState = desc; }
            if (engine.State == PlayerState.Error) break;
            await Task.Delay(300);
        }
        WriteLine($"final: state={engine.State} err={engine.LastErrorKey}");
        Assert.Equal(PlayerState.Error, engine.State);
        Assert.True(engine.LastErrorKey is "Err.NoAudio" or "Err.StreamStalled",
            $"expected silence error, got {engine.State}/{engine.LastErrorKey}");
    }
}
