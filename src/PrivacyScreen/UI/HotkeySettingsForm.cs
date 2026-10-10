using PrivacyScreen.Core;

namespace PrivacyScreen.UI;

/// <summary>
/// 快捷键可视化设置界面（对应 PRD FR-04）。
///
/// 设计要点：
/// 1. 三个“按键框”，点一下就能直接按下组合键录入，不用手写文本、也不用去改配置文件；
/// 2. 保存前做四道静态校验 + 一道真机试注册，任一条不过就**阻止保存**并指出是哪一项；
/// 3. 保存后由托盘主控立即重新注册（FR-04：立即生效 + 持久化）。
/// </summary>
internal sealed class HotkeySettingsForm : Form
{
    private static readonly Color Ink = Color.FromArgb(32, 38, 48);
    private static readonly Color Muted = Color.FromArgb(120, 130, 145);
    private static readonly Color Accent = Color.FromArgb(24, 30, 42);
    private static readonly Color Danger = Color.FromArgb(200, 44, 44);
    private static readonly Color PanelBg = Color.FromArgb(247, 249, 252);

    private const string DefaultExit = "Ctrl+Alt+B";
    private const string DefaultBackup = "Ctrl+Alt+N";
    private const string DefaultEnter = "Ctrl+Alt+M";

    private readonly HotkeyBox _exit = new();
    private readonly HotkeyBox _backup = new();
    private readonly HotkeyBox _enter = new();
    private readonly Label _error = new();

    /// <summary>进入本界面时的原值：用于跳过“本程序自己已注册的键”，避免误判为被占用。</summary>
    private readonly HashSet<string> _currentKeys;

    public string ExitHotkey => _exit.Value;
    public string BackupExitHotkey => _backup.Value;
    public string EnterHotkey => _enter.Value;

    public HotkeySettingsForm(string exitHotkey, string backupExitHotkey, string enterHotkey)
    {
        _currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            exitHotkey, backupExitHotkey, enterHotkey
        };

        Text = "隐私屏 · 快捷键设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.White;
        Icon = TrayIconFactory.CreateAppIcon();
        ClientSize = new Size(560, 418);

        // ── 标题 ──
        Controls.Add(new Label
        {
            Text = "快捷键设置",
            Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
            ForeColor = Ink,
            AutoSize = true,
            Location = new Point(26, 18)
        });

        Controls.Add(new Label
        {
            Text = "点一下方框，然后直接按下你想要的组合键（例如：按住 Ctrl 再按 B）。",
            ForeColor = Muted,
            AutoSize = true,
            Location = new Point(28, 56)
        });

        // ── 三行快捷键 ──
        AddRow("退出黑屏（主键）", _exit, exitHotkey, DefaultExit, 96);
        AddRow("退出黑屏（备用键）", _backup, backupExitHotkey, DefaultBackup, 136);
        AddRow("进入黑屏", _enter, enterHotkey, DefaultEnter, 176);

        // ── 规则说明 ──
        var tips = new Label
        {
            Location = new Point(26, 218),
            Size = new Size(508, 104),
            BackColor = PanelBg,
            ForeColor = Color.FromArgb(90, 100, 115),
            Padding = new Padding(12, 8, 12, 8),
            Text = "• 必须至少包含一个修饰键（Ctrl / Alt / Shift / Win）——单独一个字母键会抢走正常打字，无法使用。\r\n" +
                   "• 想清空某一项：点中方框后按 Backspace 或 Delete。\r\n" +
                   "• 备用键建议保留：它和主键互相独立，主键被别的软件占用时还能靠它退出黑屏。\r\n" +
                   "• 保存时会自动试注册一次，检测该组合是否已被其它软件占用；被占用会提示你更换。\r\n" +
                   "• Ctrl+Alt+Del、Win+L 属系统保留键，不可使用。"
        };
        Controls.Add(tips);

        // ── 错误提示 ──
        _error.Visible = false;
        _error.Location = new Point(28, 328);
        _error.Size = new Size(504, 38);
        _error.ForeColor = Danger;
        Controls.Add(_error);

        // ── 按钮 ──
        var resetAll = new Button
        {
            Text = "全部恢复默认",
            Location = new Point(26, 372),
            Size = new Size(112, 32),
            FlatStyle = FlatStyle.System
        };
        resetAll.Click += (_, _) =>
        {
            _exit.Value = DefaultExit;
            _backup.Value = DefaultBackup;
            _enter.Value = DefaultEnter;
            HideError();
        };
        Controls.Add(resetAll);

