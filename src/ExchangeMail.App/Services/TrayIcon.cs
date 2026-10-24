using System.Runtime.InteropServices;

namespace ExchangeMail.App.Services;

/// <summary>An entry of the tray icon's context menu; <see cref="Separator"/> draws a line.</summary>
public sealed record TrayMenuItem(string Text, Action? Action, bool Enabled = true, bool IsDefault = false)
{
    public static TrayMenuItem Separator { get; } = new("-", null);
}

/// <summary>
/// Notification-area (system tray) icon. WinUI has no tray API, so this uses Shell_NotifyIcon with a hidden
/// Win32 window created on the UI thread to receive the icon's mouse messages.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string WindowClass = "PostaciTrayWindow";
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1

    private readonly WndProc _wndProc; // kept alive: the native window calls into it
    private readonly IntPtr _hwnd;
    private readonly uint _taskbarCreatedMessage;
    private readonly IntPtr _normalIcon;
    private readonly IntPtr _unreadIcon;
    private bool _showUnread;
    private string _tooltip;
    private bool _added;
    private bool _disposed;

    public TrayIcon(string iconPath, string unreadIconPath, string tooltip)
    {
        _wndProc = WindowProc;
        _tooltip = tooltip;

        var instance = GetModuleHandleW(null);
        var windowClass = new WNDCLASSEXW
        {
            cbSize = Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = WindowClass,
        };
        RegisterClassExW(ref windowClass); // fails harmlessly if already registered
        _hwnd = CreateWindowExW(WS_EX_TOOLWINDOW, WindowClass, ViewModels.AppInfo.Name, WS_POPUP, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException($"Could not create the tray window (error {Marshal.GetLastWin32Error()}).");

        // Explorer broadcasts this after it restarts; the icon must then be added again.
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        _normalIcon = LoadSmallIcon(iconPath);
        _unreadIcon = LoadSmallIcon(unreadIconPath);
        Add();
    }

    /// <summary>Left click on the icon.</summary>
    public event EventHandler? Clicked;

    /// <summary>Builds the context menu each time it opens, so it reflects the current state.</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? MenuProvider { get; set; }

    /// <summary>Switches between the normal and the "unread mail" icon and updates the tooltip.</summary>
    public void Update(bool showUnread, string tooltip)
    {
        if (_showUnread == showUnread && _tooltip == tooltip) return;
        _showUnread = showUnread;
        _tooltip = tooltip;
        if (!_added) return;
        var data = Data(NIF_ICON | NIF_TIP);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private void Add()
    {
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        _added = Shell_NotifyIconW(NIM_ADD, ref data);
    }

    private NOTIFYICONDATAW Data(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _showUnread && _unreadIcon != IntPtr.Zero ? _unreadIcon : _normalIcon,
        szTip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == CallbackMessage)
        {
            switch ((uint)(lParam.ToInt64() & 0xFFFF))
            {
                case WM_LBUTTONUP:
                    Clicked?.Invoke(this, EventArgs.Empty);
                    break;
                case WM_RBUTTONUP:
                case WM_CONTEXTMENU:
                    ShowMenu();
                    break;
            }
            return IntPtr.Zero;
        }
        if (message == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            Add();
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var items = MenuProvider?.Invoke();
        if (items is null || items.Count == 0) return;

        var menu = CreatePopupMenu();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (ReferenceEquals(item, TrayMenuItem.Separator)) AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                else AppendMenuW(menu, MF_STRING | (item.Enabled ? 0 : MF_GRAYED), (UIntPtr)(uint)(i + 1), item.Text);
                if (item.IsDefault) SetMenuDefaultItem(menu, (uint)(i + 1), 0);
            }

            // The owner must be the foreground window, or the menu would not close when clicking elsewhere.
            GetCursorPos(out var point);
            SetForegroundWindow(_hwnd);
            var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, point.X, point.Y, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);

            if (command > 0 && command <= items.Count) items[command - 1].Action?.Invoke();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static IntPtr LoadSmallIcon(string path) =>
        LoadImageW(IntPtr.Zero, path, IMAGE_ICON, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_added)
        {
            var data = Data(0);
            Shell_NotifyIconW(NIM_DELETE, ref data);
        }
        if (_normalIcon != IntPtr.Zero) DestroyIcon(_normalIcon);
        if (_unreadIcon != IntPtr.Zero) DestroyIcon(_unreadIcon);
        DestroyWindow(_hwnd);
    }

    #region Win32

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4;
    private const uint WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B;
    private const uint WS_POPUP = 0x80000000, WS_EX_TOOLWINDOW = 0x00000080;
    private const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    private const int SM_CXSMICON = 49, SM_CYSMICON = 50;

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    #endregion
}
