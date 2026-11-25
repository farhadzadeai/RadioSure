using System.Windows;
using System.Windows.Interop;
using RadioClassic.Core;

namespace RadioClassic.Services;

/// <summary>
/// System tray integration using plain Win32 (Shell_NotifyIcon + native popup menus), so no
/// WinForms dependency is needed. Owns a hidden message-only window that receives the callbacks.
/// </summary>
public sealed class TrayService : IDisposable
{
    private const uint WM_TRAY = 0x8001; // WM_APP + 1
    private const int CMD_BASE = 1000;

    // command ids
    private const int CMD_SHOW = 1, CMD_PLAYPAUSE = 2, CMD_STOP = 3, CMD_NEXT = 4, CMD_PREV = 5,
        CMD_RECORD = 6, CMD_MUTE = 7, CMD_VOLUP = 8, CMD_VOLDOWN = 9, CMD_EXIT = 10, CMD_FAV_BASE = 100;

    private HwndSource? _source;
    private Win32.NotifyIconData _data;
    private IntPtr _icon;
    private bool _added;

    // Commands, wired by the application shell.
    public event Action? ShowWindowRequested;
    public event Action? ExitRequested;
    public event Action? PlayPauseRequested;
    public event Action? StopRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;
    public event Action? RecordRequested;
    public event Action? MuteRequested;
    public event Action<int>? VolumeDelta;
    public event Action<long>? FavoriteChosen;

    // State providers (evaluated each time the menu opens).
    public Func<bool>? IsPlaying { get; set; }
    public Func<bool>? IsRecording { get; set; }
    public Func<bool>? IsMuted { get; set; }
    public Func<IReadOnlyList<(long Id, string Name)>>? Favorites { get; set; }

    public void Initialize()
    {
        if (_source is not null) return;
        var parameters = new HwndSourceParameters("RadioClassicTray")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        Win32.ExtractIconEx(System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!, 0, out _, out _icon, 1);
        _data = Win32.NewNotifyIconData(_source.Handle, WM_TRAY);
        _data.uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP;
        _data.hIcon = _icon;
        _data.szTip = "RadioClassic";
        _added = Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref _data);
    }

    public void SetTooltip(string text)
    {
        if (!_added) return;
        _data.szTip = text.Length > 63 ? text[..63] : text;
        _data.uFlags = Win32.NIF_TIP;
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref _data);
    }

    /// <summary>Shows a small balloon notification from the tray icon.</summary>
    public void Balloon(string title, string message)
    {
        if (!_added) return;
        _data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        _data.szInfo = message.Length > 255 ? message[..255] : message;
        _data.uTimeoutOrVersion = 4000;
        _data.dwInfoFlags = Win32.NIIF_INFO;
        _data.uFlags = Win32.NIF_INFO;
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref _data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAY)
        {
            var mouse = (uint)lParam;
            if (mouse == Win32.WM_LBUTTONDBLCLK)
            {
                ShowWindowRequested?.Invoke();
                handled = true;
            }
            else if (mouse == Win32.WM_RBUTTONUP)
            {
                ShowMenu(hwnd);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void ShowMenu(IntPtr hwnd)
    {
        var menu = Win32.CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            var playing = IsPlaying?.Invoke() == true;
            var recording = IsRecording?.Invoke() == true;
            var muted = IsMuted?.Invoke() == true;

            Win32.AppendMenu(menu, Win32.MF_STRING, CMD_SHOW, Loc.T("Tray.Show"));
            Win32.AppendMenu(menu, Win32.MF_SEPARATOR, 0, null);
            Win32.AppendMenu(menu, Win32.MF_STRING, CMD_PLAYPAUSE, Loc.T(playing ? "Menu.Pause" : "Menu.Play"));
            Win32.AppendMenu(menu, Win32.MF_STRING, CMD_STOP, Loc.T("Menu.Stop"));
            Win32.AppendMenu(menu, Win32.MF_STRING, CMD_NEXT, Loc.T("Tip.Next"));
            Win32.AppendMenu(menu, Win32.MF_STRING, CMD_PREV, Loc.T("Tip.Previous"));
            Win32.AppendMenu(menu, Win32.MF_STRING, CMD_RECORD, Loc.T(recording ? "Menu.StopRecording" : "Menu.Record"));
            Win32.AppendMenu(menu, Win32.MF_SEPARATOR, 0, null);

            var volume = Win32.CreatePopupMenu();
            Win32.AppendMenu(volume, Win32.MF_STRING, CMD_VOLUP, "+");
            Win32.AppendMenu(volume, Win32.MF_STRING, CMD_VOLDOWN, "-");
            Win32.AppendMenu(volume, Win32.MF_STRING | (muted ? Win32.MF_CHECKED : 0), CMD_MUTE, Loc.T("Menu.Mute"));
            Win32.AppendMenu(menu, Win32.MF_POPUP, (UIntPtr)volume, Loc.T("Tray.Volume"));

            var favorites = Favorites?.Invoke();
            if (favorites is { Count: > 0 })
            {
                var favMenu = Win32.CreatePopupMenu();
                for (var i = 0; i < favorites.Count && i < 15; i++)
                    Win32.AppendMenu(favMenu, Win32.MF_STRING, (UIntPtr)(CMD_FAV_BASE + i), favorites[i].Name);
                Win32.AppendMenu(menu, Win32.MF_POPUP, (UIntPtr)favMenu, Loc.T("Menu.Favorites"));
            }

            Win32.AppendMenu(menu, Win32.MF_SEPARATOR, 0, null);
            Win32.AppendMenu(menu, Win32.MF_STRING, CMD_EXIT, Loc.T("Tray.Exit"));

            // Required for correct menu dismissal: bring the owner window forward, then post WM_NULL.
            Win32.GetCursorPos(out var point);
            Win32.SetForegroundWindow(hwnd);
            var command = Win32.TrackPopupMenu(menu, Win32.TPM_RETURNCMD, point.X, point.Y, 0, hwnd, IntPtr.Zero);
            Win32.PostMessage(hwnd, Win32.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case CMD_SHOW: ShowWindowRequested?.Invoke(); break;
                case CMD_PLAYPAUSE: PlayPauseRequested?.Invoke(); break;
                case CMD_STOP: StopRequested?.Invoke(); break;
                case CMD_NEXT: NextRequested?.Invoke(); break;
                case CMD_PREV: PreviousRequested?.Invoke(); break;
                case CMD_RECORD: RecordRequested?.Invoke(); break;
                case CMD_MUTE: MuteRequested?.Invoke(); break;
                case CMD_VOLUP: VolumeDelta?.Invoke(5); break;
                case CMD_VOLDOWN: VolumeDelta?.Invoke(-5); break;
                case CMD_EXIT: ExitRequested?.Invoke(); break;
                default:
                    if (command >= CMD_FAV_BASE && favorites is not null)
                    {
                        var index = command - CMD_FAV_BASE;
                        if (index < favorites.Count) FavoriteChosen?.Invoke(favorites[index].Id);
                    }
                    break;
            }
        }
        finally
        {
            Win32.DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = _data;
            Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero)
        {
            Win32.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
        _source?.Dispose();
        _source = null;
    }
}
