// ID datových schránek finančních úřadů dle oficiálního seznamu Finanční správy
// „Identifikační kódy orgánů finanční správy ČR“
// (https://financnisprava.gov.cz/assets/cs/prilohy/de-datove-schranky/Identifikacni_kody_FR_a_FU.pdf).
// Podání se posílá finančnímu úřadu (kraji), ne územnímu pracovišti – to se v podání uvádí jen
// v XML (c_pracufo), adresátem datové zprávy je vždy schránka FÚ.
namespace Dph.Core.Isds;

public static class TaxOfficeDataBoxes
{
    private static readonly Dictionary<string, string> ByOfficeCode = new()
    {
        ["451"] = "7nyn2d9", // Finanční úřad pro hlavní město Prahu
        ["452"] = "6sxny3p", // Finanční úřad pro Středočeský kraj
        ["453"] = "scdnz6b", // Finanční úřad pro Jihočeský kraj
        ["454"] = "gf9n2e3", // Finanční úřad pro Plzeňský kraj
        ["455"] = "q9in2ev", // Finanční úřad pro Karlovarský kraj
        ["456"] = "qjfn2bj", // Finanční úřad pro Ústecký kraj
        ["457"] = "zcqn2bf", // Finanční úřad pro Liberecký kraj
        ["458"] = "fj8ny4i", // Finanční úřad pro Královéhradecký kraj
        ["459"] = "95zn2bb", // Finanční úřad pro Pardubický kraj
        ["460"] = "tqjn2cy", // Finanční úřad pro Kraj Vysočina
        ["461"] = "qdhny4c", // Finanční úřad pro Jihomoravský kraj
        ["462"] = "25nnz67", // Finanční úřad pro Olomoucký kraj
        ["463"] = "4hun2cu", // Finanční úřad pro Moravskoslezský kraj
        ["464"] = "n69nz5y", // Finanční úřad pro Zlínský kraj
        ["13"] = "7cs8cge",  // Specializovaný finanční úřad
    };

    public static IReadOnlyDictionary<string, string> All => ByOfficeCode;

    // null = číselník kód nezná (např. živý číselník ADIS přidal nový úřad); volající pak požádá
    // o ruční zadání ID schránky místo toho, aby poslal podání někam jinam.
    public static string? For(string? taxOfficeCode)
        => string.IsNullOrWhiteSpace(taxOfficeCode)
            ? null
            : ByOfficeCode.GetValueOrDefault(taxOfficeCode.Trim());

    public static bool IsValidId(string? dataBoxId)
        => dataBoxId is { Length: 7 } && dataBoxId.All(char.IsLetterOrDigit);
}
