using Dph.Core.Domain;

namespace Dph.Core.Calculations;

public sealed class VatReturnSummary
{
    public decimal DomesticOutputBase { get; init; }
    public decimal DomesticOutputVat { get; init; }
    public decimal DomesticInputBase { get; init; }
    public decimal DomesticInputVat { get; init; }
    public decimal ReverseChargeBase { get; init; }
    public decimal ReverseChargeVat { get; init; }
    public decimal TaxDue => DomesticOutputVat + ReverseChargeVat;
    public decimal TaxDeduction => DomesticInputVat + ReverseChargeVat;
    public decimal NetTax => TaxDue - TaxDeduction;
    public decimal ControlStatementOutputBase => DomesticOutputBase;
    public decimal ControlStatementInputBase => DomesticInputBase;
}

public sealed class VatCalculator
{
    public const decimal StandardRate = 0.21m;

    public VatReturnSummary Calculate(IEnumerable<InvoiceLine> invoices)
    {
        var lines = invoices.ToArray();
        return new VatReturnSummary
        {
            DomesticOutputBase = Sum(lines, InvoiceKind.IssuedDomestic, x => x.TaxBaseCzk),
            DomesticOutputVat = Sum(lines, InvoiceKind.IssuedDomestic, x => ResolveVat(x)),
            DomesticInputBase = Sum(lines, InvoiceKind.ReceivedDomesticWithVat, x => x.TaxBaseCzk),
            DomesticInputVat = Sum(lines, InvoiceKind.ReceivedDomesticWithVat, x => ResolveVat(x)),
            ReverseChargeBase = Sum(lines, InvoiceKind.ReverseCharge, x => x.TaxBaseCzk),
            ReverseChargeVat = Sum(lines, InvoiceKind.ReverseCharge, x => ResolveVat(x))
        };
    }

    public decimal ResolveVat(InvoiceLine invoice)
    {
        if (invoice.VatCzk != 0)
        {
            return Money(invoice.VatCzk);
        }

        return invoice.Kind == InvoiceKind.ReverseCharge
            ? Money(invoice.TaxBaseCzk * Rate(invoice.VatRate))
            : 0m;
    }

    public static decimal Rate(VatRateKind rate) => rate switch
    {
        VatRateKind.Reduced12 => 0.12m,
        VatRateKind.Zero0 => 0m,
        _ => StandardRate
    };

    public static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static long WholeCrowns(decimal value) => (long)Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static decimal Sum(IEnumerable<InvoiceLine> lines, InvoiceKind kind, Func<InvoiceLine, decimal> selector)
        => Money(lines.Where(x => x.Kind == kind).Sum(selector));
}
