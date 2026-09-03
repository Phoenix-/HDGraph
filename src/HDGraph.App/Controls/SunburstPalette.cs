using Avalonia.Media;
using HDGraph.Core;
using HDGraph.Geometry;

namespace HDGraph.App.Controls;

/// <summary>Colours of the chart. Hue follows the angle, so a directory and its subtree share a family of
/// colours and neighbours differ; rings get lighter outward so depth reads at a glance.</summary>
internal static class SunburstPalette
{
    public static Color Fill(in Arc arc, bool dark, bool hovered)
    {
        var node = arc.Node;
        double hue = arc.MidAngle;
        double saturation = node.Kind == NodeKind.FreeSpace ? 0 : node.Error is null ? 0.6 : 0.15;
        double lightness = dark
            ? Math.Min(0.42 + (arc.Ring - 1) * 0.04, 0.6)
            : Math.Min(0.6 + (arc.Ring - 1) * 0.035, 0.78);
        if (node.Kind == NodeKind.FreeSpace)
            lightness = dark ? 0.32 : 0.88;
        if (hovered)
            lightness = dark ? Math.Min(lightness + 0.14, 0.8) : Math.Max(lightness - 0.14, 0.3);
        return FromHsl(hue, saturation, lightness);
    }

    /// <summary>The two stripe colours of the slice that stands for what a running scan has found so far:
    /// a muted blue-grey, so it reads as "not data yet" next to the coloured sectors.</summary>
    public static (Color A, Color B) ScanningStripes(bool dark, bool hovered)
    {
        var (a, b) = dark ? (0.30, 0.37) : (0.86, 0.79);
        if (hovered)
        {
            var shift = dark ? 0.1 : -0.1;
            (a, b) = (a + shift, b + shift);
        }

        return (FromHsl(210, 0.14, a), FromHsl(210, 0.14, b));
    }

    public static Color CenterFill(bool dark) => dark ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xE8, 0xE8, 0xE8);

    public static Color Separator(bool dark) => dark ? Color.FromRgb(0x1C, 0x1C, 0x1C) : Colors.White;

    public static Color Label(bool dark) => dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1A, 0x1A, 0x1A);

    public static Color FromHsl(double hue, double saturation, double lightness)
    {
        hue = ((hue % 360) + 360) % 360;
        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = lightness - c / 2;
        var (r, g, b) = (hue / 60) switch
        {
            < 1 => (c, x, 0d),
            < 2 => (x, c, 0d),
            < 3 => (0d, c, x),
            < 4 => (0d, x, c),
            < 5 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}
