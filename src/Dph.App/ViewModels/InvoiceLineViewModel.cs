using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dph.Core.Calculations;
using Dph.Core.Domain;
using Dph.Core.Epo;

namespace Dph.App.ViewModels;

public partial class InvoiceLineViewModel : ViewModelBase
{
    private bool _isRecalculating;
    private bool _isApplyingCounterparty;

    // Daňový režim určuje uživatel podle plnění, nikoli automaticky podle DIČ.
    public string[] KindOptions { get; } =
    [
        "Vydaná",
        "Přijatá",
        "Zahraniční služba (RC)"
    ];

    // Snapshot načtené sazby: ItemsSource musí zůstat stabilní i během výběru v ComboBoxu.
    public string[] VatRateOptions { get; private init; } = ["21", "12"];

    [ObservableProperty] private long id;
    [ObservableProperty] private long periodId;

    [ObservableProperty] private long? issuedInvoiceId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPartialDeduction))]
    [NotifyPropertyChangedFor(nameof(IsPartialDeductionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsControlStatementDetail))]
    [NotifyPropertyChangedFor(nameof(ShowDocumentAboveControlLimit))]
    [NotifyPropertyChangedFor(nameof(PartialDeductionTooltip))]
    [NotifyPropertyChangedFor(nameof(VatModeText))]
    [NotifyPropertyChangedFor(nameof(VatModeTooltip))]
    private string kind = "Přijatá";

    [ObservableProperty] private long? counterpartyId;
    [ObservableProperty] private string counterpartyName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPartialDeduction))]
    [NotifyPropertyChangedFor(nameof(IsPartialDeductionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsControlStatementDetail))]
    [NotifyPropertyChangedFor(nameof(ShowDocumentAboveControlLimit))]
    [NotifyPropertyChangedFor(nameof(PartialDeductionTooltip))]
    [NotifyPropertyChangedFor(nameof(VatModeText))]
    [NotifyPropertyChangedFor(nameof(VatModeTooltip))]
    private string counterpartyDic = "";

    [ObservableProperty] private CounterpartyViewModel? counterparty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPartialDeduction))]
    [NotifyPropertyChangedFor(nameof(IsPartialDeductionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsControlStatementDetail))]
    [NotifyPropertyChangedFor(nameof(ShowDocumentAboveControlLimit))]
    [NotifyPropertyChangedFor(nameof(PartialDeductionTooltip))]
    [NotifyPropertyChangedFor(nameof(VatModeText))]
    [NotifyPropertyChangedFor(nameof(VatModeTooltip))]
    private string evidenceNumber = "";
    [ObservableProperty] private string taxableSupplyDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    [ObservableProperty] private string taxBaseCzk = "0";
    [ObservableProperty] private string vatCzk = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPartialDeductionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsControlStatementDetail))]
    [NotifyPropertyChangedFor(nameof(ShowDocumentAboveControlLimit))]
    [NotifyPropertyChangedFor(nameof(PartialDeductionTooltip))]
    private string grossCzk = "0";

    [ObservableProperty] private string vatRate = "21";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsControlStatementDetail))]
    [NotifyPropertyChangedFor(nameof(ShowDocumentAboveControlLimit))]
    private bool partialDeduction;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsControlStatementDetail))]
    private bool documentAboveControlLimit;
    private decimal? _documentGrossCzk;
    private bool? _documentIsDetail;
    private decimal _detailLimit = EpoTaxFormDefinition.Current.ControlStatementDetailLimitCzk;

    public void SetControlStatementState(EpoXmlExporter.ReceivedControlStatementState? state, decimal limit)
    {
        if (_documentGrossCzk == state?.DocumentGrossCzk && _documentIsDetail == state?.IsDetail && _detailLimit == limit) return;
        _documentGrossCzk = state?.DocumentGrossCzk;
        _documentIsDetail = state?.IsDetail;
        _detailLimit = limit;
        OnPropertyChanged(nameof(ShowDocumentAboveControlLimit));
        OnPropertyChanged(nameof(IsControlStatementDetail));
    }

    // Hodnota checkboxu odráží výsledné zařazení celého dokladu, i když je skrytý.
    public bool IsControlStatementDetail
    {
        get => ShowPartialDeduction && !string.Equals(EvidenceNumber.Trim(), "B3", StringComparison.OrdinalIgnoreCase)
            && (_documentIsDetail ?? (Math.Abs(ParseDecimal(GrossCzk)) > _detailLimit || DocumentAboveControlLimit));
        set => DocumentAboveControlLimit = value;
    }

    partial void OnPartialDeductionChanged(bool value)
    {
        if (!value) DocumentAboveControlLimit = false;
    }


    public string DocumentAboveControlLimitTooltip =>
        $"Zaškrtněte, pokud celková částka původního dokladu přesahuje {EpoTaxFormDefinition.Current.ControlStatementDetailLimitCzk.ToString("N0", CultureInfo.GetCultureInfo("cs-CZ"))} Kč včetně DPH, ale zde evidujete jen část. Rozhoduje o zařazení do B.2. Částky se nemění.";

    public InvoiceKind DerivedKind => Kind switch
    {
        "Vydaná" => InvoiceKind.IssuedDomestic,
        "Zahraniční služba (RC)" => InvoiceKind.ReverseCharge,
        _ => InvoiceKind.ReceivedDomesticWithVat
    };

    public string VatModeText => DerivedKind switch
    {
        InvoiceKind.IssuedDomestic => "výstup",
        InvoiceKind.ReceivedDomesticWithVat => "tuzemská",
        _ => InvoiceKindClassifier.IsEuSupplier(CounterpartyDic) ? "RC EU" : "RC 3. země"
    };

    public string VatModeTooltip => DerivedKind switch
    {
        InvoiceKind.IssuedDomestic => "Daň na výstupu – ř.1/2 přiznání, KH oddíl A.4/A.5.",
        InvoiceKind.ReceivedDomesticWithVat =>
            "Tuzemské přijaté plnění s odpočtem – ř.40/41 přiznání, KH oddíl B.2/B.3. "
            + "Volte pouze pro tuzemské plnění s českou DPH. Samotné DIČ neurčuje režim plnění.",
        _ => InvoiceKindClassifier.IsEuSupplier(CounterpartyDic)
            ? "Reverse charge – dodavatel registrovaný v EU (podle prefixu DIČ): ř.5/6 + odpočet ř.43/44, KH oddíl A.2."
            : "Reverse charge – dodavatel ze třetí země / bez EU DIČ: ř.12/13 + odpočet ř.43/44, KH oddíl A.2. "
              + "Pouze služby s místem plnění v ČR, u kterých přiznává daň příjemce."
    };

    // Checkbox "Poměr" zobrazujeme jen u přijaté tuzemské faktury – jinde je bezpředmětný.
    public bool ShowPartialDeduction => DerivedKind == InvoiceKind.ReceivedDomesticWithVat;

    public bool IsPartialDeductionEnabled => ShowPartialDeduction;

    public bool ShowDocumentAboveControlLimit => ShowPartialDeduction && PartialDeduction
        && DecimalInput.TryParse(GrossCzk, out var gross)
        && !string.Equals(EvidenceNumber.Trim(), "B3", StringComparison.OrdinalIgnoreCase)
        && Math.Abs(_documentGrossCzk ?? gross) <= _detailLimit;

    public string PartialDeductionTooltip =>
        "Použit poměr podle § 75: základ a DPH zadávejte již v uplatňované poměrné výši. Zaškrtnutí částky nepřepočítává; v B.2 nastaví příznak poměru, v B.3 se příznak neuvádí. Nejde o krácení koeficientem podle § 76.";

    [ObservableProperty] private string currency = "CZK";
    [ObservableProperty] private string foreignAmount = "";
    [ObservableProperty] private string exchangeRate = "";
    [ObservableProperty] private string note = "";

    public static InvoiceLineViewModel FromDomain(InvoiceLine invoice)
    {
        var viewModel = new InvoiceLineViewModel
        {
            Id = invoice.Id,
            VatRateOptions = invoice.VatRate == VatRateKind.Zero0 ? ["21", "12", "0"] : ["21", "12"],
            PeriodId = invoice.PeriodId,
            IssuedInvoiceId = invoice.IssuedInvoiceId,
            Kind = KindText(invoice.Kind),
            CounterpartyId = invoice.CounterpartyId,
            CounterpartyName = invoice.CounterpartyName,
            CounterpartyDic = invoice.CounterpartyDic ?? "",
            EvidenceNumber = invoice.EvidenceNumber,
            TaxableSupplyDate = invoice.TaxableSupplyDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PartialDeduction = invoice.PartialDeduction,
            DocumentAboveControlLimit = invoice.DocumentAboveControlLimit,
            Currency = invoice.Currency,
            ForeignAmount = invoice.ForeignAmount?.ToString(CultureInfo.InvariantCulture) ?? "",
            ExchangeRate = invoice.ExchangeRate?.ToString(CultureInfo.InvariantCulture) ?? "",
            Note = invoice.Note ?? ""
        };

        // Bez přepočtu sazbou – uložená daň nemusí přesně odpovídat základ × sazba (zaokrouhlení,
        // import z KH) a nesmí se přepsat.
        viewModel.InitializeAmounts(
            RateText(invoice.VatRate),
            Format(invoice.TaxBaseCzk),
            Format(invoice.VatCzk),
            Format(invoice.TaxBaseCzk + invoice.VatCzk));
        return viewModel;
    }

    private void InitializeAmounts(string rate, string baseCzk, string vat, string gross)
    {
        _isRecalculating = true;
        VatRate = rate;
        TaxBaseCzk = baseCzk;
        VatCzk = vat;
        GrossCzk = gross;
        _isRecalculating = false;
    }

    public InvoiceLine ToDomain()
    {
        _ = DecimalInput.Parse(GrossCzk);
        return new InvoiceLine
        {
            Id = Id,
            PeriodId = PeriodId,
            IssuedInvoiceId = IssuedInvoiceId,
            Kind = DerivedKind,
            CounterpartyId = CounterpartyId,
            CounterpartyName = CounterpartyName,
            CounterpartyDic = CounterpartyDic.NullIfWhiteSpace(),
            EvidenceNumber = EvidenceNumber,
            TaxableSupplyDate = ParseDate(TaxableSupplyDate),
            TaxBaseCzk = DecimalInput.Parse(TaxBaseCzk),
            VatCzk = DecimalInput.Parse(VatCzk),
            VatRate = ParseVatRate(VatRate),
            // Skrytý checkbox může držet starou hodnotu – do domény jde jen tam, kde dává smysl.
            PartialDeduction = PartialDeduction && DerivedKind == InvoiceKind.ReceivedDomesticWithVat,
            DocumentAboveControlLimit = DocumentAboveControlLimit && DerivedKind == InvoiceKind.ReceivedDomesticWithVat,
            Currency = Currency.NullIfWhiteSpace()?.ToUpperInvariant() ?? "CZK",
            ForeignAmount = ForeignAmount.NullIfWhiteSpace() is null ? null : DecimalInput.Parse(ForeignAmount),
            ExchangeRate = ExchangeRate.NullIfWhiteSpace() is null ? null : DecimalInput.Parse(ExchangeRate),
            Note = Note.NullIfWhiteSpace()
        };
    }

    partial void OnCounterpartyChanged(CounterpartyViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        _isApplyingCounterparty = true;
        try
        {
            CounterpartyId = value.Id == 0 ? null : value.Id;
            CounterpartyName = value.DisplayName;
            CounterpartyDic = value.Dic;
        }
        finally
        {
            _isApplyingCounterparty = false;
        }
    }

    partial void OnCounterpartyNameChanged(string value) => DetachCounterpartyIfEdited(value, Counterparty?.DisplayName);
    partial void OnCounterpartyDicChanged(string value) => DetachCounterpartyIfEdited(value, Counterparty?.Dic);

    private void DetachCounterpartyIfEdited(string value, string? selectedValue)
    {
        if (_isApplyingCounterparty
            || Counterparty is null
            || string.Equals(value, selectedValue ?? "", StringComparison.Ordinal))
        {
            return;
        }

        Counterparty = null;
        CounterpartyId = null;
    }

    // Ze základu nebo celku dopočítáme daň; ruční změna daně zachovává základ.
    // _isRecalculating brání zacyklení, protože každé přepsání zase spustí tyto handlery.
    partial void OnTaxBaseCzkChanged(string value)
    {
        if (_isRecalculating || !DecimalInput.TryParse(value, out _))
        {
            return;
        }

        if (!PartialDeduction) DocumentAboveControlLimit = false;
        _isRecalculating = true;
        var baseCzk = ParseDecimal(value);
        var vat = VatCalculator.Money(baseCzk * ParseRatePercent(VatRate));
        VatCzk = Format(vat);
        GrossCzk = Format(baseCzk + vat);
        _isRecalculating = false;
    }

    partial void OnVatCzkChanged(string value)
    {
        if (_isRecalculating || !DecimalInput.TryParse(value, out _))
        {
            return;
        }

        if (!PartialDeduction) DocumentAboveControlLimit = false;
        _isRecalculating = true;
        var vat = ParseDecimal(value);
        // Daň z přijatého dokladu může zahrnovat zaokrouhlení nebo omezený nárok.
        // Ruční oprava daně nesmí přepsat doložený základ.
        GrossCzk = Format(ParseDecimal(TaxBaseCzk) + vat);

        _isRecalculating = false;
    }

    partial void OnGrossCzkChanged(string value)
    {
        if (_isRecalculating || !DecimalInput.TryParse(value, out _))
        {
            return;
        }

        if (!PartialDeduction) DocumentAboveControlLimit = false;
        _isRecalculating = true;
        var gross = ParseDecimal(value);
        var baseCzk = VatCalculator.Money(gross / (1m + ParseRatePercent(VatRate)));
        TaxBaseCzk = Format(baseCzk);
        VatCzk = Format(VatCalculator.Money(gross - baseCzk));
        _isRecalculating = false;
    }

    partial void OnVatRateChanged(string value)
    {
        if (_isRecalculating)
        {
            return;
        }

        if (!PartialDeduction) DocumentAboveControlLimit = false;
        _isRecalculating = true;
        var baseCzk = ParseDecimal(TaxBaseCzk);
        var vat = VatCalculator.Money(baseCzk * ParseRatePercent(value));
        VatCzk = Format(vat);
        GrossCzk = Format(baseCzk + vat);
        _isRecalculating = false;
    }

    private static DateOnly ParseDate(string value)
        => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new FormatException($"Neplatné datum: „{value}“. Použijte RRRR-MM-DD.");

    private static decimal ParseDecimal(string value)
        => DecimalInput.TryParse(value, out var parsed)
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

    private static string KindText(InvoiceKind kind) => kind switch
    {
        InvoiceKind.IssuedDomestic => "Vydaná",
        InvoiceKind.ReverseCharge => "Zahraniční služba (RC)",
        _ => "Přijatá"
    };
}
