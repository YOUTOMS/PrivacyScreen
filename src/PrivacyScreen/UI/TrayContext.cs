using System.Diagnostics;
using Microsoft.Win32;
using PrivacyScreen.Core;

namespace PrivacyScreen.UI;

/// <summary>
/// 程序主控：托盘图标 + 快捷键 + 逃生钩子 + 黑屏总控，把它们串起来。
/// 本程序没有主窗口，一切通过右下角托盘图标操作。
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray = new();
    private readonly BlackoutController _blackout = new();
    private readonly HotKeyService _hotKeys = new();
    private readonly EscapeHooks _hooks = new();

    // 用于把后台线程（快捷键线程）的调用安全地丢回 UI 线程
    private readonly SynchronizationContext _ui = new WindowsFormsSynchronizationContext();

    private readonly AppSettings _settings;
    private bool _exiting;

    private readonly ToolStripMenuItem _statusItem = new("隐私屏");
    private readonly ToolStripMenuItem _toggleItem = new("开始黑屏");
    private readonly ToolStripMenuItem _scopeMenu = new("黑屏范围");
    private readonly ToolStripMenuItem _captureItem = new("兼容远程（本地黑、远程正常）");
    private readonly ToolStripMenuItem _cursorItem = new("全黑时隐藏鼠标");
    private readonly ToolStripMenuItem _askItem = new("每次启动都询问黑屏范围");
    private readonly ToolStripMenuItem _hotkeyItem = new("快捷键设置：");

    public TrayContext()
    {
        _settings = AppSettings.Load();

        // 启动兜底：万一上次异常退出留下了“无鼠标指针”，这里立刻修复
        CursorHider.Restore();

        BuildTrayMenu();
        StartHotKeys();
        StartEscapeHooks();

        _blackout.Exited += () =>
        {
            UpdateTrayState();
            Notify("隐私屏已退出", "屏幕已恢复正常。按快捷键可随时再次黑屏。");
        };

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // 等消息循环跑起来再动手（启动更稳）
        var t = new System.Windows.Forms.Timer { Interval = 300 };
        t.Tick += (_, _) =>
        {
            t.Stop();
            t.Dispose();
            BeginStartupFlow();
        };
        t.Start();

        UpdateTrayState();
    }

    // ───────────────────────── 托盘 ─────────────────────────

    private void BuildTrayMenu()
    {
        _tray.Icon = TrayIconFactory.CreateAppIcon();
        _tray.Text = "隐私屏";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ToggleBlackout();

        var menu = new ContextMenuStrip();

        _statusItem.Enabled = false;
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());

        _toggleItem.Click += (_, _) => ToggleBlackout();
        menu.Items.Add(_toggleItem);

        _scopeMenu.DropDownOpening += (_, _) => RebuildScopeMenu();
        menu.Items.Add(_scopeMenu);

        menu.Items.Add(new ToolStripSeparator());

        _captureItem.CheckOnClick = true;
        _captureItem.Checked = _settings.UseExcludeFromCapture;
        _captureItem.CheckedChanged += (_, _) =>
        {
            _settings.UseExcludeFromCapture = _captureItem.Checked;
            _settings.Save();
            if (_blackout.IsActive)
            {
                // 立刻按新设置重建遮罩
                ReapplyCurrentScope();
            }
        };
        menu.Items.Add(_captureItem);

        _cursorItem.CheckOnClick = true;
        _cursorItem.Checked = _settings.HideCursorWhenAllBlack;
        _cursorItem.CheckedChanged += (_, _) =>
        {
            _settings.HideCursorWhenAllBlack = _cursorItem.Checked;
            _settings.Save();
        };
        menu.Items.Add(_cursorItem);

        _askItem.CheckOnClick = true;
        _askItem.Checked = _settings.ScopePolicy == ScopePolicy.Ask;
        _askItem.CheckedChanged += (_, _) =>
        {
            _settings.ScopePolicy = _askItem.Checked ? ScopePolicy.Ask : ScopePolicy.Remember;
            _settings.Save();
        };
        menu.Items.Add(_askItem);

        menu.Items.Add(new ToolStripSeparator());

        var pick = new ToolStripMenuItem("选择黑屏范围…", null, (_, _) => ShowScopeDialog());
        menu.Items.Add(pick);

        var resetCursor = new ToolStripMenuItem("恢复鼠标指针", null, (_, _) =>
        {
            CursorHider.Restore();
            Notify("已恢复", "系统鼠标指针已恢复为默认样式。");
        });
        menu.Items.Add(resetCursor);

        _hotkeyItem.Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_hotkeyItem);

        var hotkeySettings = new ToolStripMenuItem("快捷键设置…", null, (_, _) => ShowHotkeyDialog());
        menu.Items.Add(hotkeySettings);

        var openCfg = new ToolStripMenuItem("打开配置文件夹", null, (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(AppSettings.Folder);
                Process.Start(new ProcessStartInfo("explorer.exe", AppSettings.Folder) { UseShellExecute = true });
            }
            catch { /* 忽略 */ }
        });
        menu.Items.Add(openCfg);

        menu.Items.Add(new ToolStripSeparator());

        var quit = new ToolStripMenuItem("退出程序", null, (_, _) => ExitApp());
        menu.Items.Add(quit);

        // 菜单打开期间暂停遮罩置顶，保证菜单始终看得见；菜单关闭后恢复
        menu.Opening += (_, _) => _blackout.SuspendTopMost(true);
        menu.Closed += (_, _) => _blackout.SuspendTopMost(false);

        _tray.ContextMenuStrip = menu;
    }

    private void RebuildScopeMenu()
    {
        _scopeMenu.DropDownItems.Clear();

        var monitors = MonitorHelper.GetMonitors();
        var active = _blackout.ActiveMonitorIds;

        foreach (var m in monitors)
        {
            var item = new ToolStripMenuItem(m.Title)
            {
                CheckOnClick = false,
                Checked = active.Contains(m.Id)
            };
            var captured = m;
            item.Click += (_, _) => ToggleMonitor(captured);
            _scopeMenu.DropDownItems.Add(item);
        }

        _scopeMenu.DropDownItems.Add(new ToolStripSeparator());

        var all = new ToolStripMenuItem("全部显示器");
        all.Click += (_, _) => EnterBlackout(MonitorHelper.GetMonitors());
        _scopeMenu.DropDownItems.Add(all);

        var exit = new ToolStripMenuItem("退出黑屏");
        exit.Click += (_, _) => ExitBlackout();
        _scopeMenu.DropDownItems.Add(exit);
    }

    private void ToggleMonitor(MonitorItem m)
    {
        var monitors = MonitorHelper.GetMonitors();

        // 未黑屏时，点某一块 = 只黑这一块
        if (!_blackout.IsActive)
        {
            EnterBlackout(new[] { m });
            return;
        }

        var active = _blackout.ActiveMonitorIds.ToList();

        if (active.Contains(m.Id))
        {
            // FR-14：从黑屏集合里去掉这一块（不闪回正常桌面，直接重建）
            active.Remove(m.Id);
        }
        else
        {
            active.Add(m.Id);
        }

        var targets = MonitorHelper.Resolve(active, monitors);

        if (targets.Count == 0)
        {
            ExitBlackout();
        }
        else
        {
            EnterBlackout(targets);
        }
    }

    private void UpdateTrayState()
    {
        bool active = _blackout.IsActive;

        _statusItem.Text = active ? "状态：黑屏中" : "状态：待命";
        _toggleItem.Text = active ? "退出黑屏" : "开始黑屏";
        _tray.Text = active ? "隐私屏 · 黑屏中" : "隐私屏 · 待命";
        _hotkeyItem.Text = $"快捷键：{_settings.ExitHotkey} 退出 / {_settings.EnterHotkey} 进入";
    }

    private void Notify(string title, string text)
    {
        try
        {
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = text;
            _tray.BalloonTipIcon = ToolTipIcon.Info;
            _tray.ShowBalloonTip(2500);
        }
        catch { /* 忽略 */ }
    }

    // ───────────────────────── 黑屏流程 ─────────────────────────

    private void BeginStartupFlow()
    {
        var monitors = MonitorHelper.GetMonitors();

        if (_settings.ScopePolicy == ScopePolicy.Ask)
        {
            ShowScopeDialog();
            return;
        }

        var targets = MonitorHelper.Resolve(_settings.RememberedMonitorIds, monitors);
        if (targets.Count == 0) targets = monitors;   // 记录失效 → 全黑兜底
        EnterBlackout(targets);
    }

    private void ShowScopeDialog()
    {
        var monitors = MonitorHelper.GetMonitors();
        if (monitors.Count == 0) return;

        // 如果当前正在黑屏，先撤掉遮罩：
        // 否则遮罩是置顶的，会把范围面板盖住（这里是个必须处理的交互坑）
        bool wasActive = _blackout.IsActive;
        var previous = _blackout.ActiveMonitorIds.ToList();
        if (wasActive)
        {
            _blackout.Exit();
            UpdateTrayState();
        }

        using var dlg = new ScopeForm(monitors, _settings.RememberedMonitorIds);
        var result = dlg.ShowDialog();

        if (result != DialogResult.OK || dlg.SelectedMonitorIds.Count == 0)
        {
            if (wasActive)
            {
                // 取消时，恢复到进来之前的黑屏范围
                var restore = MonitorHelper.Resolve(previous, monitors);
                if (restore.Count > 0) EnterBlackout(restore);
            }
            else
            {
                Notify("已取消黑屏", $"程序在托盘待命。按 {_settings.EnterHotkey} 或双击托盘图标可随时黑屏。");
            }
            return;
        }

        _settings.RememberedMonitorIds = dlg.SelectedMonitorIds.ToList();
        if (dlg.RememberChoice)
        {
            _settings.ScopePolicy = ScopePolicy.Remember;
            _askItem.Checked = false;
        }
        _settings.Save();

        var targets = MonitorHelper.Resolve(dlg.SelectedMonitorIds, monitors);
        EnterBlackout(targets);
    }

    /// <summary>快捷键可视化设置（对应 PRD FR-04）。</summary>
    private void ShowHotkeyDialog()
    {
        // 遮罩是置顶的，会把设置窗口盖住；而且黑屏时“双击鼠标左键退出”的钩子会把
        // 对话框里的点击也算进去，容易误触发。所以改键前先退出黑屏
        // （反正要看见屏幕才能操作，这本来也是用户的预期）。
        bool wasActive = _blackout.IsActive;
        if (wasActive)
        {
            _blackout.Exit();
            UpdateTrayState();
        }

        using var dlg = new HotkeySettingsForm(
            _settings.ExitHotkey, _settings.BackupExitHotkey, _settings.EnterHotkey);

        if (dlg.ShowDialog() != DialogResult.OK)
        {
            if (wasActive) Notify("快捷键未改动", $"屏幕已恢复正常。按 {_settings.EnterHotkey} 可再次黑屏。");
            return;
        }

        _settings.ExitHotkey = dlg.ExitHotkey;
        _settings.BackupExitHotkey = dlg.BackupExitHotkey;
        _settings.EnterHotkey = dlg.EnterHotkey;
        _settings.Save();

        StartHotKeys();     // 立即按新键重新注册（FR-04：保存后立即生效）
        UpdateTrayState();

        Notify("快捷键已更新",
            $"退出 {_settings.ExitHotkey} / 备用 {_settings.BackupExitHotkey} / 进入 {_settings.EnterHotkey}。已立即生效并保存。");
    }

    private void EnterBlackout(IReadOnlyList<MonitorItem> targets)
    {
        if (targets.Count == 0) return;

        _settings.RememberedMonitorIds = targets.Select(t => t.Id).ToList();
        _settings.Save();

        _blackout.Enter(targets, _settings.UseExcludeFromCapture, _settings.HideCursorWhenAllBlack);
        UpdateTrayState();

        if (_settings.UseExcludeFromCapture && !_blackout.CaptureApiSupported)
        {
            Notify("兼容提示", "当前系统版本不支持“捕获排除”，远程端也会看到黑屏。建议升级到 Windows 10 2004 或更高版本。");
        }

        // 首次进入黑屏时，给一次“怎么退出”的提示（气泡会浮在遮罩之上，不影响黑屏效果）
        if (!_settings.HasShownFirstTimeTip)
        {
            _settings.HasShownFirstTimeTip = true;
            _settings.Save();
            var extra = _settings.MouseDoubleClickExit ? "，或连续双击鼠标左键" : "";
            Notify("已进入黑屏", $"按 {_settings.ExitHotkey} 退出{extra}。托盘菜单里也能退出。");
        }
    }

    private void ExitBlackout()
    {
        _blackout.Exit();
        UpdateTrayState();
    }

    private void ToggleBlackout()
    {
        if (_blackout.IsActive)
        {
            ExitBlackout();
        }
        else
        {
            var monitors = MonitorHelper.GetMonitors();
            var targets = MonitorHelper.Resolve(_settings.RememberedMonitorIds, monitors);
            if (targets.Count == 0) targets = monitors;
            EnterBlackout(targets);
        }
    }

    /// <summary>按当前记忆的范围重建遮罩（用于切换“兼容远程”设置后立即生效）。</summary>
    private void ReapplyCurrentScope()
    {
        var monitors = MonitorHelper.GetMonitors();
        var targets = MonitorHelper.Resolve(_blackout.ActiveMonitorIds, monitors);
        if (targets.Count == 0) targets = monitors;
        EnterBlackout(targets);
    }

    // ───────────────────────── 快捷键与逃生 ─────────────────────────

    private void StartHotKeys()
    {
        _hotKeys.Pressed += action => _ui.Post(_ =>
        {
            switch (action)
            {
                case "Enter":
                    if (!_blackout.IsActive) ToggleBlackout();
                    break;
                case "Exit":
                case "BackupExit":
                    if (_blackout.IsActive) ExitBlackout();
                    break;
            }
        }, null);

        _hotKeys.Start(new[]
        {
            ("Exit", _settings.ExitHotkey),
            ("BackupExit", _settings.BackupExitHotkey),
            ("Enter", _settings.EnterHotkey)
        });

        // 快捷键被占用时给出提示（但备用键仍在，不影响退出）
        if (_hotKeys.Errors.Count > 0)
        {
            var lines = string.Join("；", _hotKeys.Errors.Select(kv => $"{kv.Key}：{kv.Value}"));
            Notify("快捷键注册失败", lines + "。可改用托盘菜单或鼠标双击退出。");
        }
    }

    private void StartEscapeHooks()
    {
        _hooks.IsArmed = () => _blackout.IsActive;
        _hooks.MouseDoubleClickEnabled = _settings.MouseDoubleClickExit;
        _hooks.DoubleEscapeEnabled = _settings.DoubleEscapeExit;

        _hooks.MouseDoubleClicked += () => _ui.Post(_ =>
        {
            if (_blackout.IsActive) ExitBlackout();
        }, null);

        _hooks.DoubleEscapePressed += () => _ui.Post(_ =>
        {
            if (_blackout.IsActive) ExitBlackout();
        }, null);

        _hooks.Install();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _ui.Post(_ =>
        {
            if (_blackout.IsActive)
            {
                _blackout.RefreshAfterDisplayChange();
                UpdateTrayState();
            }
        }, null);
    }

    // ───────────────────────── 退出 ─────────────────────────

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;

        try { SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; } catch { }

        _hooks.Dispose();
        _hotKeys.Dispose();
        _blackout.Dispose();
        CursorHider.Restore();     // 退出前一定恢复鼠标

        _tray.Visible = false;
        _tray.Dispose();

        ExitThread();
    }
}
