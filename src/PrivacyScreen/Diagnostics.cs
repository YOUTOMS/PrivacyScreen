using System.Text;
using PrivacyScreen.Core;
using PrivacyScreen.Native;

namespace PrivacyScreen;

/// <summary>
/// 环境自检（命令行参数 --diag）。
/// 不进入黑屏，只探测关键能力并把结果写成报告，用来排查“远程为什么没穿透”“快捷键为什么没反应”之类的问题。
/// </summary>
internal static class Diagnostics
{
    public static void Run(bool quiet = false)
    {
        ApplicationConfiguration.Initialize();
        CursorHider.Restore();

        var sb = new StringBuilder();
        sb.AppendLine("========== 隐私屏 · 环境自检报告 ==========");
        sb.AppendLine($"时间       : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"操作系统   : {Environment.OSVersion.VersionString}");
        sb.AppendLine($"系统版本号 : {Environment.OSVersion.Version}（Win10 2004 对应 10.0.19041）");
        sb.AppendLine($"64 位系统  : {Environment.Is64BitOperatingSystem}");
        sb.AppendLine($"运行时     : {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        sb.AppendLine();

        // ── 1. 显示器 ──
        var monitors = MonitorHelper.GetMonitors();
        sb.AppendLine($"[1] 显示器：检测到 {monitors.Count} 台");
        foreach (var m in monitors)
        {
            sb.AppendLine($"    - {m.Title}  {m.Detail}");
            sb.AppendLine($"      标识 Id = {m.Id}");
        }
        sb.AppendLine();

        // ── 2. 捕获排除能力 ──
        using (var probe = CreateOffscreenWindow())
        {
            IntPtr hwnd = probe.Handle;

            bool excludeOk = Win32.SetWindowDisplayAffinity(hwnd, Win32.WDA_EXCLUDEFROMCAPTURE);
            int err1 = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            sb.AppendLine($"[2] 捕获排除 WDA_EXCLUDEFROMCAPTURE：{(excludeOk ? "支持 ✓（本地黑、远程能看到正常桌面）" : "不支持 ✗")}");
            if (!excludeOk) sb.AppendLine($"    错误码 = {err1}（87=参数错误/不支持，5=权限不足）");

            bool monitorOk = Win32.SetWindowDisplayAffinity(hwnd, Win32.WDA_MONITOR);
            sb.AppendLine($"    降级模式 WDA_MONITOR          ：{(monitorOk ? "支持 ✓（远程也会看到黑屏）" : "不支持 ✗")}");

            Win32.SetWindowDisplayAffinity(hwnd, Win32.WDA_NONE);

            // ── 3. 快捷键占用情况 ──
            sb.AppendLine();
            sb.AppendLine("[3] 快捷键占用检测");
            int id = 9001;
            foreach (var hk in new[] { "Ctrl+Alt+B", "Ctrl+Alt+N", "Ctrl+Alt+M" })
            {
                if (HotKeyParser.TryParse(hk, out uint mods, out uint vk))
                {
                    bool ok = Win32.RegisterHotKey(hwnd, id, mods | Win32.MOD_NOREPEAT, vk);
                    sb.AppendLine($"    {hk,-14} : {(ok ? "可用 ✓" : "被其它程序占用 ✗")}");
                    if (ok) Win32.UnregisterHotKey(hwnd, id);
                }
                else
                {
                    sb.AppendLine($"    {hk,-14} : 解析失败 ✗");
                }
                id++;
            }
            probe.Close();
        }

        // ── 4. 指针状态 ──
        sb.AppendLine();
        sb.AppendLine($"[4] 鼠标指针句柄 : 0x{Win32.GetCursor():X}（已恢复为系统默认）");

        // ── 5. 配置文件 ──
        sb.AppendLine();
        sb.AppendLine($"[5] 配置文件路径 : {AppSettings.FilePath}");
        sb.AppendLine($"    是否存在     : {File.Exists(AppSettings.FilePath)}");

        sb.AppendLine();
        sb.AppendLine("========== 报告结束 ==========");

        string text = sb.ToString();

        // 写到临时目录，方便直接打开查看
        string outPath = Path.Combine(Path.GetTempPath(), "PrivacyScreen-diag.txt");
        try { File.WriteAllText(outPath, text, Encoding.UTF8); } catch { /* 忽略 */ }

        try { Console.WriteLine(text); } catch { /* WinExe 下可能无控制台 */ }
        try { Console.WriteLine("报告已保存到：" + outPath); } catch { /* 忽略 */ }

        if (!quiet)
        {
            MessageBox.Show(text + "\n\n报告已保存到：\n" + outPath,
                "隐私屏 · 环境自检", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>创建一个屏幕外的隐藏探针窗口（不会闪屏、不干扰你）。</summary>
    private static Form CreateOffscreenWindow()
    {
        var f = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(100, 100),
            Opacity = 0
        };
        f.Show();
        return f;
    }
}
