using System.Xml.Linq;

namespace Dph.Core.Epo;

public sealed record TaxOfficeCatalogData(
    IReadOnlyList<TaxOffice> Offices,
    IReadOnlyList<TaxOfficeWorkplace> Workplaces);

public interface ITaxOfficeCatalog
{
    Task<TaxOfficeCatalogData?> LoadAsync(CancellationToken cancellationToken = default);
}

// Živý číselník finančních úřadů a územních pracovišť z rozhraní MOJE daně (ADIS). Vrací jen
// aktuálně platné položky (bez data zániku). Při chybě vrací null – volající použije zabudovaný
// číselník (TaxOfficeDirectory) jako zálohu.
public sealed class MfcrTaxOfficeCatalog(HttpClient httpClient) : ITaxOfficeCatalog
{
    private const string BaseUrl = "https://adisspr.mfcr.cz/dpr/epo_ciselnik";

    public async Task<TaxOfficeCatalogData?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var offices = await LoadOfficesAsync(cancellationToken);
        if (offices.Count == 0)
        {
            return null;
        }

        var workplaces = await LoadWorkplacesAsync(cancellationToken);
        return workplaces.Count == 0 ? null : new TaxOfficeCatalogData(offices, workplaces);
    }

    private async Task<List<TaxOffice>> LoadOfficesAsync(CancellationToken cancellationToken)
    {
        var document = await LoadAsync("ufo", cancellationToken);
        var offices = new List<TaxOffice>();
        if (document?.Root is null)
        {
            return offices;
        }

        foreach (var veta in document.Root.Elements("Veta").Where(IsCurrent))
        {
            var code = veta.Attribute("c_ufo")?.Value;
            var name = veta.Attribute("nazu_ufo")?.Value;
            // Kód 0 = Generální finanční ředitelství, kam se přiznání nepodává.
            if (string.IsNullOrEmpty(code) || code == "0" || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            offices.Add(new TaxOffice(code, name.Trim()));
        }

        // Kraje 451–464 vzestupně, Specializovaný FÚ (13) na konec.
        return offices
            .OrderBy(x => x.Code == "13" ? 1 : 0)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<List<TaxOfficeWorkplace>> LoadWorkplacesAsync(CancellationToken cancellationToken)
    {
        var document = await LoadAsync("pracufo", cancellationToken);
        var workplaces = new List<TaxOfficeWorkplace>();
        if (document?.Root is null)
        {
            return workplaces;
        }

        foreach (var veta in document.Root.Elements("Veta").Where(IsCurrent))
        {
            var code = veta.Attribute("c_pracufo")?.Value;
            var officeCode = veta.Attribute("c_ufo")?.Value;
            var name = veta.Attribute("nazu_pracufo")?.Value;
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(officeCode) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            workplaces.Add(new TaxOfficeWorkplace(code, officeCode, name.Trim()));
        }

        return workplaces;
    }

    private static bool IsCurrent(XElement veta)
        => string.IsNullOrEmpty(veta.Attribute("d_zaniku")?.Value);

    private async Task<XDocument?> LoadAsync(string code, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"{BaseUrl}?C={code}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        // Při chybě vrací <Chyby>…</Chyby> místo <Ciselnik>.
        return document.Root?.Name.LocalName == "Ciselnik" ? document : null;
    }
}
