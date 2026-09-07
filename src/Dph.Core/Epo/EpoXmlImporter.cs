using System.Xml.Linq;
using Dph.Core.Domain;

namespace Dph.Core.Epo;

public sealed class EpoXmlImporter
{
    public ImportedEpoData ImportDirectory(string directory)
    {
        var result = new ImportedEpoData();
        var failedStatements = new HashSet<(string Dic, int Year, int Month)>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            try
            {
                ImportFile(file, result);
            }
            catch
            {
                result.SkippedFiles.Add(file);
                // Jen nečitelné KH identifikovaného poplatníka může zneplatnit jeho evidenci.
                // Rozsah vyhodnotíme až po načtení složky, nezávisle na pořadí souborů.
                try
                {
                    var form = XDocument.Load(file).Root?.Element("DPHKH1");
                    var header = form?.Element("VetaD");
                    var dic = NormalizeCzechDic(form?.Element("VetaP")?.Attribute("dic")?.Value ?? "");
                    if (InvoiceKindClassifier.IsCzechDic(dic)
                        && int.TryParse(header?.Attribute("rok")?.Value, out var year)
                        && int.TryParse(header?.Attribute("mesic")?.Value, out var month))
                        failedStatements.Add((dic, year, month));
                }
                catch { /* Bez čitelné identity soubor nemůže měnit jiné podání. */ }
            }
        }

