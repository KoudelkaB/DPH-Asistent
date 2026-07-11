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

        // Zahraniční dodavatelé z A.2 (reverse charge) – jen ti s EU VAT ID; třetí země nemá
        // v KH žádnou identifikaci.
        foreach (var element in document.Descendants("VetaA2"))
        {
            var state = Attr(element, "k_stat");
            var vatId = Attr(element, "vatid_dod");
            if (state.Length == 0 || vatId.Length == 0)
            {
                continue;
            }

            var dic = $"{state}{vatId}";
            result.Counterparties.TryAdd(dic, new Counterparty
            {
                Dic = dic,
                Name = dic,
                CountryCode = state.ToUpperInvariant(),
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

        return new VatPeriod
        {
            Year = year,
            Month = month,
            SubmissionDate = ParseEpoDate(Attr(header, "d_poddp")) ?? DateOnly.FromDateTime(DateTime.Today),
            FormType = EmptyToNull(Attr(header, "khdph_forma")) ?? EmptyToNull(Attr(header, "dapdph_forma")) ?? "B"
        };
    }

    private static void ImportControlStatementInvoices(XDocument document, ImportedPeriod period)
    {
        // A.2 – přijatá plnění s daní příjemce (reverse charge, ř. 3–6, 9, 12, 13 přiznání).
        foreach (var element in document.Descendants("VetaA2"))
        {
            var vatId = $"{Attr(element, "k_stat")}{Attr(element, "vatid_dod")}".Trim();
            AddRateLines(element, period, () => new InvoiceLine
            {
                Kind = InvoiceKind.ReverseCharge,
                CounterpartyDic = EmptyToNull(vatId),
                CounterpartyName = vatId.Length > 0 ? vatId : "Zahraniční dodavatel",
                EvidenceNumber = Attr(element, "c_evid_dd"),
                TaxableSupplyDate = ParseDate(Attr(element, "dppd"), period.Period)
            });
        }

        foreach (var element in document.Descendants("VetaA4"))
        {
            AddRateLines(element, period, () => new InvoiceLine
            {
                Kind = InvoiceKind.IssuedDomestic,
                CounterpartyDic = NormalizeCzechDic(Attr(element, "dic_odb")),
                CounterpartyName = NormalizeCzechDic(Attr(element, "dic_odb")),
                EvidenceNumber = Attr(element, "c_evid_dd"),
                TaxableSupplyDate = ParseDate(Attr(element, "dppd"), period.Period)
            });
        }

        foreach (var element in document.Descendants("VetaA5"))
        {
            AddRateLines(element, period, () => new InvoiceLine
            {
                Kind = InvoiceKind.IssuedDomestic,
                CounterpartyName = "Souhrn malých vydaných dokladů",
                EvidenceNumber = "A5",
                TaxableSupplyDate = LastDay(period.Period)
            });
        }

        foreach (var element in document.Descendants("VetaB2"))
        {
            AddRateLines(element, period, () => new InvoiceLine
            {
                Kind = InvoiceKind.ReceivedDomesticWithVat,
                CounterpartyDic = NormalizeCzechDic(Attr(element, "dic_dod")),
                CounterpartyName = NormalizeCzechDic(Attr(element, "dic_dod")),
                EvidenceNumber = Attr(element, "c_evid_dd"),
                TaxableSupplyDate = ParseDate(Attr(element, "dppd"), period.Period),
                PartialDeduction = string.Equals(Attr(element, "pomer"), "A", StringComparison.OrdinalIgnoreCase)
            });
        }

        foreach (var element in document.Descendants("VetaB3"))
        {
            AddRateLines(element, period, () => new InvoiceLine
            {
                Kind = InvoiceKind.ReceivedDomesticWithVat,
                CounterpartyName = "Souhrn malých přijatých dokladů",
                EvidenceNumber = "B3",
                TaxableSupplyDate = LastDay(period.Period)
            });
        }
    }

    // KH vede základ+daň ve sloupcích podle sazby (1 = základní, 2/3 = snížené). Za každý neprázdný
    // sloupec vznikne jeden řádek tabulky, stejně jako je exportér zpátky slučuje do jednoho dokladu.
    private static void AddRateLines(XElement element, ImportedPeriod period, Func<InvoiceLine> create)
    {
        AddRateLine(element, period, create, "zakl_dane1", "dan1", inferredRate: null);
        AddRateLine(element, period, create, "zakl_dane2", "dan2", VatRateKind.Reduced12);
        AddRateLine(element, period, create, "zakl_dane3", "dan3", VatRateKind.Reduced12);
    }

    private static void AddRateLine(
        XElement element,
        ImportedPeriod period,
        Func<InvoiceLine> create,
        string baseAttr,
        string vatAttr,
        VatRateKind? inferredRate)
    {
        var baseCzk = ParseMoney(Attr(element, baseAttr));
        var vatCzk = ParseMoney(Attr(element, vatAttr));
        if (baseCzk == 0 && vatCzk == 0)
        {
            return;
        }

        var line = create();
        line.TaxBaseCzk = baseCzk;
        line.VatCzk = vatCzk;
        // Sloupec 1 je podle pokynů základní sazba, ale starší exporty této aplikace do něj psaly
        // i sníženou – proto se sazba dopočítá z poměru daně a základu.
        line.VatRate = inferredRate ?? InferVatRate(baseCzk, vatCzk);
        period.Invoices.Add(line);
    }

    // KH carries only base + VAT amounts, not the rate. Recover it from the ratio so later edits
    // in the UI recompute VAT at the right rate instead of defaulting everything to 21 %.
    private static VatRateKind InferVatRate(decimal baseCzk, decimal vatCzk)
    {
        if (baseCzk == 0m)
        {
            return VatRateKind.Standard21;
        }

        // Math.Abs kvůli opravným dokladům (záporný základ i daň) – poměr sazby je stejný.
        return Math.Abs(vatCzk / baseCzk) switch
        {
            < 0.06m => VatRateKind.Zero0,
            < 0.165m => VatRateKind.Reduced12,
            _ => VatRateKind.Standard21
        };
    }

    private static DateOnly ParseDate(string value, VatPeriod period)
        => ParseEpoDate(value) ?? LastDay(period);

    // EPO zapisuje data ve tvaru "dd.MM.yyyy" – parsování nesmí záviset na jazyku systému.
    private static DateOnly? ParseEpoDate(string value)
        => DateOnly.TryParseExact(value, "d.M.yyyy", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

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
