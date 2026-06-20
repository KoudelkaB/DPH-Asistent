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
        var formElement = document.Root?.Elements().FirstOrDefault();
        var period = ReadPeriod(formElement);
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

        if (period is not null)
        {
            var importedPeriod = result.GetOrAddPeriod(period);
            ImportControlStatementInvoices(document, importedPeriod);
        }

        foreach (var customer in document.Descendants("VetaA4").Select(x => Attr(x, "dic_odb")).Where(x => x.Length > 0))
        {
            var dic = NormalizeCzechDic(customer);
            result.Counterparties.TryAdd(dic, new Counterparty
            {
                Dic = dic,
                Ico = Services.AresClient.TryGetIcoFromDic(dic),
                Name = dic,
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
                Name = dic,
                CountryCode = "CZ",
                Role = CounterpartyRole.Supplier
            });
        }
    }

    private static string Attr(XElement element, string name) => element.Attribute(name)?.Value?.Trim() ?? "";
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string NormalizeCzechDic(string value)
        => value.StartsWith("CZ", StringComparison.OrdinalIgnoreCase) ? value : "CZ" + value;

    private static VatPeriod? ReadPeriod(XElement? formElement)
    {
        var header = formElement?.Element("VetaD");
        if (header is null)
        {
            return null;
        }

        if (!int.TryParse(Attr(header, "rok"), out var year)
            || !int.TryParse(Attr(header, "mesic"), out var month)
            || month is < 1 or > 12)
        {
            return null;
        }

        var submitted = DateOnly.TryParse(Attr(header, "d_poddp"), out var submissionDate)
            ? submissionDate
            : DateOnly.FromDateTime(DateTime.Today);

        return new VatPeriod
        {
            Year = year,
            Month = month,
            SubmissionDate = submissionDate,
            FormType = EmptyToNull(Attr(header, "khdph_forma")) ?? EmptyToNull(Attr(header, "dapdph_forma")) ?? "B"
        };
    }

    private static void ImportControlStatementInvoices(XDocument document, ImportedPeriod period)
    {
        foreach (var element in document.Descendants("VetaA4"))
        {
            period.Invoices.Add(new InvoiceLine
            {
                Kind = InvoiceKind.IssuedDomestic,
                CounterpartyDic = NormalizeCzechDic(Attr(element, "dic_odb")),
                CounterpartyName = NormalizeCzechDic(Attr(element, "dic_odb")),
                EvidenceNumber = Attr(element, "c_evid_dd"),
                TaxableSupplyDate = ParseDate(Attr(element, "dppd"), period.Period),
                TaxBaseCzk = ParseMoney(Attr(element, "zakl_dane1")),
                VatCzk = ParseMoney(Attr(element, "dan1"))
            });
        }

        foreach (var element in document.Descendants("VetaA5"))
        {
            period.Invoices.Add(new InvoiceLine
            {
                Kind = InvoiceKind.IssuedDomestic,
                CounterpartyName = "Souhrn malých vydaných dokladů",
                EvidenceNumber = "A5",
                TaxableSupplyDate = LastDay(period.Period),
                TaxBaseCzk = ParseMoney(Attr(element, "zakl_dane1")),
                VatCzk = ParseMoney(Attr(element, "dan1"))
            });
        }

        foreach (var element in document.Descendants("VetaB2"))
        {
            period.Invoices.Add(new InvoiceLine
            {
                Kind = InvoiceKind.ReceivedDomesticWithVat,
                CounterpartyDic = NormalizeCzechDic(Attr(element, "dic_dod")),
                CounterpartyName = NormalizeCzechDic(Attr(element, "dic_dod")),
                EvidenceNumber = Attr(element, "c_evid_dd"),
                TaxableSupplyDate = ParseDate(Attr(element, "dppd"), period.Period),
                TaxBaseCzk = ParseMoney(Attr(element, "zakl_dane1")),
                VatCzk = ParseMoney(Attr(element, "dan1"))
            });
        }

        foreach (var element in document.Descendants("VetaB3"))
        {
            period.Invoices.Add(new InvoiceLine
            {
                Kind = InvoiceKind.ReceivedDomesticWithVat,
                CounterpartyName = "Souhrn malých přijatých dokladů",
                EvidenceNumber = "B3",
                TaxableSupplyDate = LastDay(period.Period),
                TaxBaseCzk = ParseMoney(Attr(element, "zakl_dane1")),
                VatCzk = ParseMoney(Attr(element, "dan1"))
            });
        }
    }

    private static DateOnly ParseDate(string value, VatPeriod period)
        => DateOnly.TryParse(value, out var parsed) ? parsed : LastDay(period);

    private static DateOnly LastDay(VatPeriod period)
        => new(period.Year, period.Month, DateTime.DaysInMonth(period.Year, period.Month));

    private static decimal ParseMoney(string value)
        => decimal.TryParse(value.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
}

public sealed class ImportedEpoData
{
    public TaxSubject? Subject { get; set; }
    public Dictionary<string, Counterparty> Counterparties { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ImportedPeriod> Periods { get; } = [];
    public List<string> SkippedFiles { get; } = [];

    public ImportedPeriod GetOrAddPeriod(VatPeriod period)
    {
        var existing = Periods.FirstOrDefault(x => x.Period.Year == period.Year && x.Period.Month == period.Month);
        if (existing is not null)
        {
            return existing;
        }

        var imported = new ImportedPeriod(period);
        Periods.Add(imported);
        return imported;
    }
}

public sealed class ImportedPeriod(VatPeriod period)
{
    public VatPeriod Period { get; } = period;
    public List<InvoiceLine> Invoices { get; } = [];
}
