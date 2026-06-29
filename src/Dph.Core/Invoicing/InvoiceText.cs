using System.Globalization;

namespace Dph.Core.Invoicing;

// Placeholdery v textech faktury (úvodní text apod.). Dosazují se až při generování PDF podle
// DUZP, takže uložená šablona zůstane editovatelná a v PDF vždy sedí na zdaňovací období.
public static class InvoiceText
{
    public const string MonthPlaceholder = "{měsíc}";
    public const string YearPlaceholder = "{rok}";

    public const string DefaultIntroTemplate = "Za {měsíc} {rok} Vám fakturujeme:";

    private static readonly string[] CzechMonthsNominative =
    [
        "leden", "únor", "březen", "duben", "květen", "červen",
        "červenec", "srpen", "září", "říjen", "listopad", "prosinec"
    ];

    public static string MonthNominative(int month) => CzechMonthsNominative[month - 1];

    // Nahradí {měsíc}/{rok} (a jejich ASCII variantu {mesic}) hodnotami z data zdanitelného plnění.
    public static string ResolvePlaceholders(string? text, DateOnly taxableSupplyDate)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? "";
        }

        var month = MonthNominative(taxableSupplyDate.Month);
        var year = taxableSupplyDate.Year.ToString(CultureInfo.InvariantCulture);
        return text
            .Replace(MonthPlaceholder, month, StringComparison.OrdinalIgnoreCase)
            .Replace("{mesic}", month, StringComparison.OrdinalIgnoreCase)
            .Replace(YearPlaceholder, year, StringComparison.OrdinalIgnoreCase);
    }
}
