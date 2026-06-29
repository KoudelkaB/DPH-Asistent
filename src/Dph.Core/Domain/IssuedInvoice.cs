using Dph.Core.Calculations;

namespace Dph.Core.Domain;

// Plnohodnotná vydaná faktura (daňový doklad) – nadmnožina toho, co se používá pro DPH.
// Do tabulky DPH se z ní generují řádky InvoiceKind.IssuedDomestic (jeden na každou sazbu).
public sealed class IssuedInvoice
{
    public long Id { get; set; }

    // Číslo faktury ve tvaru RRRR#### (rok + pořadí), např. "20260001".
    public string Number { get; set; } = "";

    public DateOnly IssueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly TaxableSupplyDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly DueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today).AddDays(14);

    // Odběratel
    public long? CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string? CustomerIco { get; set; }
    public string? CustomerDic { get; set; }
    public string? CustomerStreet { get; set; }
    public string? CustomerHouseNumber { get; set; }
    public string? CustomerCity { get; set; }
    public string? CustomerPostalCode { get; set; }
    public string CustomerCountry { get; set; } = "Česká republika";

    // Platba
    public string Currency { get; set; } = "CZK";
    public string? VariableSymbol { get; set; }
    public string? PaymentMethod { get; set; } = "Převodem";

    // Úvodní text nad položkami, např. "Za červen 2026 Vám fakturujeme:".
    public string? IntroText { get; set; }
    public string? Note { get; set; }
    public string? Footer { get; set; }

    public List<IssuedInvoiceItem> Items { get; set; } = [];

    public decimal TotalBaseCzk => VatCalculator.Money(Items.Sum(x => x.LineBaseCzk));
    public decimal TotalVatCzk => VatCalculator.Money(Items.Sum(x => x.LineVatCzk));
    public decimal TotalGrossCzk => VatCalculator.Money(TotalBaseCzk + TotalVatCzk);

    // Variabilní symbol pro platbu: zadaný, jinak číslice z čísla faktury.
    public string PaymentVariableSymbol
        => string.IsNullOrWhiteSpace(VariableSymbol)
            ? new string(Number.Where(char.IsDigit).ToArray())
            : new string(VariableSymbol.Where(char.IsDigit).ToArray());

    // Rekapitulace DPH po sazbách (základ + daň), seřazená sestupně podle sazby.
    public IReadOnlyList<VatRateTotal> VatRecap()
        => Items
            .GroupBy(x => x.VatRate)
            .Select(g => new VatRateTotal(
                g.Key,
                VatCalculator.Money(g.Sum(x => x.LineBaseCzk)),
                VatCalculator.Money(g.Sum(x => x.LineVatCzk))))
            .OrderByDescending(x => VatCalculator.Rate(x.Rate))
            .ToList();
}

public sealed class IssuedInvoiceItem
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1m;
    public string Unit { get; set; } = "ks";
    public decimal UnitPriceCzk { get; set; }
    public VatRateKind VatRate { get; set; } = VatRateKind.Standard21;
    public int SortOrder { get; set; }

    public decimal LineBaseCzk => VatCalculator.Money(Quantity * UnitPriceCzk);
    public decimal LineVatCzk => VatCalculator.Money(LineBaseCzk * VatCalculator.Rate(VatRate));
    public decimal LineGrossCzk => VatCalculator.Money(LineBaseCzk + LineVatCzk);
}

public sealed record VatRateTotal(VatRateKind Rate, decimal BaseCzk, decimal VatCzk)
{
    public decimal GrossCzk => BaseCzk + VatCzk;
}
