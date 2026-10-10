using PrivacyScreen.Native;

namespace PrivacyScreen.Core;

/// <summary>
/// 全局快捷键服务。
///
/// 关键设计（对应 PRD FR-11 的硬性约束）：
/// 快捷键注册在一个**独立的后台线程**上，配一个隐藏的“消息泵”窗口。
/// 这样即使主界面卡住了，退出快捷键依然有效 —— 这是防“退不出”的第一道保险。
/// </summary>
internal sealed class HotKeyService : IDisposable
{
    private readonly Dictionary<int, string> _ids = new();      // id → 动作名
    private readonly Dictionary<string, string> _errors = new(); // 动作名 → 失败原因
    private HotKeyWindow? _window;
    private Thread? _thread;
    private int _nextId = 1;
    private bool _disposed;

    /// <summary>按下快捷键时触发（在 UI 线程上回调）。参数是动作名。</summary>
    public event Action<string>? Pressed;

    /// <summary>注册失败的动作说明（例如被其它软件占用）。</summary>
    public IReadOnlyDictionary<string, string> Errors => _errors;

    /// <summary>
    /// 注册一批快捷键。keys: (动作名, 快捷键文本)。
    /// 允许在运行期间反复调用（改键后立即重新注册，对应 PRD FR-04）。
    /// </summary>
    public void Start(IEnumerable<(string Action, string Hotkey)> keys)
    {
        Stop();                 // 先注销上一批，避免新旧两套键同时生效
        _ids.Clear();
        _errors.Clear();
        _nextId = 1;

        var parsed = new List<(int Id, string Action, uint Mods, uint Vk)>();
        foreach (var (action, text) in keys)
        {
            if (!HotKeyParser.TryParse(text, out uint mods, out uint vk))
            {
                _errors[action] = $"无法识别的快捷键写法：{text}";
                continue;
            }
            int id = _nextId++;
            _ids[id] = action;
            parsed.Add((id, action, mods, vk));
        }

        _thread = new Thread(() =>
        {
            _window = new HotKeyWindow();
            _window.CreateControl();
            IntPtr hwnd = _window.Handle;

            foreach (var p in parsed)
            {
                bool ok = Win32.RegisterHotKey(hwnd, p.Id, p.Mods | Win32.MOD_NOREPEAT, p.Vk);
                if (!ok)
                {
                    _errors[p.Action] = "已被其它程序占用或无效";
                }
            }
            _window.HotKeyPressed += id =>
            {
                if (_ids.TryGetValue(id, out var action)) Pressed?.Invoke(action);
            };

            Application.Run();  // 空消息泵：不显示窗口，只处理快捷键消息
        })
        {
            IsBackground = true,
            Name = "PrivacyScreen-HotKey"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>
    /// 注销全部快捷键并停掉后台线程。
    /// 之后可以再次调用 Start 重新注册（改键流程要用）。
    /// </summary>
    public void Stop()
    {
        var window = _window;
        var thread = _thread;
        var ids = _ids.Keys.ToList();   // 先取出，回调里还要用

        _window = null;
        _thread = null;
        _ids.Clear();

        if (window is null) return;

        try
        {
            if (window.IsHandleCreated)
            {
                window.BeginInvoke(new Action(() =>
                {
                    foreach (int id in ids) Win32.UnregisterHotKey(window.Handle, id);
                    Application.ExitThread();   // 结束这个后台消息循环
                }));
                thread?.Join(1500);             // 等它干净退出，避免两套注册打架
            }
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>
    /// 试注册一个快捷键，用来判断它是否已被**别的程序**占用
    /// （对应 PRD FR-04：保存前必须检测，被占用则阻止保存）。
    ///
    /// 做法：临时开一个隐藏窗口注册一次，无论成功与否立刻注销，不留痕迹。
    /// </summary>
    public static bool TryProbe(string hotkey, out string reason)
    {
        reason = "";

        if (!HotKeyParser.TryParse(hotkey, out uint mods, out uint vk))
        {
            reason = "无法识别的快捷键";
            return false;
        }

        HotKeyWindow? probe = null;
        try
        {
            probe = new HotKeyWindow();
            probe.CreateControl();

            bool ok = Win32.RegisterHotKey(probe.Handle, ProbeId, mods | Win32.MOD_NOREPEAT, vk);
            if (ok) Win32.UnregisterHotKey(probe.Handle, ProbeId);
            else reason = "已被其它程序占用";
            return ok;
        }
        catch
        {
            // 探测本身出问题时，不要卡住用户保存
            return true;
        }
        finally
        {
            try { probe?.Dispose(); } catch { /* 忽略 */ }
        }
    }

    /// <summary>探测用的临时快捷键 ID，取一个不会和正式注册（从 1 递增）撞车的值。</summary>
    private const int ProbeId = 0x4FFF;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_window is { IsHandleCreated: true })
            {
                _window.BeginInvoke(new Action(() =>
                {
                    foreach (var id in _ids.Keys) Win32.UnregisterHotKey(_window.Handle, id);
                    Application.ExitThread();
                }));
            }
        }
        catch
        {
            // 忽略
        }
    }

    // ───────────────────────────────────────────────

    private sealed class HotKeyWindow : Form
    {
        public event Action<int>? HotKeyPressed;

        public HotKeyWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
        }

        // 永不真正显示，只借用它的句柄来收消息
        protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_HOTKEY)
            {
                HotKeyPressed?.Invoke(m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }
    }
}

