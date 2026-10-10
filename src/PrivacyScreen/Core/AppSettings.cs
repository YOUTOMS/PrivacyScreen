using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrivacyScreen.Core;

/// <summary>黑屏范围的选择策略（对应 PRD FR-15）。</summary>
public enum ScopePolicy
{
    /// <summary>每次启动都弹面板询问（默认）。</summary>
    Ask,
    /// <summary>记住上次选择，直接黑屏。</summary>
    Remember,
    /// <summary>固定使用设定的默认范围。</summary>
    Fixed
}

/// <summary>
/// 程序配置。保存在 %APPDATA%\Blackout\settings.json，纯本地、不联网。
/// </summary>
public sealed class AppSettings
{
    /// <summary>范围策略。</summary>
    public ScopePolicy ScopePolicy { get; set; } = ScopePolicy.Ask;

    /// <summary>“记住”的显示器标识列表。</summary>
    public List<string> RememberedMonitorIds { get; set; } = new();

    /// <summary>进入黑屏的快捷键。</summary>
    public string EnterHotkey { get; set; } = "Ctrl+Alt+M";

    /// <summary>退出黑屏的快捷键（主）。</summary>
    public string ExitHotkey { get; set; } = "Ctrl+Alt+B";

    /// <summary>退出黑屏的快捷键（备用，与主键冲突时仍可用）。</summary>
    public string BackupExitHotkey { get; set; } = "Ctrl+Alt+N";

    /// <summary>黑屏时双击鼠标左键可退出（键盘失灵时的软件退路）。</summary>
    public bool MouseDoubleClickExit { get; set; } = true;

    /// <summary>黑屏时连续按两次 Esc 可退出。</summary>
    public bool DoubleEscapeExit { get; set; } = true;

    /// <summary>当所有显示器都被黑时，隐藏鼠标指针（更像关机）。</summary>
    public bool HideCursorWhenAllBlack { get; set; } = true;

    /// <summary>使用“捕获排除”模式：本地黑屏、远程看到正常桌面。</summary>
    public bool UseExcludeFromCapture { get; set; } = true;

    /// <summary>是否已展示过“如何退出黑屏”的首次提示。</summary>
    public bool HasShownFirstTimeTip { get; set; }

    // ─────────────── 读写 ───────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Blackout");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
                if (s is not null) return s;
            }
        }
        catch
        {
            // 配置损坏时静默回退到默认值，保证程序一定能启动
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch
        {
            // 存不下就算了，不影响主流程
        }
    }
}
