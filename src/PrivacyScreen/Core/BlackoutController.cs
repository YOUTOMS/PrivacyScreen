using PrivacyScreen.UI;

namespace PrivacyScreen.Core;

/// <summary>
/// 黑屏总控：负责创建/销毁各显示器的遮罩、维持置顶、隐藏光标、应对显示器变化。
/// </summary>
internal sealed class BlackoutController : IDisposable
{
    private readonly List<(OverlayForm Form, string MonitorId)> _overlays = new();
    private readonly System.Windows.Forms.Timer _topMostTimer;
    private bool _cursorHidden;
    private bool _topMostSuspended;

    /// <summary>当前是否处于黑屏状态。</summary>
    public bool IsActive => _overlays.Count > 0;

    /// <summary>
    /// 暂停/恢复“反复置顶”。打开托盘菜单时必须暂停，
    /// 否则遮罩会把菜单抢到下面去，用户就看不见菜单了。
    /// </summary>
    public void SuspendTopMost(bool suspend) => _topMostSuspended = suspend;

    /// <summary>当前正在被黑的显示器标识。</summary>
    public IReadOnlyList<string> ActiveMonitorIds => _overlays.Select(o => o.MonitorId).ToList();

    /// <summary>捕获排除是否真的生效；false = 已降级，远程也会看到黑屏。</summary>
    public bool CaptureExclusionOk { get; private set; } = true;

    /// <summary>当前系统是否支持“捕获排除”能力（Win10 2004+）。</summary>
    public bool CaptureApiSupported { get; private set; } = true;

    /// <summary>黑屏结束（非重启切换）时触发。</summary>
    public event Action? Exited;

    public BlackoutController()
    {
        // 每隔 1.5 秒把遮罩重新顶到最上层，防止被后开的窗口盖住
        _topMostTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _topMostTimer.Tick += (_, _) =>
        {
            if (_topMostSuspended) return;
            foreach (var (form, _) in _overlays) form.KeepOnTop();
        };
    }

    /// <summary>开始黑屏（会先清掉上一次的遮罩）。</summary>
    public void Enter(IReadOnlyList<MonitorItem> targets, bool useExcludeFromCapture, bool hideCursorWhenAllBlack)
    {
        ExitCore(silent: true);
        if (targets.Count == 0) return;

        CaptureExclusionOk = true;
        CaptureApiSupported = true;

        foreach (var m in targets)
        {
            var form = new OverlayForm(m.Bounds, useExcludeFromCapture);
            form.Show();
            form.KeepOnTop();
            _overlays.Add((form, m.Id));

            if (!form.CaptureExclusionSupported) CaptureApiSupported = false;
            if (!form.CaptureExclusionApplied) CaptureExclusionOk = false;
        }

        _topMostTimer.Start();

        // 只有“所有显示器都被黑”时才隐藏鼠标：
        // 若还有一块屏没黑（你还要在那块屏上正常工作），全局隐藏光标会连正常屏一起影响，得不偿失。
        var all = MonitorHelper.GetMonitors();
        bool allBlack = _overlays.Count >= all.Count;
        if (hideCursorWhenAllBlack && allBlack)
        {
            _cursorHidden = CursorHider.Hide();
        }
    }

    /// <summary>结束黑屏。</summary>
    public void Exit() => ExitCore(silent: false);

    private void ExitCore(bool silent)
    {
        _topMostTimer.Stop();

        bool wasActive = _overlays.Count > 0;

        foreach (var (form, _) in _overlays)
        {
            try
            {
                form.Hide();
                form.Close();
                form.Dispose();
            }
            catch
            {
                // 单个窗口失败不影响整体退出
            }
        }
        _overlays.Clear();

        if (_cursorHidden)
        {
            CursorHider.Restore();
            _cursorHidden = false;
        }

        _topMostSuspended = false;

        if (wasActive && !silent) Exited?.Invoke();
    }

    /// <summary>
    /// 显示器配置变化后重新贴合（对应 PRD EF-12）：
    /// 还在的屏保留并贴合新位置，拔掉的屏移掉遮罩。
    /// </summary>
    public void RefreshAfterDisplayChange()
    {
        if (!IsActive) return;

        var current = MonitorHelper.GetMonitors();
        var keep = new List<(OverlayForm, string)>();

        foreach (var (form, id) in _overlays)
        {
            var m = current.FirstOrDefault(x => x.Id == id);
            if (m is null)
            {
                try { form.Close(); form.Dispose(); } catch { /* 忽略 */ }
            }
            else
            {
                form.ApplyBounds(m.Bounds);
                form.KeepOnTop();
                keep.Add((form, id));
            }
        }

        _overlays.Clear();
        _overlays.AddRange(keep);

        if (_overlays.Count == 0) Exit();
    }

    public void Dispose()
    {
        ExitCore(silent: true);
        _topMostTimer.Dispose();
    }
}
