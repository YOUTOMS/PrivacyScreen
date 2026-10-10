using System.Drawing.Drawing2D;

namespace PrivacyScreen.UI;

/// <summary>动态画一个程序图标：黑色屏幕 + 一条高亮横线，直观表示“黑屏”。</summary>
internal static class TrayIconFactory
{
    private static Icon? _cached;
    private static IntPtr _handle = IntPtr.Zero;

    public static Icon CreateAppIcon()
    {
        if (_cached is not null) return _cached;

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var dark = new SolidBrush(Color.FromArgb(28, 34, 46));
            using var path = RoundedRect(new RectangleF(2, 4, 28, 20), 4f);
            g.FillPath(dark, path);

            // 底座
            g.FillRectangle(dark, 12, 25, 8, 3);
            g.FillRectangle(dark, 8, 28, 16, 2);

            // 屏幕上的一道“关闭线”
            using var accent = new Pen(Color.FromArgb(255, 205, 90), 2.4f);
            accent.StartCap = LineCap.Round;
            accent.EndCap = LineCap.Round;
            g.DrawLine(accent, 9, 14, 23, 14);
        }

        _handle = bmp.GetHicon();
        _cached = Icon.FromHandle(_handle).Clone() as Icon ?? SystemIcons.Application;
        return _cached;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
