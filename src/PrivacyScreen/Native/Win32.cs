using System.Runtime.InteropServices;

namespace PrivacyScreen.Native;

/// <summary>
/// 所有对 Windows 系统 API 的调用集中在这里。
/// 说明：这些是 Windows 官方公开的接口（user32.dll / kernel32.dll），
/// 不是“外挂”或“黑科技”，只是 C# 默认没包装，需要手动声明一下。
/// </summary>
internal static class Win32
{
    // ───────────────────────── 窗口扩展样式 ─────────────────────────
    public const int GWL_EXSTYLE = -20;

    /// <summary>鼠标穿透：点到这个窗口时视为点到它下面的窗口。</summary>
    public const int WS_EX_TRANSPARENT = 0x00000020;
    /// <summary>工具窗口：不出现在 Alt+Tab 和任务栏里。</summary>
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    /// <summary>不激活：显示时不抢焦点（黑屏时键盘仍归原窗口管）。</summary>
    public const int WS_EX_NOACTIVATE = 0x08000000;

    // ───────────────────────── 窗口消息 ─────────────────────────
    public const int WM_NCHITTEST = 0x0084;
    public const int HTTRANSPARENT = -1;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_DISPLAYCHANGE = 0x007E;

    // ───────────────────────── 捕获排除（远程穿透的核心）─────────────────────────
    /// <summary>不参与捕获排除（普通窗口）。</summary>
    public const uint WDA_NONE = 0x00000000;
    /// <summary>旧系统的降级方案：被截屏/录屏时显示为黑色（远程也会黑）。</summary>
    public const uint WDA_MONITOR = 0x00000001;
    /// <summary>★核心：把窗口从截屏/录屏中“抠掉”，截图拿到的是窗口后面的正常画面。
    /// 需要 Windows 10 版本 2004（内部版本 19041）及以上。</summary>
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    // ───────────────────────── 窗口置顶 ─────────────────────────
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    // ───────────────────────── 全局快捷键 ─────────────────────────
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;   // 按住不放只触发一次

    // ───────────────────────── 系统光标 ─────────────────────────
    public const uint SPI_SETCURSORS = 0x0057;
    public const uint SPIF_SENDCHANGE = 0x02;
    /// <summary>标准箭头光标（我们要替换掉的就是它）。</summary>
    public const uint OCR_NORMAL = 32512;

    // ───────────────────────── 钩子 ─────────────────────────
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int VK_ESCAPE = 0x1B;

    public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    // ───────────────────────── P/Invoke 声明 ─────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>关键 API：设置窗口是否参与屏幕捕获。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    /// <summary>取得窗口当前的捕获策略；返回 false 表示系统不支持。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint pdwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>把某个系统光标替换成指定光标（全局生效，只影响当前用户会话）。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetSystemCursor(IntPtr hcur, uint id);

    /// <summary>创建一个单色光标（宽度/高度至少 1×1）。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot,
        int nWidth, int nHeight, byte[] pvANDPlane, byte[] pvXORPlane);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyCursor(IntPtr hCursor);

    /// <summary>用于恢复系统默认光标（SPI_SETCURSORS）。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    public static extern IntPtr GetCursor();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    // ───────────────────────── 结构体 ─────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
