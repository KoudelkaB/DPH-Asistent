using System.Globalization;

namespace Dph.Core.Calculations;

public static class DecimalInput
{
    // Czech decimal comma and spaces (including NBSP) are accepted. A dot is
    // always a decimal separator, never a thousands separator.
    public static bool TryParse(string value, out decimal result)
        => decimal.TryParse(value.Replace(" ", "").Replace("\u00a0", "").Replace("\u202f", "").Replace(',', '.'),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out result);

    public static decimal Parse(string value)
        => TryParse(value, out var result) ? result
            : throw new FormatException($"Neplatné číslo: „{value}“. Použijte desetinnou čárku nebo tečku.");
}