        var selectedDic = NormalizeCzechDic(result.Subject?.Dic ?? "");
        foreach (var failed in failedStatements.Where(x => x.Dic == selectedDic))
        {
            result.Periods.RemoveAll(x => x.Period.Year == failed.Year && x.Period.Month == failed.Month);
            result.Warnings.Add($"Období {failed.Year:D4}-{failed.Month:D2} nebylo importováno: KH subjektu {failed.Dic} obsahuje chybu nebo nejednoznačnou historii.");
        }
        return result;
    }

    public void ImportFile(string file, ImportedEpoData result)
    {
        var document = XDocument.Load(file);
        var formElement = document.Root?.Elements().FirstOrDefault();
        var period = ReadPeriod(formElement);
        if (formElement?.Name.LocalName is not ("DPHKH1" or "DPHDP3") || period is null || period.Year < 2024)
            throw new FormatException("Import podporuje měsíční přiznání a KH od roku 2024.");

        var isControlStatement = formElement.Name.LocalName == "DPHKH1";
        var parsed = new ImportedPeriod(period);
        if (isControlStatement)
        {
            if (formElement.Elements().Any(x => x.Name.LocalName is "VetaA1" or "VetaA3" or "VetaB1")
                || formElement.Elements().Any(x => Attr(x, "zdph_44") is not ("" or "N")
                    || Attr(x, "kod_rezim_pl") is not ("" or "0")))
                throw new FormatException("KH obsahuje nepodporovaný režim; nelze importovat jen část jeho plnění.");
            ImportControlStatementInvoices(document, parsed);
        }

        var subject = document.Descendants("VetaP").FirstOrDefault();
        if (subject is not null && result.Subject is not null
            && NormalizeCzechDic(Attr(subject, "dic")) != NormalizeCzechDic(result.Subject.Dic))
            throw new FormatException("Složka obsahuje podání různých daňových subjektů.");

        var existing = result.Periods.FirstOrDefault(x => x.Period.Year == period.Year && x.Period.Month == period.Month);
        var replaceInvoices = isControlStatement;
        if (isControlStatement && existing?.ControlStatement is { } previous)
        {
            var previousPeriod = ReadPeriod(previous)!;
            var order = period.SubmissionDate.CompareTo(previousPeriod.SubmissionDate);
            if (order == 0) order = FormOrder(period.FormType).CompareTo(FormOrder(previousPeriod.FormType));
            if (order == 0 && !XNode.DeepEquals(formElement, previous))
                throw new FormatException("Různá KH mají stejné datum a druh podání. Importujte jednoznačně poslední podané KH.");
            replaceInvoices = order > 0;
        }
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
            if (replaceInvoices)
            {
                importedPeriod.Invoices.Clear();
                importedPeriod.Invoices.AddRange(parsed.Invoices);
                importedPeriod.ControlStatement = new XElement(formElement);
                importedPeriod.Period.SubmissionDate = period.SubmissionDate;
                importedPeriod.Period.FormType = period.FormType;
            }
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

    private static int FormOrder(string form) => form switch { "B" => 0, "O" => 1, "N" => 2, "E" => 3, _ => -1 };

    private static string Attr(XElement element, string name) => element.Attribute(name)?.Value?.Trim() ?? "";
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string NormalizeCzechDic(string value)
    {
        var normalized = InvoiceKindClassifier.NormalizeVatId(value);
        return normalized.Length == 0 || normalized.StartsWith("CZ", StringComparison.Ordinal)
            ? normalized : "CZ" + normalized;
    }

    private static VatPeriod? ReadPeriod(XElement? formElement)
    {
        var header = formElement?.Element("VetaD");
        if (header is null)
        {
            return null;
        }

        if (!int.TryParse(Attr(header, "rok"), out var year)
            || !int.TryParse(Attr(header, "mesic"), out var month)
            || month is < 1 or > 12 || year is < 1 or > 9999)
        {
            return null;
        }

        return new VatPeriod
        {
            Year = year,
            Month = month,
            SubmissionDate = ParseEpoDate(Attr(header, "d_poddp")) ?? throw new FormatException("Chybí platné datum vyhotovení XML."),
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
            // B.2 pod limitem musí zachovat výslovné zařazení z podaného XML.
            // Nad limitem stačí částky; trvalý override by po jejich úpravě byl zavádějící.
            var reportedGross = new[] { "zakl_dane1", "dan1", "zakl_dane2", "dan2" }
                .Sum(attribute => ParseMoney(Attr(element, attribute)));

            AddRateLines(element, period, () => new InvoiceLine
            {
                Kind = InvoiceKind.ReceivedDomesticWithVat,
                CounterpartyDic = NormalizeCzechDic(Attr(element, "dic_dod")),
                CounterpartyName = NormalizeCzechDic(Attr(element, "dic_dod")),
                EvidenceNumber = Attr(element, "c_evid_dd"),
                TaxableSupplyDate = ParseDate(Attr(element, "dppd"), period.Period),
                DocumentAboveControlLimit = Math.Abs(reportedGross) <= EpoTaxFormDefinition.Current.ControlStatementDetailLimitCzk,
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
        AddRateLine(element, period, create, "zakl_dane1", "dan1", VatRateKind.Standard21);
        AddRateLine(element, period, create, "zakl_dane2", "dan2", VatRateKind.Reduced12);
        if (ParseMoney(Attr(element, "zakl_dane3")) != 0 || ParseMoney(Attr(element, "dan3")) != 0)
            throw new FormatException("Historická sazba ve třetím sloupci KH není podporována; nelze ji převést na 12 %.");
    }

    private static void AddRateLine(
        XElement element,
        ImportedPeriod period,
        Func<InvoiceLine> create,
        string baseAttr,
        string vatAttr,
        VatRateKind inferredRate)
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
        // Autoritou je sloupec KH. Poměr daně a základu může změnit zaokrouhlení
        // nebo omezení nároku na odpočet (např. u osobního automobilu).
        line.VatRate = inferredRate;
        period.Invoices.Add(line);
    }

    private static DateOnly ParseDate(string value, VatPeriod period)
        => ParseEpoDate(value) ?? throw new FormatException($"Neplatné datum plnění: {value}.");

    // EPO zapisuje data ve tvaru "dd.MM.yyyy" – parsování nesmí záviset na jazyku systému.
    private static DateOnly? ParseEpoDate(string value)
        => DateOnly.TryParseExact(value, "d.M.yyyy", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static DateOnly LastDay(VatPeriod period)
        => new(period.Year, period.Month, DateTime.DaysInMonth(period.Year, period.Month));

    private static decimal ParseMoney(string value)
        => string.IsNullOrWhiteSpace(value) ? 0m : Calculations.DecimalInput.Parse(value);
}

public sealed class ImportedEpoData
{
    public List<string> Warnings { get; } = [];
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
    internal XElement? ControlStatement { get; set; }
}
