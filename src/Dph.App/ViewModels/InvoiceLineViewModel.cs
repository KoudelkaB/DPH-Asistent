using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dph.Core.Domain;

namespace Dph.App.ViewModels;

public partial class InvoiceLineViewModel : ViewModelBase
{
    public string[] KindOptions { get; } =
    [
        InvoiceKind.IssuedDomestic.ToString(),
        InvoiceKind.ReceivedDomesticWithVat.ToString(),
        InvoiceKind.ReverseCharge.ToString()
    ];

    [ObservableProperty] private long id;
    [ObservableProperty] private long periodId;
    [ObservableProperty] private string kind = InvoiceKind.ReceivedDomesticWithVat.ToString();
    [ObservableProperty] private string counterpartyName = "";
    [ObservableProperty] private string counterpartyDic = "";
    [ObservableProperty] private string evidenceNumber = "";
    [ObservableProperty] private string taxableSupplyDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    [ObservableProperty] private string taxBaseCzk = "0";
    [ObservableProperty] private string vatCzk = "0";
    [ObservableProperty] private string currency = "CZK";
    [ObservableProperty] private string foreignAmount = "";
    [ObservableProperty] private string exchangeRate = "";
    [ObservableProperty] private string note = "";

    public static InvoiceLineViewModel FromDomain(InvoiceLine invoice) => new()
    {
        Id = invoice.Id,
        PeriodId = invoice.PeriodId,
        Kind = invoice.Kind.ToString(),
        CounterpartyName = invoice.CounterpartyName,
        CounterpartyDic = invoice.CounterpartyDic ?? "",
        EvidenceNumber = invoice.EvidenceNumber,
        TaxableSupplyDate = invoice.TaxableSupplyDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TaxBaseCzk = Format(invoice.TaxBaseCzk),
        VatCzk = Format(invoice.VatCzk),
        Currency = invoice.Currency,
        ForeignAmount = invoice.ForeignAmount is null ? "" : Format(invoice.ForeignAmount.Value),
        ExchangeRate = invoice.ExchangeRate is null ? "" : Format(invoice.ExchangeRate.Value),
        Note = invoice.Note ?? ""
    };

    public InvoiceLine ToDomain() => new()
    {
        Id = Id,
        PeriodId = PeriodId,
        Kind = Enum.TryParse<InvoiceKind>(Kind, out var parsedKind) ? parsedKind : InvoiceKind.ReceivedDomesticWithVat,
        CounterpartyName = CounterpartyName,
        CounterpartyDic = CounterpartyDic.NullIfWhiteSpace(),
        EvidenceNumber = EvidenceNumber,
        TaxableSupplyDate = ParseDate(TaxableSupplyDate),
        TaxBaseCzk = ParseDecimal(TaxBaseCzk),
        VatCzk = ParseDecimal(VatCzk),
        Currency = Currency.NullIfWhiteSpace()?.ToUpperInvariant() ?? "CZK",
        ForeignAmount = ForeignAmount.NullIfWhiteSpace() is null ? null : ParseDecimal(ForeignAmount),
        ExchangeRate = ExchangeRate.NullIfWhiteSpace() is null ? null : ParseDecimal(ExchangeRate),
        Note = Note.NullIfWhiteSpace()
    };

    private static DateOnly ParseDate(string value)
        => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.Today);

    private static decimal ParseDecimal(string value)
        => decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
