using System.Windows.Forms;

namespace PrivacyScreen.Core;

/// <summary>一台显示器的描述。</summary>
public sealed class MonitorItem
{
    /// <summary>用于“记住选择”的稳定标识（不直接用 1/2 序号，避免插拔后错位）。</summary>
    public required string Id { get; init; }

    /// <summary>Windows「显示设置」里的编号（1、2 …）。</summary>
    public required int Index { get; init; }

    /// <summary>该显示器在虚拟桌面中的物理像素范围。</summary>
    public required Rectangle Bounds { get; init; }

    /// <summary>是否为主显示器。</summary>
    public required bool IsPrimary { get; init; }

    /// <summary>系统设备名，如 \\.\DISPLAY1。</summary>
    public required string DeviceName { get; init; }

    public string Title => IsPrimary ? $"显示器 {Index}（主显示器）" : $"显示器 {Index}";

    public string Detail => $"{Bounds.Width} × {Bounds.Height}  ·  位置 X={Bounds.X}, Y={Bounds.Y}";
}

public static class MonitorHelper
{
    /// <summary>枚举当前所有显示器，按编号排序。</summary>
    public static List<MonitorItem> GetMonitors()
    {
        var result = new List<MonitorItem>();
        var primary = Screen.PrimaryScreen;

        foreach (var s in Screen.AllScreens)
        {
            int idx = ParseDisplayIndex(s.DeviceName);
            string id = BuildId(s, idx);
            result.Add(new MonitorItem
            {
                Id = id,
                Index = idx,
                Bounds = s.Bounds,
                IsPrimary = primary is not null && s.DeviceName == primary.DeviceName,
                DeviceName = s.DeviceName
            });
        }

        return result.OrderBy(m => m.Index).ToList();
    }

    /// <summary>把 \\.\DISPLAY2 解析成 2，让编号和 Windows「显示设置」一致。</summary>
    private static int ParseDisplayIndex(string deviceName)
    {
        int i = deviceName.Length - 1;
        int pow = 1;
        int val = 0;
        while (i >= 0 && char.IsDigit(deviceName[i]))
        {
            val += (deviceName[i] - '0') * pow;
            pow *= 10;
            i--;
        }
        return val > 0 ? val : 999;
    }

    private static string BuildId(Screen s, int idx)
    {
        // 设备名 + 分辨率，双重特征；比纯序号稳定得多
        return $"{s.DeviceName}|{s.Bounds.Width}x{s.Bounds.Height}";
    }

    /// <summary>
    /// 根据记住的 Id 找出对应显示器；找不到时按分辨率兜底匹配。
    /// </summary>
    public static List<MonitorItem> Resolve(IEnumerable<string> ids, List<MonitorItem> current)
    {
        var wanted = new List<MonitorItem>();
        foreach (var id in ids)
        {
            var hit = current.FirstOrDefault(m => m.Id == id);
            if (hit is null)
            {
                // 兜底：分辨率相同也认（面板换接口时可能出现）
                var res = id.Contains('|') ? id.Split('|')[1] : null;
                if (res is not null)
                {
                    int w = int.TryParse(res.Split('x')[0], out var a) ? a : 0;
                    int h = res.Contains('x') && int.TryParse(res.Split('x')[1], out var b) ? b : 0;
                    hit = current.FirstOrDefault(m => m.Bounds.Width == w && m.Bounds.Height == h);
                }
            }
            if (hit is not null && !wanted.Contains(hit)) wanted.Add(hit);
        }
        return wanted;
    }
}
