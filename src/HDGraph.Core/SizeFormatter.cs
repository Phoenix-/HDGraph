using System.Globalization;

namespace HDGraph.Core;

/// <summary>Explorer-style sizes: binary multiples, short unit labels, two significant decimals at most.</summary>
public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (bytes < 1024)
            return bytes.ToString(culture) + " B";

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##";
        return value.ToString(format, culture) + " " + Units[unit];
    }

    public static string FormatPercent(double fraction, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var percent = fraction * 100;
        var format = percent >= 10 ? "0.#" : "0.##";
        return percent.ToString(format, culture) + " %";
    }
}
