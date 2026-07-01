namespace Dph.Core.Domain;

public sealed class InvoiceLine
{
    public long Id { get; set; }
    public long PeriodId { get; set; }
    public long? IssuedInvoiceId { get; set; }
    public InvoiceKind Kind { get; set; }
    public long? CounterpartyId { get; set; }
    public string CounterpartyName { get; set; } = "";
    public string? CounterpartyDic { get; set; }
    public string EvidenceNumber { get; set; } = "";
    public DateOnly TaxableSupplyDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal TaxBaseCzk { get; set; }
    public decimal VatCzk { get; set; }
    public string Currency { get; set; } = "CZK";
    public decimal? ForeignAmount { get; set; }
    public decimal? ExchangeRate { get; set; }
    public VatRateKind VatRate { get; set; } = VatRateKind.Standard21;
    public bool PartialDeduction { get; set; }
    public string? Note { get; set; }

    public decimal GrossCzk => TaxBaseCzk + VatCzk;
}
