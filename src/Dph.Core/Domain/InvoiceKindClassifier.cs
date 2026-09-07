namespace Dph.Core.Domain;

// Pomocné rozlišení státu registrace. DIČ samo neurčuje daňový režim plnění.
public static class InvoiceKindClassifier
{
    // EU VAT prefixy pro SLUŽBY; XI je unijním režimem pouze pro zboží.
    public static readonly IReadOnlySet<string> EuVatPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AT", "BE", "BG", "HR", "CY", "DK", "EE", "FI", "FR", "DE", "EL", "HU", "IE", "IT",
        "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE"
    };

    // Dodavatel registrovaný v jiném členském státě (podle prefixu DIČ) → §9/1, ř.5/6.
    public static bool IsEuSupplier(string? counterpartyDic)
    {
        var dic = NormalizeVatId(counterpartyDic);
        return dic is { Length: >= 2 } && EuVatPrefixes.Contains(dic[..2]);
    }

    public static string NormalizeVatId(string? value)
        => string.Concat((value ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    public static bool IsCzechDic(string? value)
    {
        var dic = NormalizeVatId(value);
        if (dic.StartsWith("CZ", StringComparison.OrdinalIgnoreCase)) dic = dic[2..];
        return dic.Length is >= 8 and <= 10 && dic.All(char.IsAsciiDigit);
    }

    // Souhrnné řádky kontrolního hlášení importované/vedené pod evidenčním číslem oddílu.
    public static bool IsControlStatementSummary(string? evidenceNumber)
        => string.Equals(evidenceNumber?.Trim(), "A5", StringComparison.OrdinalIgnoreCase)
           || string.Equals(evidenceNumber?.Trim(), "B3", StringComparison.OrdinalIgnoreCase);
}
