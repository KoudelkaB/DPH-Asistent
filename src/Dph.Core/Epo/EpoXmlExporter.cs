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

        if (summary.DomesticOutputBase != 0 || summary.DomesticOutputVat != 0)
        {
            dph.Add(new XElement("Veta1",
                A("obrat23", VatCalculator.WholeCrowns(summary.DomesticOutputBase)),
                A("dan23", VatCalculator.WholeCrowns(summary.DomesticOutputVat)),
                A("obrat5", 0),
                A("dan5", 0)));
        }

        if (summary.ReverseChargeBase != 0 || summary.ReverseChargeVat != 0)
        {
            dph.Add(new XElement("Veta2",
                A("pln_rez_pren23", VatCalculator.WholeCrowns(summary.ReverseChargeBase)),
                A("dan_rez_pren23", VatCalculator.WholeCrowns(summary.ReverseChargeVat))));
        }

        if (summary.DomesticInputBase != 0 || summary.DomesticInputVat != 0 || summary.ReverseChargeVat != 0)
        {
            dph.Add(new XElement("Veta4",
                A("pln23", VatCalculator.WholeCrowns(summary.DomesticInputBase)),
                A("odp_tuz23_nar", VatCalculator.WholeCrowns(summary.DomesticInputVat)),
                A("odp_tuz5_nar", 0),
                A("odp_sum_nar", VatCalculator.WholeCrowns(summary.DomesticInputVat + summary.ReverseChargeVat))));
        }

        dph.Add(new XElement("Veta6",
            A("dan_zocelk", VatCalculator.WholeCrowns(summary.TaxDue)),
            A("odp_zocelk", VatCalculator.WholeCrowns(summary.TaxDeduction)),
            A(summary.NetTax >= 0 ? "dano_da" : "dano_no", Math.Abs(VatCalculator.WholeCrowns(summary.NetTax))),
            A(summary.NetTax >= 0 ? "dano_no" : "dano_da", 0),
            A("dano", 0)));

        return Wrap(dph);
    }

    public XDocument ExportControlStatement(TaxSubject subject, VatPeriod period, IEnumerable<InvoiceLine> invoices)
    {
        var lines = invoices.ToArray();
        var dph = new XElement(_definition.ControlStatementElement,
            new XAttribute("verzePis", _definition.ControlStatementVersion),
            ControlStatementHeader(period),
            TaxSubjectElement(subject, includeDataBox: true));

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
                A("pomer", "N"),
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
