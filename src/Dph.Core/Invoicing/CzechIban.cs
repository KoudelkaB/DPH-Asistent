using System.Globalization;
using System.Numerics;

namespace Dph.Core.Invoicing;

// Sestavení českého IBAN z čísla účtu ve tvaru "[předčíslí-]číslo/kódbanky".
// CZ IBAN = "CZ" + 2 kontrolní číslice + 4 číslice kódu banky + 6 číslic předčíslí + 10 číslic účtu.
public static class CzechIban
{
    public static string? TryFromAccount(string? account)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return null;
        }

        var trimmed = account.Trim();

        // Když uživatel zadá rovnou IBAN, jen ho znormalizujeme.
        var compact = trimmed.Replace(" ", "", StringComparison.Ordinal);
        if (compact.StartsWith("CZ", StringComparison.OrdinalIgnoreCase) && compact.Length == 24)
        {
            return compact.ToUpperInvariant();
        }

        var slash = trimmed.IndexOf('/');
        if (slash <= 0 || slash == trimmed.Length - 1)
        {
            return null;
        }

        var bankCode = new string(trimmed[(slash + 1)..].Where(char.IsDigit).ToArray());
        var accountPart = trimmed[..slash];
        var dash = accountPart.IndexOf('-');
        var prefix = dash >= 0 ? new string(accountPart[..dash].Where(char.IsDigit).ToArray()) : "";
        var number = new string((dash >= 0 ? accountPart[(dash + 1)..] : accountPart).Where(char.IsDigit).ToArray());

        if (bankCode.Length != 4 || number.Length == 0 || number.Length > 10 || prefix.Length > 6)
        {
            return null;
        }

        var bban = bankCode + prefix.PadLeft(6, '0') + number.PadLeft(10, '0');
        var check = ComputeCheckDigits(bban);
        return $"CZ{check:D2}{bban}";
    }

    // Kontrolní číslice IBAN podle ISO 13616 / ISO 7064 (mod-97-10).
    private static int ComputeCheckDigits(string bban)
    {
        // Přesuneme "CZ00" na konec a "CZ" převedeme na číslice (C=12, Z=35).
        var rearranged = bban + "123500";
        var remainder = BigInteger.Parse(rearranged, CultureInfo.InvariantCulture) % 97;
        return (int)(98 - remainder);
    }
}
