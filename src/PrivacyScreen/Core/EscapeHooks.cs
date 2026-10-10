using PrivacyScreen.Native;

namespace PrivacyScreen.Core;

/// <summary>
/// 键盘失灵时的“纯鼠标逃生”与“Esc 逃生”（PRD FR-11 第 4/5 级保险）。
///
/// 实现方式：安装两个 Windows 低级钩子（Low Level Hook）。
/// 钩子只“看一眼”按键，随即原样放行（返回 CallNextHookEx），不拦截、不修改，
/// 因此不会影响任何正常软件的输入，也不会被系统当成恶意软件处理。
/// </summary>
internal sealed class EscapeHooks : IDisposable
{
    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _keyboardHook = IntPtr.Zero;

    // 必须用字段保存委托引用，否则会被 GC 回收导致钩子失效（经典坑）
    private Win32.LowLevelProc? _mouseProc;
    private Win32.LowLevelProc? _keyboardProc;

    private DateTime _lastLeftDown = DateTime.MinValue;
    private DateTime _lastEscDown = DateTime.MinValue;

    private const int DoubleClickWindowMs = 500;
    private const int DoubleEscapeWindowMs = 800;

    /// <summary>是否处于“武装”状态；只有黑屏时才返回 true。</summary>
    public Func<bool>? IsArmed { get; set; }

    /// <summary>快捷退出开关。</summary>
    public bool MouseDoubleClickEnabled { get; set; } = true;
    public bool DoubleEscapeEnabled { get; set; } = true;

    /// <summary>在黑屏期间连续双击鼠标左键。</summary>
    public event Action? MouseDoubleClicked;

    /// <summary>在黑屏期间快速连按两次 Esc。</summary>
    public event Action? DoubleEscapePressed;

    public bool Installed => _mouseHook != IntPtr.Zero || _keyboardHook != IntPtr.Zero;

    public void Install()
    {
        if (_mouseHook == IntPtr.Zero && MouseDoubleClickEnabled)
        {
            _mouseProc = MouseProc;
            _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
        }

        if (_keyboardHook == IntPtr.Zero && DoubleEscapeEnabled)
        {
            _keyboardProc = KeyboardProc;
            _keyboardHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _keyboardProc, IntPtr.Zero, 0);
        }
    }

    public void Uninstall()
    {
        if (_mouseHook != IntPtr.Zero) { Win32.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        if (_keyboardHook != IntPtr.Zero) { Win32.UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
        _mouseProc = null;
        _keyboardProc = null;
    }

    public void Dispose() => Uninstall();

    private bool Armed => IsArmed?.Invoke() ?? false;

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && Armed && MouseDoubleClickEnabled && wParam.ToInt32() == Win32.WM_LBUTTONDOWN)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastLeftDown).TotalMilliseconds <= DoubleClickWindowMs)
                {
                    _lastLeftDown = DateTime.MinValue;   // 重置，避免连点被算作多次
                    RaiseAsync(MouseDoubleClicked);
                }
                else
                {
                    _lastLeftDown = now;
                }
            }
        }
        catch
        {
            // 钩子回调里绝不能抛异常
        }
        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && Armed && DoubleEscapeEnabled)
            {
                int msg = wParam.ToInt32();
                if (msg == Win32.WM_KEYDOWN || msg == Win32.WM_SYSKEYDOWN)
                {
                    var data = System.Runtime.InteropServices.Marshal
                        .PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                    if (data.vkCode == Win32.VK_ESCAPE)
                    {
                        var now = DateTime.UtcNow;
                        if ((now - _lastEscDown).TotalMilliseconds <= DoubleEscapeWindowMs)
                        {
                            _lastEscDown = DateTime.MinValue;
                            RaiseAsync(DoubleEscapePressed);
                        }
                        else
                        {
                            _lastEscDown = now;
                        }
                    }
                }
            }
        }
        catch
        {
            // 同上
        }
        return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    /// <summary>把回调推迟到消息队列，避免在钩子回调里做重活（做重活会被系统摘钩子）。</summary>
    private static void RaiseAsync(Action? handler)
    {
        if (handler is null) return;
        var sync = SynchronizationContext.Current;
        if (sync is not null) sync.Post(_ => handler(), null);
        else ThreadPool.QueueUserWorkItem(_ => handler());
    }
}