        var save = new Button
        {
            Text = "保存",
            Location = new Point(330, 372),
            Size = new Size(100, 32),
            BackColor = Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        save.FlatAppearance.BorderSize = 0;
        save.Click += (_, _) => OnSave();
        Controls.Add(save);

        var cancel = new Button
        {
            Text = "取消",
            Location = new Point(440, 372),
            Size = new Size(94, 32),
            FlatStyle = FlatStyle.System,
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(cancel);

        CancelButton = cancel;
        AcceptButton = save;
    }

    /// <summary>加一行：说明文字 + 按键框 + 单项“默认”按钮。</summary>
    private void AddRow(string caption, HotkeyBox box, string initialValue, string defaultValue, int top)
    {
        Controls.Add(new Label
        {
            Text = caption,
            ForeColor = Ink,
            AutoSize = true,
            Location = new Point(28, top + 7)
        });

        box.Location = new Point(190, top);
        box.Size = new Size(190, 30);
        box.Value = initialValue;
        box.TextChanged += (_, _) => HideError();
        Controls.Add(box);

        var one = new Button
        {
            Text = "默认",
            Location = new Point(396, top),
            Size = new Size(72, 30),
            FlatStyle = FlatStyle.System
        };
        one.Click += (_, _) =>
        {
            box.Value = defaultValue;
            HideError();
        };
        Controls.Add(one);
    }

    private void HideError()
    {
        _error.Visible = false;
        _error.Text = "";
    }

    private void OnSave()
    {
        var rows = new (string Label, HotkeyBox Box)[]
        {
            ("退出黑屏（主键）", _exit),
            ("退出黑屏（备用键）", _backup),
            ("进入黑屏", _enter)
        };

        // ① 不能为空
        foreach (var (label, box) in rows)
        {
            if (string.IsNullOrWhiteSpace(box.Value))
            {
                Fail($"{label}：还没有设置按键。点一下那个方框，然后按下你想要的组合键。", box);
                return;
            }
        }

        // ② 必须含修饰键（FR-04 硬性规则）
        foreach (var (label, box) in rows)
        {
            if (!HotKeyParser.HasModifier(box.Value))
            {
                Fail($"{label}：必须至少包含一个修饰键（Ctrl / Alt / Shift / Win）。" +
                     "单独一个按键会抢走正常打字，不能使用。", box);
                return;
            }
        }

        // ③ 三个快捷键不能互相重复
        for (int i = 0; i < rows.Length; i++)
        {
            for (int j = i + 1; j < rows.Length; j++)
            {
                if (string.Equals(rows[i].Box.Value, rows[j].Box.Value, StringComparison.OrdinalIgnoreCase))
                {
                    Fail($"「{rows[i].Label}」和「{rows[j].Label}」用了一样的组合键，请换一个。", rows[j].Box);
                    return;
                }
            }
        }

        // ④ 系统保留键
        foreach (var (label, box) in rows)
        {
            if (HotKeyParser.IsSystemReserved(box.Value, out string why))
            {
                Fail($"{label}：{why}。", box);
                return;
            }
        }

        // ⑤ 真机试注册：检测是否已被其它软件占用（FR-04 要求“被占用则阻止保存”）
        foreach (var (label, box) in rows)
        {
            if (_currentKeys.Contains(box.Value)) continue;   // 本程序正在用它，跳过不试

            if (!HotKeyService.TryProbe(box.Value, out string why))
            {
                Fail($"{label}：{box.Value} {why}。请换一个组合键，" +
                     "或先关闭占用它的那个软件再来设置。", box);
                return;
            }
        }

        HideError();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Fail(string message, HotkeyBox focus)
    {
        _error.Text = message;
        _error.Visible = true;
        focus.Focus();
        focus.SelectAll();
    }
}

/// <summary>
/// 一个“按键框”：获得焦点后按下组合键，就会把 "Ctrl+Alt+B" 这样的写法显示出来。
/// 用户不需要知道任何格式，纯粹“按出来”。
/// </summary>
internal sealed class HotkeyBox : TextBox
{
    private static readonly Color Ink = Color.FromArgb(24, 30, 42);
    private static readonly Color BoxBg = Color.FromArgb(252, 253, 255);

    public HotkeyBox()
    {
        ReadOnly = true;             // 只接受“按出来”的值，不允许手打
        ShortcutsEnabled = false;    // 不让 Ctrl+C / Ctrl+V 等默认行为干扰录入
        TabStop = true;
        Cursor = Cursors.Hand;
        TextAlign = HorizontalAlignment.Center;
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = BoxBg;
        ForeColor = Ink;
        Font = new Font("Consolas", 11F, FontStyle.Bold);
    }

    /// <summary>当前值；空串表示“未设置”。</summary>
    public string Value
    {
        get => Text;
        set => Text = value ?? "";
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        SelectAll();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        SelectAll();
    }

    // 让功能键、方向键等也按“输入键”送达本控件（而不是被当成对话框导航键）
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) != Keys.Tab;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Focused && HandleKey(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (HandleKey(e.KeyData))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;   // 防止发出“叮”的提示音 / 产生乱字符
            return;
        }
        base.OnKeyDown(e);
    }

    /// <summary>
    /// 按键处理。返回 true 表示本控件已消费掉这个按键。
    /// </summary>
    private bool HandleKey(Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        Keys mods = keyData & Keys.Modifiers;

        // Tab 留给“切换焦点”
        if (key == Keys.Tab) return false;

        // Backspace / Delete = 清空
        if (key is Keys.Back or Keys.Delete)
        {
            Value = "";
            return true;
        }

        // 单独按 Esc = 关闭对话框（要绑 Esc 请配合修饰键，如 Ctrl+Esc）
        if (key == Keys.Escape && mods == Keys.None) return false;

        // 只按住修饰键时先不动，等主键
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return true;

        // 不支持的键（方向键、小键盘等）安静吃掉，避免框里出现怪字符
        if (!HotKeyText.TryGetKeyText(key, out string keyText)) return true;

        Value = HotKeyText.Compose(
            ctrl: (mods & Keys.Control) != 0,
            alt: (mods & Keys.Alt) != 0,
            shift: (mods & Keys.Shift) != 0,
            win: HotKeyText.IsWinKeyDown(),
            keyText: keyText);

        return true;
    }
}
