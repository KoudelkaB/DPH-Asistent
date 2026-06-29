using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dph.Core.Calculations;
using Dph.Core.Domain;

namespace Dph.App.ViewModels;

public partial class IssuedInvoiceItemViewModel : ViewModelBase
{
    public string[] VatRateOptions { get; } = ["21", "12", "0"];

    [ObservableProperty] private long id;
    [ObservableProperty] private string description = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineBaseText))]
    [NotifyPropertyChangedFor(nameof(LineVatText))]
    [NotifyPropertyChangedFor(nameof(LineGrossText))]
    private string quantity = "1";

    [ObservableProperty] private string unit = "ks";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineBaseText))]
    [NotifyPropertyChangedFor(nameof(LineVatText))]
    [NotifyPropertyChangedFor(nameof(LineGrossText))]
    private string unitPriceCzk = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineVatText))]
    [NotifyPropertyChangedFor(nameof(LineGrossText))]
    private string vatRate = "21";

    public decimal LineBase => VatCalculator.Money(ParseDecimal(Quantity) * ParseDecimal(UnitPriceCzk));
    public decimal LineVat => VatCalculator.Money(LineBase * (ParseDecimal(VatRate) / 100m));

    public string LineBaseText => Format(LineBase);
    public string LineVatText => Format(LineVat);
    public string LineGrossText => Format(LineBase + LineVat);

    public static IssuedInvoiceItemViewModel FromDomain(IssuedInvoiceItem item) => new()
    {
        Id = item.Id,
        Description = item.Description,
        Quantity = Format(item.Quantity),
        Unit = item.Unit,
        UnitPriceCzk = Format(item.UnitPriceCzk),
        VatRate = RateText(item.VatRate)
    };

    public IssuedInvoiceItem ToDomain() => new()
    {
        Id = Id,
        Description = Description,
        Quantity = ParseDecimal(Quantity),
        Unit = Unit.NullIfWhiteSpace() ?? "ks",
        UnitPriceCzk = ParseDecimal(UnitPriceCzk),
        VatRate = ParseVatRate(VatRate)
    };

    private static decimal ParseDecimal(string value)
        => decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

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
}
