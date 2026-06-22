using System.Globalization;
using System.Xml.Linq;
using Dph.Core.Calculations;
using Dph.Core.Domain;

namespace Dph.Core.Epo;

public sealed class EpoXmlExporter(EpoTaxFormDefinition? definition = null)
{
    private readonly EpoTaxFormDefinition _definition = definition ?? EpoTaxFormDefinition.Current;
    private readonly VatCalculator _calculator = new();

    public XDocument ExportVatReturn(TaxSubject subject, VatPeriod period, IEnumerable<InvoiceLine> invoices)
    {
        var lines = invoices.ToArray();
        var summary = _calculator.Calculate(lines);
        var dph = new XElement(_definition.VatReturnElement,
            new XAttribute("verzePis", _definition.VatReturnVersion),
            VatReturnHeader(subject, period),
            TaxSubjectElement(subject));

        // InvoiceKind.ReverseCharge = přijetí služby ze zahraničí, kde daň přiznává příjemce. Řádek
        // se liší podle původu dodavatele (rozpoznáno z prefixu jeho DIČ):
        //   - dodavatel registrovaný v JČS (EU prefix, např. IE u OpenAI Ireland) → §9(1) → ř.5/6
        //     (Veta1 p_sl23_e / p_sl5_e),
        //   - dodavatel ze třetí země / bez EU DIČ (např. GitHub, Anthropic z USA) → §108 → ř.12/13
        //     (Veta1 p_sl23_z / p_sl5_z).
        // Nárok na odpočet u obojího jde na ř.43/44 (Veta4 nar_zdp / od_zdp). NENÍ to tuzemský režim
        // přenesení §92a (ř.10/11 + KH B1) a do kontrolního hlášení tato plnění NEPATŘÍ.
        var rc = lines.Where(x => x.Kind == InvoiceKind.ReverseCharge).ToArray();
        var euStd = RcLine(rc, eu: true, reduced: false);
        var euRed = RcLine(rc, eu: true, reduced: true);
        var nonEuStd = RcLine(rc, eu: false, reduced: false);
        var nonEuRed = RcLine(rc, eu: false, reduced: true);

        var domesticOutputVat = VatCalculator.WholeCrowns(summary.DomesticOutputVat);
        var veta1 = new XElement("Veta1");
        if (summary.DomesticOutputBase != 0 || summary.DomesticOutputVat != 0)
        {
            veta1.Add(
                A("obrat23", VatCalculator.WholeCrowns(summary.DomesticOutputBase)),
                A("dan23", domesticOutputVat),
                A("obrat5", 0),
                A("dan5", 0));
        }

        AddOutputRow(veta1, "p_sl23_e", "dan_psl23_e", euStd);     // ř.5  EU služba, základní sazba
        AddOutputRow(veta1, "p_sl5_e", "dan_psl5_e", euRed);       // ř.6  EU služba, snížená sazba
        AddOutputRow(veta1, "p_sl23_z", "dan_psl23_z", nonEuStd);  // ř.12 třetí země, základní sazba
        AddOutputRow(veta1, "p_sl5_z", "dan_psl5_z", nonEuRed);    // ř.13 třetí země, snížená sazba
        if (veta1.HasAttributes)
        {
            dph.Add(veta1);
        }

        // ř.43/44 odpočet sčítá EU i třetí zemi podle sazby. Počítáme ze stejných zaokrouhlených
        // řádků jako výstup (ne z nového součtu), aby ř.62 a ř.63 za reverse charge přesně seděly.
        var stdDeductBase = euStd.Base + nonEuStd.Base;
        var stdDeductVat = euStd.Vat + nonEuStd.Vat;
        var redDeductBase = euRed.Base + nonEuRed.Base;
        var redDeductVat = euRed.Vat + nonEuRed.Vat;

        var domesticInputVat = VatCalculator.WholeCrowns(summary.DomesticInputVat);
        var veta4 = new XElement("Veta4");
        var hasDeduction = false;
        if (summary.DomesticInputBase != 0 || summary.DomesticInputVat != 0)
        {
            // ř.40/41 – odpočet z tuzemských plnění od plátců.
            veta4.Add(
                A("pln23", VatCalculator.WholeCrowns(summary.DomesticInputBase)),
                A("odp_tuz23_nar", domesticInputVat),
                A("odp_tuz5_nar", 0));
            hasDeduction = true;
        }

        if (stdDeductBase != 0 || stdDeductVat != 0)
        {
            veta4.Add(A("nar_zdp23", stdDeductBase), A("od_zdp23", stdDeductVat));
            hasDeduction = true;
        }

        if (redDeductBase != 0 || redDeductVat != 0)
        {
            veta4.Add(A("nar_zdp5", redDeductBase), A("od_zdp5", redDeductVat));
            hasDeduction = true;
        }

        if (hasDeduction)
        {
            // ř.46 „V plné výši“ = součet odpočtů; musí sednout se součtem výše uvedených řádků.
            veta4.Add(A("odp_sum_nar", domesticInputVat + stdDeductVat + redDeductVat));
            dph.Add(veta4);
        }

        // ř.62/63/64: počítáme ze zaokrouhlených řádků, aby ř.64 = ř.62 - ř.63 přesně sedělo
        // (EPO si tyto součty přepočítává a jinak by podání odmítlo).
        var rcOutputVat = euStd.Vat + euRed.Vat + nonEuStd.Vat + nonEuRed.Vat;
        var taxDueWhole = domesticOutputVat + rcOutputVat;
        var taxDeductionWhole = domesticInputVat + stdDeductVat + redDeductVat;
        var netWhole = taxDueWhole - taxDeductionWhole;
        dph.Add(new XElement("Veta6",
            A("dan_zocelk", taxDueWhole),
            A("odp_zocelk", taxDeductionWhole),
            A(netWhole >= 0 ? "dano_da" : "dano_no", Math.Abs(netWhole)),
            A(netWhole >= 0 ? "dano_no" : "dano_da", 0),
            A("dano", 0)));

        return Wrap(dph);
    }

