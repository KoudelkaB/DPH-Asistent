using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dph.Core.Calculations;
using Dph.Core.Domain;

namespace Dph.App.ViewModels;

public partial class IssuedInvoiceViewModel : ViewModelBase
{
    [ObservableProperty] private long id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string number = "";
    [ObservableProperty] private string issueDate = Today();
    [ObservableProperty] private string taxableSupplyDate = Today();
    [ObservableProperty] private string dueDate = DateOnly.FromDateTime(DateTime.Today).AddDays(14).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private long? customerId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string customerName = "";

    [ObservableProperty] private string customerIco = "";
    [ObservableProperty] private string customerDic = "";
    [ObservableProperty] private string customerStreet = "";
    [ObservableProperty] private string customerHouseNumber = "";
    [ObservableProperty] private string customerCity = "";
    [ObservableProperty] private string customerPostalCode = "";
    [ObservableProperty] private string customerCountry = "Česká republika";

    [ObservableProperty] private string currency = "CZK";
    [ObservableProperty] private string variableSymbol = "";
    [ObservableProperty] private string paymentMethod = "Převodem";
    [ObservableProperty] private string introText = "";
    [ObservableProperty] private string note = "";
    [ObservableProperty] private string footer = "";

    public ObservableCollection<IssuedInvoiceItemViewModel> Items { get; } = [];

    public IssuedInvoiceViewModel()
    {
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    public string DisplayTitle
        => $"{(string.IsNullOrWhiteSpace(Number) ? "(bez čísla)" : Number)} – {(string.IsNullOrWhiteSpace(CustomerName) ? "(bez odběratele)" : CustomerName)}";

    public string TotalsText
    {
        get
        {
            var baseSum = VatCalculator.Money(Items.Sum(x => x.LineBase));
            var vatSum = VatCalculator.Money(Items.Sum(x => x.LineVat));
            var gross = VatCalculator.Money(baseSum + vatSum);
            var currency = string.Equals(Currency, "CZK", StringComparison.OrdinalIgnoreCase) ? "Kč" : Currency;
            return $"Základ {Format(baseSum)} | DPH {Format(vatSum)} | Celkem {Format(gross)} {currency}";
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (IssuedInvoiceItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnItemChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (IssuedInvoiceItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnItemChanged;
            }
        }

        OnPropertyChanged(nameof(TotalsText));
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(TotalsText));

    partial void OnCurrencyChanged(string value) => OnPropertyChanged(nameof(TotalsText));

    public static IssuedInvoiceViewModel FromDomain(IssuedInvoice invoice)
    {
        var viewModel = new IssuedInvoiceViewModel
        {
            Id = invoice.Id,
            Number = invoice.Number,
            IssueDate = invoice.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TaxableSupplyDate = invoice.TaxableSupplyDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DueDate = invoice.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            CustomerId = invoice.CustomerId,
            CustomerName = invoice.CustomerName,
            CustomerIco = invoice.CustomerIco ?? "",
            CustomerDic = invoice.CustomerDic ?? "",
            CustomerStreet = invoice.CustomerStreet ?? "",
            CustomerHouseNumber = invoice.CustomerHouseNumber ?? "",
            CustomerCity = invoice.CustomerCity ?? "",
            CustomerPostalCode = invoice.CustomerPostalCode ?? "",
            CustomerCountry = invoice.CustomerCountry,
            Currency = invoice.Currency,
            VariableSymbol = invoice.VariableSymbol ?? "",
            PaymentMethod = invoice.PaymentMethod ?? "",
            IntroText = invoice.IntroText ?? "",
            Note = invoice.Note ?? "",
            Footer = invoice.Footer ?? ""
        };

        foreach (var item in invoice.Items)
        {
            viewModel.Items.Add(IssuedInvoiceItemViewModel.FromDomain(item));
        }

        return viewModel;
    }

    public IssuedInvoice ToDomain() => new()
    {
        Id = Id,
        Number = Number.Trim(),
        IssueDate = ParseDate(IssueDate),
        TaxableSupplyDate = ParseDate(TaxableSupplyDate),
        DueDate = ParseDate(DueDate),
        CustomerId = CustomerId,
        CustomerName = CustomerName.Trim(),
        CustomerIco = CustomerIco.NullIfWhiteSpace(),
        CustomerDic = CustomerDic.NullIfWhiteSpace(),
        CustomerStreet = CustomerStreet.NullIfWhiteSpace(),
        CustomerHouseNumber = CustomerHouseNumber.NullIfWhiteSpace(),
        CustomerCity = CustomerCity.NullIfWhiteSpace(),
        CustomerPostalCode = CustomerPostalCode.NullIfWhiteSpace(),
        CustomerCountry = CustomerCountry.NullIfWhiteSpace() ?? "Česká republika",
        Currency = Currency.NullIfWhiteSpace()?.ToUpperInvariant() ?? "CZK",
        VariableSymbol = VariableSymbol.NullIfWhiteSpace(),
        PaymentMethod = PaymentMethod.NullIfWhiteSpace(),
        IntroText = IntroText.NullIfWhiteSpace(),
        Note = Note.NullIfWhiteSpace(),
        Footer = Footer.NullIfWhiteSpace(),
        Items = Items.Select(x => x.ToDomain()).ToList()
    };

    private static string Today() => DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string value)
        => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.Today);

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
