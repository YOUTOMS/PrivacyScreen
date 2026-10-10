using PrivacyScreen.Core;
using PrivacyScreen.UI;

namespace PrivacyScreen;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // ── 环境自检模式：不做任何黑屏，只输出能力报告 ──
        if (args.Any(a => string.Equals(a, "--diag", StringComparison.OrdinalIgnoreCase)))
        {
            bool quiet = args.Any(a => string.Equals(a, "--quiet", StringComparison.OrdinalIgnoreCase));
            Diagnostics.Run(quiet);
            return;
        }

        // ── 单实例：避免开出两个程序互相抢快捷键 ──
        using var mutex = new Mutex(true, "PrivacyScreen.SingleInstance.Mutex", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show(
                "隐私屏已经在运行了。\n\n请看屏幕右下角的托盘图标（一个黑色小显示器）。",
                "隐私屏", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        // ── 全局异常兜底 ──
        // 只要程序出任何问题，第一件事就是把鼠标指针恢复回来，绝不能让你“没鼠标可用”。
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (_, e) =>
        {
            CursorHider.Restore();
            MessageBox.Show(
                "程序遇到了一点问题，鼠标指针已经自动恢复。\n\n详细信息：" + e.Exception.Message,
                "隐私屏", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, _) => CursorHider.Restore();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CursorHider.Restore();

        Application.Run(new TrayContext());
    }
}
