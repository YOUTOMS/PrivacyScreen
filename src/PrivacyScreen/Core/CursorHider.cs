using PrivacyScreen.Native;

namespace PrivacyScreen.Core;

/// <summary>
/// 隐藏/恢复鼠标指针。
///
/// 原理：把系统“标准箭头光标”临时替换成一个 1×1 的纯黑小点。
/// 在全黑屏幕上，一个 1 像素的黑点等于完全看不见，视觉效果就是“没有鼠标”。
/// 退出黑屏时用 SPI_SETCURSORS 让 Windows 从系统设置里重新载入默认光标。
///
/// 安全兜底：程序每次启动时会先无条件恢复一次，防止上次异常退出留下“无光标”状态。
/// </summary>
internal static class CursorHider
{
    private static bool _hidden;

    public static bool IsHidden => _hidden;

    public static bool Hide()
    {
        if (_hidden) return true;
        try
        {
            // 单色光标：AND 平面与 XOR 平面各 1 字节。全 0 → 一个极小的黑点。
            byte[] andPlane = { 0x00 };
            byte[] xorPlane = { 0x00 };

            IntPtr hCur = Win32.CreateCursor(IntPtr.Zero, 0, 0, 1, 1, andPlane, xorPlane);
            if (hCur == IntPtr.Zero) return false;

            // 注意：SetSystemCursor 成功后由系统接管并销毁该句柄，不能再 DestroyCursor。
            if (Win32.SetSystemCursor(hCur, Win32.OCR_NORMAL))
            {
                _hidden = true;
                return true;
            }

            Win32.DestroyCursor(hCur);
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>恢复系统默认光标。任何时候调用都安全。</summary>
    public static void Restore()
    {
        try
        {
            Win32.SystemParametersInfo(Win32.SPI_SETCURSORS, 0, IntPtr.Zero, Win32.SPIF_SENDCHANGE);
        }
        catch
        {
            // 忽略
        }
        _hidden = false;
    }
}
