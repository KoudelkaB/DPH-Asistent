using System.Globalization;
using System.Text;

namespace Dph.Core.Invoicing;

// QR Platba (Short Payment Descriptor, SPAYD 1.0). Výsledný řetězec se zakóduje do QR kódu.
// Formát: SPD*1.0*ACC:<IBAN>*AM:<částka>*CC:<měna>*X-VS:<VS>*MSG:<zpráva>
public static class SpaydBuilder
{
    public static string Build(string iban, decimal amount, string currency, string? variableSymbol, string? message)
    {
        var builder = new StringBuilder("SPD*1.0");
        builder.Append("*ACC:").Append(Sanitize(iban));
        builder.Append("*AM:").Append(amount.ToString("0.00", CultureInfo.InvariantCulture));
        builder.Append("*CC:").Append(Sanitize(string.IsNullOrWhiteSpace(currency) ? "CZK" : currency.ToUpperInvariant()));

        var vs = string.IsNullOrWhiteSpace(variableSymbol) ? null : new string(variableSymbol.Where(char.IsDigit).ToArray());
        if (!string.IsNullOrEmpty(vs))
        {
            builder.Append("*X-VS:").Append(vs);
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            builder.Append("*MSG:").Append(SanitizeMessage(message));
        }

        return builder.ToString();
    }

    // Hodnoty nesmí obsahovat oddělovač '*'; u zprávy navíc zkrátíme na 60 znaků (limit SPAYD).
    private static string Sanitize(string value) => value.Replace("*", "", StringComparison.Ordinal).Trim();

    private static string SanitizeMessage(string value)
    {
        var cleaned = Sanitize(value);
        return cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }
}
