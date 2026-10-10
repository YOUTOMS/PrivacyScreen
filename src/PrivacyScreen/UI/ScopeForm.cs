using PrivacyScreen.Core;

namespace PrivacyScreen.UI;

/// <summary>
/// 黑屏范围选择面板（对应 PRD FR-13）。
/// 启动时弹出，用图形化示意 + 勾选框让你指定“黑哪几块屏”。
/// </summary>
internal sealed class ScopeForm : Form
{
    private readonly List<MonitorItem> _monitors;
    private readonly List<CheckBox> _checks = new();
    private readonly Panel _preview;
    private readonly CheckBox _remember;
    private readonly Button _okButton;

    /// <summary>用户确认的显示器标识列表。</summary>
    public List<string> SelectedMonitorIds { get; private set; } = new();

    /// <summary>用户是否勾选了“记住本次选择”。</summary>
    public bool RememberChoice => _remember.Checked;

    private static readonly Color Ink = Color.FromArgb(32, 38, 48);
    private static readonly Color Muted = Color.FromArgb(120, 130, 145);
    private static readonly Color Accent = Color.FromArgb(24, 30, 42);
    private static readonly Color ScreenFill = Color.FromArgb(236, 239, 243);
    private static readonly Color ScreenFillOn = Color.FromArgb(255, 214, 102);
    private static readonly Color ScreenEdge = Color.FromArgb(150, 160, 175);

