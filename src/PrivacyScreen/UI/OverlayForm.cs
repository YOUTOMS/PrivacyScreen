using PrivacyScreen.Native;

namespace PrivacyScreen.UI;

/// <summary>
/// 一块显示器上的黑色遮罩窗。
///
/// 三个关键特性：
/// 1) 无边框 + 置顶 + 纯黑 → 看起来就像显示器关掉了；
/// 2) WS_EX_TRANSPARENT + WM_NCHITTEST 返回 HTTRANSPARENT → **鼠标穿透**，
///    你在本地或远程点的鼠标都会落到真正的窗口上，因此不影响电脑正常使用；
/// 3) SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) → **截图/录屏拍不到它**，
///    远程端因此能看到正常桌面（这就是“远程看正常、仅本地黑”的实现核心）。
/// </summary>
internal sealed class OverlayForm : Form
{
    private readonly bool _useExcludeFromCapture;

    /// <summary>捕获策略是否设置成功。false 表示已降级为“远程也黑”。</summary>
    public bool CaptureExclusionApplied { get; private set; }

    public bool CaptureExclusionSupported { get; private set; }

    public OverlayForm(Rectangle bounds, bool useExcludeFromCapture = true)
    {
        _useExcludeFromCapture = useExcludeFromCapture;
        AutoScaleMode = AutoScaleMode.None;   // 用物理像素，避免被 DPI 二次缩放
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        ForeColor = Color.Black;
        Bounds = bounds;
        Text = "PrivacyScreen-Overlay";
        DoubleBuffered = true;
        SetStyle(ControlStyles.Opaque, true); // 不做多余背景重绘，避免闪烁
    }

    /// <summary>Show 的时候不要抢焦点，键盘仍归原本的窗口管。</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32.WS_EX_TOOLWINDOW   // 不出现在 Alt+Tab / 任务栏
                        | Win32.WS_EX_NOACTIVATE   // 不抢焦点
                        | Win32.WS_EX_TRANSPARENT; // 鼠标穿透
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        // 双保险：明确告诉系统“这一点不属于我”，鼠标消息继续往下传
        if (m.Msg == Win32.WM_NCHITTEST)
        {
            m.Result = (IntPtr)Win32.HTTRANSPARENT;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyCapturePolicy();
    }

    /// <summary>应用“捕获排除”。返回 true 表示期望的排除模式生效。</summary>
    public bool ApplyCapturePolicy()
    {
        // 用户关掉了“兼容远程”开关：老老实实参与捕获（远程也黑）
        if (!_useExcludeFromCapture)
        {
            Win32.SetWindowDisplayAffinity(Handle, Win32.WDA_NONE);
            CaptureExclusionApplied = false;
            CaptureExclusionSupported = true;
            return false;
        }

        // 首选：从截屏/录屏里排除（远程能看到正常桌面）
        if (Win32.SetWindowDisplayAffinity(Handle, Win32.WDA_EXCLUDEFROMCAPTURE))
        {
            CaptureExclusionApplied = true;
            CaptureExclusionSupported = true;
            return true;
        }

        // 降级：旧系统只支持“捕获时显示为黑色”，此时远程和本地都会看到黑
        CaptureExclusionSupported = false;
        CaptureExclusionApplied = Win32.SetWindowDisplayAffinity(Handle, Win32.WDA_MONITOR);
        return false;
    }

    /// <summary>把窗口重新顶到最上层（防止被后开的窗口盖住）。</summary>
    public void KeepOnTop()
    {
        if (!IsHandleCreated) return;
        Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }

    /// <summary>显示器分辨率/位置变化后重新贴合。</summary>
    public void ApplyBounds(Rectangle bounds) => Bounds = bounds;
}
