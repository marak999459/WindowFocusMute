using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowFocusMute;

internal static class Win32
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    private static extern bool IsWindowVisibleNative(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int attrValue, int attrSize);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_APPWINDOW = 0x00040000L;
    private const uint GW_OWNER = 4;
    private const int DWMWA_CLOAKED = 14;

    /// <summary>返回当前前台窗口所属的进程号；取不到时返回 0。</summary>
    public static uint GetForegroundProcessId()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        return GetWindowThreadProcessId(hwnd, out uint pid) != 0 ? pid : 0;
    }

    /// <summary>窗口是否可见。</summary>
    public static bool IsWindowVisible(IntPtr hWnd) => hWnd != IntPtr.Zero && IsWindowVisibleNative(hWnd);

    private static bool IsTaskbarWindow(IntPtr hwnd)
    {
        if (!IsWindowVisibleNative(hwnd)) return false;

        // 窗口标题为空的一般不是任务栏窗口
        int len = GetWindowText(hwnd, out string title);
        if (len == 0 || string.IsNullOrWhiteSpace(title)) return false;

        long exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        // DWM 遮蔽的窗口（UWP 挂起/虚拟桌面外等）不进任务栏
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0)
            return false;

        // APPWINDOW 强制显示；TOOLWINDOW 强制不显示；其余有属主的窗口不显示
        if ((exStyle & WS_EX_APPWINDOW) != 0) return true;
        if ((exStyle & WS_EX_TOOLWINDOW) != 0) return false;
        if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;

        return true;
    }

    private static int GetWindowText(IntPtr hWnd, out string text)
    {
        var sb = new StringBuilder(256);
        int len = GetWindowText(hWnd, sb, sb.Capacity);
        text = sb.ToString();
        return len;
    }

    /// <summary>类似任务栏的可见顶层窗口清单：pid -> 窗口句柄与标题。同进程取枚举到的第一个。</summary>
    public static Dictionary<uint, (IntPtr Hwnd, string Title)> GetTaskbarWindows()
    {
        var result = new Dictionary<uint, (IntPtr, string)>();
        EnumWindows((hwnd, _) =>
        {
            if (IsTaskbarWindow(hwnd) && GetWindowThreadProcessId(hwnd, out uint pid) != 0 && pid != 0)
                result.TryAdd(pid, (hwnd, GetWindowText(hwnd, out string t) >= 0 ? t : string.Empty));
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
