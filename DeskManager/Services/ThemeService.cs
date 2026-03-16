using System.Windows;
using System.Windows.Media;
using DeskManager.Models;
using Application      = System.Windows.Application;
using Color            = System.Windows.Media.Color;
using ColorConverter   = System.Windows.Media.ColorConverter;

namespace DeskManager.Services;

public static class ThemeService
{
    public static void Apply(ThemeConfig theme)
    {
        var res = Application.Current.Resources;
        res["GridBackgroundBrush"] = MakeBrush(theme.GridBackground);
        res["TitleBarBrush"]       = MakeBrush(theme.TitleBarColor);
        res["GridBorderBrush"]     = MakeBrush(theme.BorderColor);
        res["GridTextBrush"]       = MakeBrush(theme.TextColor);
    }

    private static SolidColorBrush MakeBrush(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(color);
            b.Freeze();
            return b;
        }
        catch
        {
            return new SolidColorBrush(Colors.Transparent);
        }
    }

    /// Convert a WinForms Color + byte alpha to ARGB hex string
    public static string ToHex(System.Drawing.Color c, byte alpha) =>
        $"#{alpha:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    /// Extract RGB (no alpha) from an ARGB hex string as System.Drawing.Color
    public static System.Drawing.Color ToDrawingColor(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return System.Drawing.Color.FromArgb(c.R, c.G, c.B);
        }
        catch { return System.Drawing.Color.DimGray; }
    }

    /// Extract alpha (0-255) from ARGB hex string
    public static byte GetAlpha(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return c.A;
        }
        catch { return 200; }
    }
}