/// <summary>把 "Ctrl+Alt+B" 这样的文本解析成 Win32 的 修饰键 + 虚拟键码。</summary>
internal static class HotKeyParser
{
    public static bool TryParse(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim())
                        .Where(p => p.Length > 0)
                        .ToList();
        if (parts.Count == 0) return false;

        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= Win32.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= Win32.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= Win32.MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    modifiers |= Win32.MOD_WIN;
                    break;
                default:
                    if (!TryParseKey(p, out vk)) return false;
                    break;
            }
        }

        return vk != 0;
    }

    private static bool TryParseKey(string s, out uint vk)
    {
        vk = 0;
        if (s.Length == 1)
        {
            char c = char.ToUpperInvariant(s[0]);
            if (c is >= 'A' and <= 'Z') { vk = c; return true; }
            if (c is >= '0' and <= '9') { vk = c; return true; }
            return false;
        }

        if (s.StartsWith('F') && int.TryParse(s.AsSpan(1), out int fn) && fn is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + fn - 1);  // VK_F1 = 0x70
            return true;
        }

        switch (s.ToLowerInvariant())
        {
            case "space": vk = 0x20; return true;
            case "esc":
            case "escape": vk = 0x1B; return true;
            case "home": vk = 0x24; return true;
            case "end": vk = 0x23; return true;
            case "insert": vk = 0x2D; return true;
            case "delete": vk = 0x2E; return true;
            case "pageup": vk = 0x21; return true;
            case "pagedown": vk = 0x22; return true;
            default: return false;
        }
    }

    /// <summary>
    /// 是否含有至少一个修饰键（Ctrl / Alt / Shift / Win）。
    /// PRD FR-04 硬性要求：不含修饰键的组合（比如单独一个字母）会抢走正常打字，禁止使用。
    /// </summary>
    public static bool HasModifier(string text)
    {
        if (!TryParse(text, out uint mods, out _)) return false;

        const uint anyModifier = Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_SHIFT | Win32.MOD_WIN;
        return (mods & anyModifier) != 0;
    }

    /// <summary>是否是 Windows 保留、任何程序都注册不了（或不该占用）的组合。</summary>
    public static bool IsSystemReserved(string text, out string reason)
    {
        reason = "";
        if (!TryParse(text, out uint mods, out uint vk)) return false;

        // Ctrl+Alt+Del 被系统安全注意序列独占，注册必然失败
        if (mods == (Win32.MOD_CONTROL | Win32.MOD_ALT) && vk == Win32.VK_DELETE)
        {
            reason = "Ctrl+Alt+Del 是 Windows 安全键，任何程序都无法注册";
            return true;
        }

        // Win+L 是系统自带的锁屏键
        if (mods == Win32.MOD_WIN && vk == 'L')
        {
            reason = "Win+L 是 Windows 自带的锁屏键，不能占用";
            return true;
        }

        return false;
    }
}

/// <summary>
/// 快捷键文本的“拼装”与按键名翻译（给可视化设置界面用，对应 PRD FR-04）。
/// </summary>
internal static class HotKeyText
{
    /// <summary>按 Ctrl → Alt → Shift → Win → 主键 的顺序拼成 "Ctrl+Alt+B"。</summary>
    public static string Compose(bool ctrl, bool alt, bool shift, bool win, string keyText)
    {
        var parts = new List<string>(5);
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        parts.Add(keyText);
        return string.Join("+", parts);
    }

    /// <summary>把 .NET 的按键翻译成快捷键写法；不支持的键（方向键、小键盘等）返回 false。</summary>
    public static bool TryGetKeyText(Keys key, out string text)
    {
        text = "";

        if (key is >= Keys.A and <= Keys.Z) { text = ((char)key).ToString(); return true; }
        if (key is >= Keys.D0 and <= Keys.D9) { text = ((char)('0' + (key - Keys.D0))).ToString(); return true; }
        if (key is >= Keys.F1 and <= Keys.F24) { text = "F" + (key - Keys.F1 + 1); return true; }

        switch (key)
        {
            case Keys.Space: text = "Space"; return true;
            case Keys.Escape: text = "Esc"; return true;
            case Keys.Home: text = "Home"; return true;
            case Keys.End: text = "End"; return true;
            case Keys.Insert: text = "Insert"; return true;
            case Keys.Delete: text = "Delete"; return true;
            case Keys.PageUp: text = "PageUp"; return true;
            case Keys.PageDown: text = "PageDown"; return true;
            default: return false;
        }
    }

    /// <summary>当前是否按住 Win 键（.NET 的 Keys 修饰符里不含 Win，只能问系统）。</summary>
    public static bool IsWinKeyDown() =>
        (Win32.GetAsyncKeyState(Win32.VK_LWIN) & 0x8000) != 0 ||
        (Win32.GetAsyncKeyState(Win32.VK_RWIN) & 0x8000) != 0;
}
