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

    /// <summary>注册一批快捷键。keys: (动作名, 快捷键文本)。</summary>
    public void Start(IEnumerable<(string Action, string Hotkey)> keys)
    {
        _ids.Clear();
        _errors.Clear();

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
}