    private static void AddOutputRow(XElement veta1, string baseAttr, string vatAttr, (long Base, long Vat) row)
    {
        if (row.Base != 0 || row.Vat != 0)
        {
            veta1.Add(A(baseAttr, row.Base), A(vatAttr, row.Vat));
        }
    }

    // Whole-crown (base, vat) for one reverse-charge bucket. eu = dodavatel registrovaný v JČS
    // (EU prefix DIČ), reduced = snížená sazba (12 %); ostatní (vč. 0 %) spadá do základní.
    private (long Base, long Vat) RcLine(InvoiceLine[] reverseCharge, bool eu, bool reduced)
    {
        var bucket = reverseCharge
            .Where(x => IsEuSupplier(x) == eu && (x.VatRate == VatRateKind.Reduced12) == reduced)
            .ToArray();
        return (
            VatCalculator.WholeCrowns(VatCalculator.Money(bucket.Sum(x => x.TaxBaseCzk))),
            VatCalculator.WholeCrowns(VatCalculator.Money(bucket.Sum(_calculator.ResolveVat))));
    }

    // EU VAT prefixy (mimo CZ = tuzemsko); EL = Řecko, XI = Severní Irsko.
    private static readonly HashSet<string> EuVatPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AT", "BE", "BG", "HR", "CY", "DK", "EE", "FI", "FR", "DE", "EL", "HU", "IE", "IT",
        "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE", "XI"
    };

    private static bool IsEuSupplier(InvoiceLine invoice)
    {
        var dic = invoice.CounterpartyDic?.Trim();
        return dic is { Length: >= 2 } && EuVatPrefixes.Contains(dic[..2]);
    }

    public XDocument ExportControlStatement(TaxSubject subject, VatPeriod period, IEnumerable<InvoiceLine> invoices)
    {
        var lines = invoices.ToArray();
        var dph = new XElement(_definition.ControlStatementElement,
            new XAttribute("verzePis", _definition.ControlStatementVersion),
            ControlStatementHeader(period),
            TaxSubjectElement(subject, includeDataBox: true));

        // Pouze tuzemská plnění: vydaná (A4/A5) a přijatá s českou DPH (B2/B3). Reverse charge
        // (přijetí služby od osoby neusazené v tuzemsku, §108) do kontrolního hlášení nepatří.
        var row = 1;
        foreach (var invoice in lines.Where(IsIssuedDetail))
        {
            dph.Add(new XElement("VetaA4",
                A("c_radku", row++),
                A("c_evid_dd", invoice.EvidenceNumber),
                A("dppd", Date(invoice.TaxableSupplyDate)),
                A("dic_odb", StripCz(invoice.CounterpartyDic)),
                A("zakl_dane1", Money(invoice.TaxBaseCzk)),
                A("dan1", Money(_calculator.ResolveVat(invoice))),
                A("kod_rezim_pl", "0"),
                A("zdph_44", "N")));
        }

        var smallIssued = lines.Where(IsIssuedSmall).ToArray();
        if (smallIssued.Length > 0)
        {
            dph.Add(new XElement("VetaA5",
                A("zakl_dane1", Money(smallIssued.Sum(x => x.TaxBaseCzk))),
                A("dan1", Money(smallIssued.Sum(x => _calculator.ResolveVat(x))))));
        }

        row = 1;
        foreach (var invoice in lines.Where(IsReceivedDetail))
        {
            dph.Add(new XElement("VetaB2",
                A("c_radku", row++),
                A("c_evid_dd", invoice.EvidenceNumber),
                A("dppd", Date(invoice.TaxableSupplyDate)),
                A("dic_dod", StripCz(invoice.CounterpartyDic)),
                A("zakl_dane1", Money(invoice.TaxBaseCzk)),
                A("dan1", Money(_calculator.ResolveVat(invoice))),
                A("pomer", invoice.PartialDeduction ? "A" : "N"),
                A("zdph_44", "N")));
        }

        var smallReceived = lines.Where(IsReceivedSmall).ToArray();
        if (smallReceived.Length > 0)
        {
            dph.Add(new XElement("VetaB3",
                A("zakl_dane1", Money(smallReceived.Sum(x => x.TaxBaseCzk))),
                A("dan1", Money(smallReceived.Sum(x => _calculator.ResolveVat(x))))));
        }

        var summary = _calculator.Calculate(lines);
        dph.Add(new XElement("VetaC",
            A("obrat23", Money(summary.ControlStatementOutputBase)),
            A("pln23", Money(summary.ControlStatementInputBase))));

        return Wrap(dph);
    }

    private bool IsIssuedDetail(InvoiceLine invoice)
        => invoice.Kind == InvoiceKind.IssuedDomestic
           && !IsSummary(invoice, "A5")
           && invoice.GrossCzk > _definition.ControlStatementDetailLimitCzk;

    private bool IsIssuedSmall(InvoiceLine invoice)
        => invoice.Kind == InvoiceKind.IssuedDomestic
           && (IsSummary(invoice, "A5") || invoice.GrossCzk <= _definition.ControlStatementDetailLimitCzk);

    private bool IsReceivedDetail(InvoiceLine invoice)
        => invoice.Kind == InvoiceKind.ReceivedDomesticWithVat
           && !IsSummary(invoice, "B3")
           && invoice.GrossCzk > _definition.ControlStatementDetailLimitCzk;

    private bool IsReceivedSmall(InvoiceLine invoice)
        => invoice.Kind == InvoiceKind.ReceivedDomesticWithVat
           && (IsSummary(invoice, "B3") || invoice.GrossCzk <= _definition.ControlStatementDetailLimitCzk);

    private static bool IsSummary(InvoiceLine invoice, string code)
        => string.Equals(invoice.EvidenceNumber, code, StringComparison.OrdinalIgnoreCase);

    private XElement VatReturnHeader(TaxSubject subject, VatPeriod period) => new("VetaD",
        A("dokument", "DP3"),
        A("k_uladis", "DPH"),
        A("dapdph_forma", period.FormType),
        A("mesic", period.Month.ToString("D2", CultureInfo.InvariantCulture)),
        A("rok", period.Year),
        A("d_poddp", Date(period.SubmissionDate)),
        A("typ_platce", "P"),
        A("c_okec", subject.ActivityCode));

    private XElement ControlStatementHeader(VatPeriod period) => new("VetaD",
        A("dokument", "KH1"),
        A("k_uladis", "DPH"),
        A("khdph_forma", period.FormType),
        A("mesic", period.Month),
        A("rok", period.Year),
        A("d_poddp", Date(period.SubmissionDate)));

    private XElement TaxSubjectElement(TaxSubject subject, bool includeDataBox = false)
    {
        var element = new XElement("VetaP",
            A("dic", StripCz(subject.Dic)),
            A("jmeno", subject.FirstName),
            A("prijmeni", subject.LastName),
            A("ulice", subject.Street),
            A("c_pop", subject.HouseNumber),
            A("naz_obce", subject.City),
            A("psc", subject.PostalCode),
            A("stat", subject.Country),
            A("email", subject.Email),
            A("c_telef", subject.Phone),
            A("c_ufo", subject.TaxOfficeCode),
            A("c_pracufo", subject.WorkplaceCode),
            A("typ_ds", "F"));

        if (!string.IsNullOrWhiteSpace(subject.Title))
        {
            element.Add(A("titul", subject.Title));
        }

        if (includeDataBox && !string.IsNullOrWhiteSpace(subject.DataBoxId))
        {
            element.Add(A("id_dats", subject.DataBoxId));
        }

        return element;
    }

    private XDocument Wrap(XElement form) => new(new XDeclaration("1.0", "UTF-8", null),
        new XElement("Pisemnost",
            A("nazevSW", _definition.SoftwareName),
            A("verzeSW", _definition.SoftwareVersion),
            form));

    private static XAttribute A(string name, object? value) => new(name, value ?? "");
    private static string Date(DateOnly value) => value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    private static string Money(decimal value) => VatCalculator.Money(value).ToString("0.##", CultureInfo.InvariantCulture);
    private static string StripCz(string? value) => string.IsNullOrWhiteSpace(value)
        ? ""
        : value.StartsWith("CZ", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
}
