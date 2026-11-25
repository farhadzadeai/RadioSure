using System.Diagnostics;
using Microsoft.Win32;

namespace RadioClassic.Services;

/// <summary>Single-instance activation and the optional "Start with Windows" registry entry.</summary>
public static class SingleInstance
{
    private const string MutexName = @"Local\RadioClassic.SingleInstance";
    private const string EventName = @"Local\RadioClassic.ShowWindow";

    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;

    /// <summary>Returns true for the first instance. Other instances signal the first one.</summary>
    public static bool TryBecomePrimary()
    {
        _mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            try
            {
                using var evt = EventWaitHandle.OpenExisting(EventName);
                evt.Set();
            }
            catch { }
            return false;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        return true;
    }

    /// <summary>Invokes <paramref name="action"/> on the UI thread when another instance asks to show the window.</summary>
    public static void ListenForActivation(Action action)
    {
        if (_showEvent is null) return;
        var registration = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(action), null, -1, false);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => registration.Unregister(null);
    }

    public static void Release()
    {
        _showEvent?.Dispose();
        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
        }
    }
}

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RadioClassic";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch { return false; }
    }

    /// <summary>Per-user autostart, no administrator rights involved. Never enabled implicitly.</summary>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;
            if (enabled)
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (exe is not null) key.SetValue(ValueName, $"\"{exe}\" --minimized");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Core.Log.Error("Updating the autostart entry failed", ex);
        }
    }

    public static void RemoveValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { }
    }
}