    public ScopeForm(IReadOnlyList<MonitorItem> monitors, IReadOnlyCollection<string> preselected)
    {
        _monitors = monitors.ToList();

        Text = "隐私屏 · 选择黑屏范围";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.White;
        Icon = TrayIconFactory.CreateAppIcon();

        int rowHeight = 30;
        int listTop = 268;
        int bottom = listTop + _monitors.Count * rowHeight + 74;

        ClientSize = new Size(624, bottom);

        // ── 标题 ──
        var title = new Label
        {
            Text = "选择要黑屏的显示器",
            Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
            ForeColor = Ink,
            AutoSize = true,
            Location = new Point(26, 20)
        };
        Controls.Add(title);

        var sub = new Label
        {
            Text = "勾选后点击“开始黑屏”。未勾选的显示器保持正常显示，可继续使用。",
            ForeColor = Muted,
            AutoSize = true,
            Location = new Point(28, 56)
        };
        Controls.Add(sub);

        // ── 显示器示意区 ──
        _preview = new Panel
        {
            Location = new Point(26, 88),
            Size = new Size(572, 160),
            BackColor = Color.FromArgb(250, 251, 253),
            BorderStyle = BorderStyle.FixedSingle
        };
        _preview.Paint += (_, e) => DrawPreview(e.Graphics);
        Controls.Add(_preview);

        // ── 勾选列表 ──
        for (int i = 0; i < _monitors.Count; i++)
        {
            var m = _monitors[i];
            var cb = new CheckBox
            {
                Text = $"{m.Title}   ·   {m.Detail}",
                Location = new Point(28, listTop + i * rowHeight),
                Size = new Size(430, 24),
                Checked = preselected.Contains(m.Id),
                ForeColor = Ink
            };
            cb.CheckedChanged += (_, _) =>
            {
                _preview.Invalidate();
                UpdateOkEnabled();
            };
            _checks.Add(cb);
            Controls.Add(cb);
        }

        // ── 全选 / 全不选 ──
        var btnAll = new Button
        {
            Text = "全选",
            Location = new Point(468, listTop - 2),
            Size = new Size(62, 26),
            FlatStyle = FlatStyle.System
        };
        btnAll.Click += (_, _) => SetAll(true);
        Controls.Add(btnAll);

        var btnNone = new Button
        {
            Text = "全不选",
            Location = new Point(536, listTop - 2),
            Size = new Size(62, 26),
            FlatStyle = FlatStyle.System
        };
        btnNone.Click += (_, _) => SetAll(false);
        Controls.Add(btnNone);

        // ── 记住选择 ──
        _remember = new CheckBox
        {
            Text = "记住本次选择，以后直接用这个范围",
            Location = new Point(28, bottom - 58),
            Size = new Size(320, 24),
            ForeColor = Ink
        };
        Controls.Add(_remember);

        // ── 按钮 ──
        _okButton = new Button
        {
            Text = "开始黑屏",
            Location = new Point(392, bottom - 62),
            Size = new Size(104, 32),
            BackColor = Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _okButton.FlatAppearance.BorderSize = 0;
        _okButton.Click += (_, _) =>
        {
            SelectedMonitorIds = _monitors
                .Where((m, i) => _checks[i].Checked)
                .Select(m => m.Id)
                .ToList();
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(_okButton);

        var cancel = new Button
        {
            Text = "取消",
            Location = new Point(504, bottom - 62),
            Size = new Size(94, 32),
            FlatStyle = FlatStyle.System,
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(cancel);

        CancelButton = cancel;
        AcceptButton = _okButton;

        UpdateOkEnabled();

        // 快捷键：数字键 1/2/3… 快速切换对应显示器
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            int idx = e.KeyCode - Keys.D1;
            if (idx >= 0 && idx < _checks.Count)
            {
                _checks[idx].Checked = !_checks[idx].Checked;
            }
        };
    }

    private void SetAll(bool value)
    {
        foreach (var cb in _checks) cb.Checked = value;
    }

    private void UpdateOkEnabled()
    {
        // FR-13：一块都没勾选时，“开始黑屏”必须不可点
        bool any = _checks.Any(c => c.Checked);
        _okButton.Enabled = any;
        _okButton.BackColor = any ? Accent : Color.FromArgb(200, 205, 212);
    }

    /// <summary>按各显示器的真实相对位置画示意图，尽量防止“选错屏”。</summary>
    private void DrawPreview(Graphics g)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(250, 251, 253));

        if (_monitors.Count == 0) return;

        int minX = _monitors.Min(m => m.Bounds.X);
        int minY = _monitors.Min(m => m.Bounds.Y);
        int maxX = _monitors.Max(m => m.Bounds.Right);
        int maxY = _monitors.Max(m => m.Bounds.Bottom);
        int totalW = Math.Max(1, maxX - minX);
        int totalH = Math.Max(1, maxY - minY);

        int pad = 26;
        float availW = _preview.Width - pad * 2;
        float availH = _preview.Height - pad * 2;
        float scale = Math.Min(availW / totalW, availH / totalH);

        // 居中偏移
        float usedW = totalW * scale;
        float usedH = totalH * scale;
        float offX = (_preview.Width - usedW) / 2f;
        float offY = (_preview.Height - usedH) / 2f;

        for (int i = 0; i < _monitors.Count; i++)
        {
            var m = _monitors[i];
            bool on = _checks[i].Checked;

            var rect = new RectangleF(
                offX + (m.Bounds.X - minX) * scale,
                offY + (m.Bounds.Y - minY) * scale,
                m.Bounds.Width * scale,
                m.Bounds.Height * scale);

            using var fill = new SolidBrush(on ? ScreenFillOn : ScreenFill);
            using var pen = new Pen(on ? Color.FromArgb(200, 150, 30) : ScreenEdge, on ? 2f : 1f);

            g.FillRectangle(fill, rect);
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

            // 屏幕上的编号
            string label = $"显示器 {m.Index}";
            using var f = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            var size = g.MeasureString(label, f);
            var tp = new PointF(rect.X + (rect.Width - size.Width) / 2,
                                rect.Y + (rect.Height - size.Height) / 2);
            using var txt = new SolidBrush(on ? Color.FromArgb(120, 88, 0) : Color.FromArgb(110, 120, 135));
            g.DrawString(label, f, txt, tp);

            if (m.IsPrimary)
            {
                using var f2 = new Font("Microsoft YaHei UI", 8F);
                var s2 = g.MeasureString("主", f2);
                g.DrawString("主", f2, txt,
                    rect.X + (rect.Width - s2.Width) / 2,
                    rect.Y + (rect.Height - s2.Height) / 2 + 18);
            }
        }
    }
}
