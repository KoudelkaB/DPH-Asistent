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

    public bool ItemsLoaded { get; set; } = true;

    // Souhrny pro zobrazení v seznamu, když položky ještě nejsou načtené (líné dotažení).
    // HasCachedTotals = false znamená starý záznam bez uloženého souhrnu (dopočítá se on-demand).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalsText))]
    private decimal cachedTotalBase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalsText))]
    private decimal cachedTotalVat;

    public bool HasCachedTotals { get; private set; }

    public void SetCachedTotals(decimal totalBase, decimal totalVat)
    {
        CachedTotalBase = totalBase;
        CachedTotalVat = totalVat;
        HasCachedTotals = true;
    }

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
    [ObservableProperty] private DateTimeOffset? pdfExportedAt;
    [ObservableProperty] private DateTimeOffset? vatInsertedAt;
    [ObservableProperty] private DateTimeOffset? pdfChangedAt;
    [ObservableProperty] private DateTimeOffset? vatChangedAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLockedByHistory))]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyPropertyChangedFor(nameof(ShowStatusBanner))]
    [NotifyPropertyChangedFor(nameof(ShowUnlockButton))]
    [NotifyPropertyChangedFor(nameof(LockReason))]
    [NotifyPropertyChangedFor(nameof(ShowInsertVatButton))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateVatButton))]
    [NotifyPropertyChangedFor(nameof(ShowVatActionButton))]
    [NotifyPropertyChangedFor(nameof(VatActionText))]
    private IssuedInvoiceVatPeriodState vatPeriodState = IssuedInvoiceVatPeriodState.Missing;

    public ObservableCollection<IssuedInvoiceItemViewModel> Items { get; } = [];

    public IssuedInvoiceViewModel()
    {
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    public string DisplayTitle
        => $"{(string.IsNullOrWhiteSpace(Number) ? "(bez čísla)" : Number)} – {(string.IsNullOrWhiteSpace(CustomerName) ? "(bez odběratele)" : CustomerName)}";

    public bool IsLockedByHistory
        => PdfExportedAt is not null
           || (VatInsertedAt is not null && VatPeriodState == IssuedInvoiceVatPeriodState.Closed);
    public bool HasPdfPendingChanges => PdfChangedAt is not null;
    public bool HasVatPendingChanges => VatChangedAt is not null;
    public bool HasPendingChanges => HasPdfPendingChanges || HasVatPendingChanges;

    // PDF nebo uzavřené přiznání DPH jsou historický milník. Vložení do otevřeného přiznání je jen
    // průběžná synchronizace, takže samo o sobě fakturu nezamyká.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyPropertyChangedFor(nameof(ShowStatusBanner))]
    [NotifyPropertyChangedFor(nameof(ShowUnlockButton))]
    [NotifyPropertyChangedFor(nameof(LockReason))]
    private bool isUnlocked;

    public bool IsEditable => !IsLockedByHistory || HasPendingChanges || IsUnlocked;
    public bool ShowStatusBanner => IsLockedByHistory;
    public bool ShowUnlockButton => IsLockedByHistory && !HasPendingChanges && !IsUnlocked;
    public bool ShowInsertVatButton => VatInsertedAt is null && VatPeriodState != IssuedInvoiceVatPeriodState.Missing;
    public bool ShowUpdateVatButton => VatInsertedAt is not null && HasVatPendingChanges && VatPeriodState != IssuedInvoiceVatPeriodState.Missing;
    public bool ShowVatActionButton => ShowInsertVatButton || ShowUpdateVatButton;
    public string VatActionText => VatInsertedAt is null ? "Vložit do přiznání DPH" : "Aktualizovat v přiznání DPH";

    public string LockReason
    {
        get
        {
            if (IsUnlocked && !HasPendingChanges)
            {
                return "Faktura je odemčená k úpravě. Po uložení změn bude označená jako změněná.";
            }

            if (HasPendingChanges)
            {
                var changedState = new List<string>();
                if (HasPdfPendingChanges)
                {
                    changedState.Add("uložení do PDF");
                }

                if (HasVatPendingChanges)
                {
                    changedState.Add("vložení do přiznání DPH");
                }

                var changedText = changedState.Count == 0
                    ? "Faktura byla změněna."
                    : $"Po {string.Join(" a ", changedState)} byla faktura změněna.";
                return $"{changedText} {VatPeriodText()}";
            }

            var states = new List<string>();
            if (PdfExportedAt is not null)
            {
                states.Add($"uložená do PDF ({PdfExportedAt.Value.LocalDateTime:d.M.yyyy})");
            }

            if (VatInsertedAt is not null)
            {
                states.Add("vložená do přiznání DPH");
            }

            return states.Count == 0
                ? ""
                : $"Faktura je uzamčená – {string.Join(" a ", states)}. {VatPeriodText()}";
        }
    }

    public string TotalsText
    {
        get
        {
            // Otevřená faktura počítá souhrny živě z položek; hlavička v seznamu (bez načtených
            // položek) použije denormalizované hodnoty, takže řádek nikdy nezobrazuje prázdno.
            var baseSum = ItemsLoaded ? VatCalculator.Money(Items.Sum(x => x.LineBase)) : CachedTotalBase;
            var vatSum = ItemsLoaded ? VatCalculator.Money(Items.Sum(x => x.LineVat)) : CachedTotalVat;
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
    partial void OnPdfExportedAtChanged(DateTimeOffset? value) => OnHistoryStateChanged();
    partial void OnVatInsertedAtChanged(DateTimeOffset? value) => OnHistoryStateChanged();
    partial void OnPdfChangedAtChanged(DateTimeOffset? value) => OnPendingStateChanged();
    partial void OnVatChangedAtChanged(DateTimeOffset? value) => OnPendingStateChanged();

    private void OnPendingStateChanged()
    {
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(HasPdfPendingChanges));
        OnPropertyChanged(nameof(HasVatPendingChanges));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(ShowUnlockButton));
        OnPropertyChanged(nameof(ShowInsertVatButton));
        OnPropertyChanged(nameof(ShowUpdateVatButton));
        OnPropertyChanged(nameof(ShowVatActionButton));
        OnPropertyChanged(nameof(VatActionText));
        OnPropertyChanged(nameof(LockReason));
    }

    private void OnHistoryStateChanged()
    {
        OnPropertyChanged(nameof(IsLockedByHistory));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(HasPdfPendingChanges));
        OnPropertyChanged(nameof(HasVatPendingChanges));
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(ShowStatusBanner));
        OnPropertyChanged(nameof(ShowUnlockButton));
        OnPropertyChanged(nameof(ShowInsertVatButton));
        OnPropertyChanged(nameof(ShowUpdateVatButton));
        OnPropertyChanged(nameof(ShowVatActionButton));
        OnPropertyChanged(nameof(VatActionText));
        OnPropertyChanged(nameof(LockReason));
    }

    private string VatPeriodText()
    {
        if (VatInsertedAt is null)
        {
            return VatPeriodState switch
            {
                IssuedInvoiceVatPeriodState.Missing => "Přiznání DPH pro její DUZP ještě neexistuje.",
                IssuedInvoiceVatPeriodState.Open => "Přiznání DPH pro její DUZP existuje a je otevřené; faktura se do něj při uložení automaticky vloží.",
                IssuedInvoiceVatPeriodState.Closed => "Přiznání DPH pro její DUZP už existuje a je uzavřené, ale faktura do něj vložená není; vložení bude vyžadovat potvrzení změny.",
                _ => ""
            };
        }

        return VatPeriodState switch
        {
            IssuedInvoiceVatPeriodState.Missing => "Původní přiznání DPH pro její DUZP v aplikaci neexistuje.",
            IssuedInvoiceVatPeriodState.Open when ShowUpdateVatButton => "Přiznání DPH pro její DUZP je otevřené; změny se do něj při uložení automaticky propíšou.",
            IssuedInvoiceVatPeriodState.Open => "Přiznání DPH pro její DUZP je otevřené.",
            IssuedInvoiceVatPeriodState.Closed when ShowUpdateVatButton => "Přiznání DPH pro její DUZP je uzavřené; aktualizace bude vyžadovat potvrzení změny.",
            IssuedInvoiceVatPeriodState.Closed => "Přiznání DPH pro její DUZP je uzavřené.",
            _ => ""
        };
    }

    public static IssuedInvoiceViewModel FromDomain(IssuedInvoice invoice, bool itemsLoaded = true)
    {
        var viewModel = new IssuedInvoiceViewModel
        {
            ItemsLoaded = itemsLoaded,
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
            Footer = invoice.Footer ?? "",
            PdfExportedAt = invoice.PdfExportedAt,
            VatInsertedAt = invoice.VatInsertedAt,
            PdfChangedAt = invoice.PdfChangedAt,
            VatChangedAt = invoice.VatChangedAt
        };

        // Souhrn preferuje uložené hodnoty; u starých záznamů bez souhrnu (null) zůstane
        // HasCachedTotals = false a doplní se on-demand po dotažení položek.
        if (invoice.StoredTotalBaseCzk is { } storedBase && invoice.StoredTotalVatCzk is { } storedVat)
        {
            viewModel.SetCachedTotals(storedBase, storedVat);
        }
        else
        {
            viewModel.CachedTotalBase = invoice.TotalBaseCzk;
            viewModel.CachedTotalVat = invoice.TotalVatCzk;
        }

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
        PdfExportedAt = PdfExportedAt,
        VatInsertedAt = VatInsertedAt,
        PdfChangedAt = PdfChangedAt,
        VatChangedAt = VatChangedAt,
        Items = Items.Select(x => x.ToDomain()).ToList()
    };

    private static string Today() => DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string value)
        => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.Today);

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

public enum IssuedInvoiceVatPeriodState
{
    Missing,
    Open,
    Closed
}
