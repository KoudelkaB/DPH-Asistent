using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dph.Core.Calculations;
using Dph.Core.Domain;

namespace Dph.App.ViewModels;

public partial class InvoiceLineViewModel : ViewModelBase
{
    private bool _isRecalculating;

    public string[] KindOptions { get; } =
    [
        "Vydaná",
        "Přijatá",
        "Reverse"
    ];

    public string[] VatRateOptions { get; } = ["21", "12", "0"];

    [ObservableProperty] private long id;
    [ObservableProperty] private long periodId;
    [ObservableProperty] private string kind = "Přijatá";
    [ObservableProperty] private long? counterpartyId;
    [ObservableProperty] private string counterpartyName = "";
    [ObservableProperty] private string counterpartyDic = "";
    [ObservableProperty] private string evidenceNumber = "";
    [ObservableProperty] private string taxableSupplyDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    [ObservableProperty] private string taxBaseCzk = "0";
    [ObservableProperty] private string vatCzk = "0";
    [ObservableProperty] private string vatRate = "21";
    [ObservableProperty] private bool partialDeduction;
    [ObservableProperty] private string currency = "CZK";
    [ObservableProperty] private string foreignAmount = "";
    [ObservableProperty] private string exchangeRate = "";
    [ObservableProperty] private string note = "";

    public static InvoiceLineViewModel FromDomain(InvoiceLine invoice) => new()
    {
        Id = invoice.Id,
        PeriodId = invoice.PeriodId,
        Kind = KindText(invoice.Kind),
        CounterpartyId = invoice.CounterpartyId,
        CounterpartyName = invoice.CounterpartyName,
        CounterpartyDic = invoice.CounterpartyDic ?? "",
        EvidenceNumber = invoice.EvidenceNumber,
        TaxableSupplyDate = invoice.TaxableSupplyDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TaxBaseCzk = Format(invoice.TaxBaseCzk),
        VatCzk = Format(invoice.VatCzk),
        VatRate = RateText(invoice.VatRate),
        PartialDeduction = invoice.PartialDeduction,
        Currency = invoice.Currency,
        ForeignAmount = invoice.ForeignAmount is null ? "" : Format(invoice.ForeignAmount.Value),
        ExchangeRate = invoice.ExchangeRate is null ? "" : Format(invoice.ExchangeRate.Value),
        Note = invoice.Note ?? ""
    };

    public InvoiceLine ToDomain() => new()
    {
        Id = Id,
        PeriodId = PeriodId,
        Kind = ParseKind(Kind),
        CounterpartyId = CounterpartyId,
        CounterpartyName = CounterpartyName,
        CounterpartyDic = CounterpartyDic.NullIfWhiteSpace(),
        EvidenceNumber = EvidenceNumber,
        TaxableSupplyDate = ParseDate(TaxableSupplyDate),
        TaxBaseCzk = ParseDecimal(TaxBaseCzk),
        VatCzk = ParseDecimal(VatCzk),
        VatRate = ParseVatRate(VatRate),
        PartialDeduction = PartialDeduction,
        Currency = Currency.NullIfWhiteSpace()?.ToUpperInvariant() ?? "CZK",
        ForeignAmount = ForeignAmount.NullIfWhiteSpace() is null ? null : ParseDecimal(ForeignAmount),
        ExchangeRate = ExchangeRate.NullIfWhiteSpace() is null ? null : ParseDecimal(ExchangeRate),
        Note = Note.NullIfWhiteSpace()
    };

    partial void OnTaxBaseCzkChanged(string value)
    {
        if (_isRecalculating)
        {
            return;
        }

        _isRecalculating = true;
        VatCzk = Format(VatCalculator.Money(ParseDecimal(value) * ParseRatePercent(VatRate)));
        _isRecalculating = false;
    }

    partial void OnVatCzkChanged(string value)
    {
        if (_isRecalculating)
        {
            return;
        }

        var rate = ParseRatePercent(VatRate);
        if (rate == 0)
        {
            return;
        }

        _isRecalculating = true;
        TaxBaseCzk = Format(VatCalculator.Money(ParseDecimal(value) / rate));
        _isRecalculating = false;
    }

    partial void OnVatRateChanged(string value)
    {
        if (_isRecalculating)
        {
            return;
        }

        _isRecalculating = true;
        VatCzk = Format(VatCalculator.Money(ParseDecimal(TaxBaseCzk) * ParseRatePercent(value)));
        _isRecalculating = false;
    }

    private static DateOnly ParseDate(string value)
        => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.Today);

    private static decimal ParseDecimal(string value)
        => decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static decimal ParseRatePercent(string value)
        => ParseDecimal(value) / 100m;

    private static VatRateKind ParseVatRate(string value)
        => ParseDecimal(value) switch
        {
            12m => VatRateKind.Reduced12,
            0m => VatRateKind.Zero0,
            _ => VatRateKind.Standard21
        };

    private static string RateText(VatRateKind rate) => rate switch
    {
        VatRateKind.Reduced12 => "12",
        VatRateKind.Zero0 => "0",
        _ => "21"
    };

    private static InvoiceKind ParseKind(string value) => value switch
    {
        "Vydaná" => InvoiceKind.IssuedDomestic,
        "Přijatá" => InvoiceKind.ReceivedDomesticWithVat,
        "Reverse" => InvoiceKind.ReverseCharge,
        _ => Enum.TryParse<InvoiceKind>(value, out var parsed) ? parsed : InvoiceKind.ReceivedDomesticWithVat
    };

    private static string KindText(InvoiceKind kind) => kind switch
    {
        InvoiceKind.IssuedDomestic => "Vydaná",
        InvoiceKind.ReverseCharge => "Reverse",
        _ => "Přijatá"
    };
}
