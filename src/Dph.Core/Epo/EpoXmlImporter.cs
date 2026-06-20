using System.Xml.Linq;
using Dph.Core.Domain;

namespace Dph.Core.Epo;

public sealed class EpoXmlImporter
{
    public ImportedEpoData ImportDirectory(string directory)
    {
        var result = new ImportedEpoData();
        foreach (var file in Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories))
        {
            try
            {
                ImportFile(file, result);
            }
            catch
            {
                result.SkippedFiles.Add(file);
            }
        }

        return result;
    }

    public void ImportFile(string file, ImportedEpoData result)
    {
        var document = XDocument.Load(file);
        var subject = document.Descendants("VetaP").FirstOrDefault();
        if (subject is not null && result.Subject is null)
        {
            result.Subject = new TaxSubject
            {
                Dic = Attr(subject, "dic"),
                FirstName = Attr(subject, "jmeno"),
                LastName = Attr(subject, "prijmeni"),
                Title = EmptyToNull(Attr(subject, "titul")),
                Street = Attr(subject, "ulice"),
                HouseNumber = EmptyToNull(Attr(subject, "c_pop")),
                City = Attr(subject, "naz_obce"),
                PostalCode = Attr(subject, "psc"),
                Country = EmptyToNull(Attr(subject, "stat")) ?? "Česká Republika",
                Email = EmptyToNull(Attr(subject, "email")),
                Phone = EmptyToNull(Attr(subject, "c_telef")),
                TaxOfficeCode = Attr(subject, "c_ufo"),
                WorkplaceCode = Attr(subject, "c_pracufo"),
                DataBoxId = EmptyToNull(Attr(subject, "id_dats"))
            };
        }

        foreach (var customer in document.Descendants("VetaA4").Select(x => Attr(x, "dic_odb")).Where(x => x.Length > 0))
        {
            var dic = NormalizeCzechDic(customer);
            result.Counterparties.TryAdd(dic, new Counterparty
            {
                Dic = dic,
                Ico = Services.AresClient.TryGetIcoFromDic(dic),
                CustomName = dic,
                CountryCode = "CZ",
                Role = CounterpartyRole.Customer
            });
        }

        foreach (var supplier in document.Descendants("VetaB2").Select(x => Attr(x, "dic_dod")).Where(x => x.Length > 0))
        {
            var dic = NormalizeCzechDic(supplier);
            result.Counterparties.TryAdd(dic, new Counterparty
            {
                Dic = dic,
                Ico = Services.AresClient.TryGetIcoFromDic(dic),
                CustomName = dic,
                CountryCode = "CZ",
                Role = CounterpartyRole.Supplier
            });
        }
    }

    private static string Attr(XElement element, string name) => element.Attribute(name)?.Value?.Trim() ?? "";
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string NormalizeCzechDic(string value)
        => value.StartsWith("CZ", StringComparison.OrdinalIgnoreCase) ? value : "CZ" + value;
}

public sealed class ImportedEpoData
{
    public TaxSubject? Subject { get; set; }
    public Dictionary<string, Counterparty> Counterparties { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> SkippedFiles { get; } = [];
}
