namespace Dph.Core.Domain;

// Režim přijatého plnění se neurčuje ručně, ale odvozuje se z DIČ dodavatele – uživatel v UI
// vybírá už jen Vydaná/Přijatá. Viz InvoiceKind.ReverseCharge.
public static class InvoiceKindClassifier
{
    // EU VAT prefixy (mimo CZ = tuzemsko); EL = Řecko, XI = Severní Irsko.
    public static readonly IReadOnlySet<string> EuVatPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AT", "BE", "BG", "HR", "CY", "DK", "EE", "FI", "FR", "DE", "EL", "HU", "IE", "IT",
        "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE", "XI"
    };

    /// <summary>
    /// Určí režim přijatého dokladu podle DIČ dodavatele:
    /// <list type="bullet">
    /// <item>souhrnný řádek KH (evidenční číslo A5/B3) → vždy tuzemské plnění;</item>
    /// <item>české DIČ (prefix CZ, nebo jen číslice) → tuzemský odpočet (ř.40/41, KH B.2/B.3);</item>
    /// <item>jinak (DIČ jiného státu, nebo prázdné – typicky USA bez VAT ID) → reverse charge
    /// (ř.5/6 u EU dodavatele, ř.12/13 u třetí země; KH A.2).</item>
    /// </list>
    /// Tuzemský plátce má DIČ vždy (bez něj by doklad stejně nešel do KH B.2); drobné tuzemské
    /// doklady bez DIČ patří do souhrnu B3.
    /// </summary>
    public static InvoiceKind ClassifyReceived(string? counterpartyDic, string? evidenceNumber)
    {
        if (IsControlStatementSummary(evidenceNumber))
        {
            return InvoiceKind.ReceivedDomesticWithVat;
        }

        var dic = counterpartyDic?.Trim();
        if (string.IsNullOrEmpty(dic))
        {
            return InvoiceKind.ReverseCharge;
        }

        return dic.StartsWith("CZ", StringComparison.OrdinalIgnoreCase) || dic.All(char.IsDigit)
            ? InvoiceKind.ReceivedDomesticWithVat
            : InvoiceKind.ReverseCharge;
    }

    // Dodavatel registrovaný v jiném členském státě (podle prefixu DIČ) → §9/1, ř.5/6.
    public static bool IsEuSupplier(string? counterpartyDic)
    {
        var dic = counterpartyDic?.Trim();
        return dic is { Length: >= 2 } && EuVatPrefixes.Contains(dic[..2]);
    }

    // Souhrnné řádky kontrolního hlášení importované/vedené pod evidenčním číslem oddílu.
    public static bool IsControlStatementSummary(string? evidenceNumber)
        => string.Equals(evidenceNumber?.Trim(), "A5", StringComparison.OrdinalIgnoreCase)
           || string.Equals(evidenceNumber?.Trim(), "B3", StringComparison.OrdinalIgnoreCase);
}
