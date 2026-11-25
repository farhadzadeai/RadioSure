using System.Windows.Interop;
using RadioClassic.Core;

namespace RadioClassic.Services;

/// <summary>
/// Global hotkeys registered on a hidden message window, so they work even when the main
/// window is hidden: Ctrl+Alt+P play/pause, Ctrl+Alt+S stop, Ctrl+Alt+N next, Ctrl+Alt+B
/// previous, Ctrl+Alt+R record, Ctrl+Alt+Up/Down volume, plus the media keys.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int ID_PLAY = 1, ID_STOP = 2, ID_NEXT = 3, ID_PREV = 4, ID_RECORD = 5, ID_VOLUP = 6, ID_VOLDOWN = 7;
    private const int ID_MEDIA_PLAY = 11, ID_MEDIA_STOP = 12, ID_MEDIA_NEXT = 13, ID_MEDIA_PREV = 14;

    private const uint VK_PLAY = 0xFA, VK_STOP = 0xF9, VK_NEXT = 0xB0, VK_PREV = 0xB1;

    private HwndSource? _source;
    private readonly List<int> _registered = [];

    public event Action? PlayPauseRequested;
    public event Action? StopRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;
    public event Action? RecordRequested;
    public event Action<int>? VolumeDelta;

    public void Initialize()
    {
        if (_source is not null) return;
        var parameters = new HwndSourceParameters("RadioClassicHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>Applies the hotkey settings; can be called again whenever they change.</summary>
    public void Apply(bool globalHotkeys, bool mediaKeys)
    {
        if (_source is null) return;
        var hwnd = _source.Handle;
        foreach (var id in _registered) Win32.UnregisterHotKey(hwnd, id);
        _registered.Clear();

        if (globalHotkeys)
        {
            const uint mod = Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT;
            Register(hwnd, ID_PLAY, mod, 'P');
            Register(hwnd, ID_STOP, mod, 'S');
            Register(hwnd, ID_NEXT, mod, 'N');
            Register(hwnd, ID_PREV, mod, 'B');
            Register(hwnd, ID_RECORD, mod, 'R');
            Register(hwnd, ID_VOLUP, mod, 0x26); // up arrow
            Register(hwnd, ID_VOLDOWN, mod, 0x28); // down arrow
        }
        if (mediaKeys)
        {
            Register(hwnd, ID_MEDIA_PLAY, 0, VK_PLAY);
            Register(hwnd, ID_MEDIA_STOP, 0, VK_STOP);
            Register(hwnd, ID_MEDIA_NEXT, 0, VK_NEXT);
            Register(hwnd, ID_MEDIA_PREV, 0, VK_PREV);
        }
    }

    private void Register(IntPtr hwnd, int id, uint mod, uint vk)
    {
        if (Win32.RegisterHotKey(hwnd, id, mod, vk))
            _registered.Add(id);
        else
            Log.Warn($"Hotkey {id} is already used by another application");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Win32.WM_HOTKEY) return IntPtr.Zero;
        handled = true;
        switch ((int)wParam)
        {
            case ID_PLAY or ID_MEDIA_PLAY: PlayPauseRequested?.Invoke(); break;
            case ID_STOP or ID_MEDIA_STOP: StopRequested?.Invoke(); break;
            case ID_NEXT or ID_MEDIA_NEXT: NextRequested?.Invoke(); break;
            case ID_PREV or ID_MEDIA_PREV: PreviousRequested?.Invoke(); break;
            case ID_RECORD: RecordRequested?.Invoke(); break;
            case ID_VOLUP: VolumeDelta?.Invoke(5); break;
            case ID_VOLDOWN: VolumeDelta?.Invoke(-5); break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is null) return;
        var hwnd = _source.Handle;
        foreach (var id in _registered) Win32.UnregisterHotKey(hwnd, id);
        _registered.Clear();
        _source.Dispose();
        _source = null;
    }
}
