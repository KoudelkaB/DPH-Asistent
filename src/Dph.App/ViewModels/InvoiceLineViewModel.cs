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

    // Uživatel rozlišuje jen Vydaná/Přijatá; režim přijatého plnění (tuzemský odpočet vs.
    // reverse charge ze zahraničí) se odvozuje z DIČ dodavatele – viz InvoiceKindClassifier
    // a sloupec „Režim“ v tabulce.
    public string[] KindOptions { get; } =
    [
        "Vydaná",
        "Přijatá"
    ];

    public string[] VatRateOptions { get; } = ["21", "12", "0"];

    // Poměrný/krácený odpočet (atribut pomer v KH) dává smysl jen u přijaté tuzemské faktury,
    // a do XML se promítne až nad detailním limitem KH. U vydaných a reverse je strukturálně
    // bezpředmětný, pod limitem nemá vliv na XML – viz EpoXmlExporter.VetaB2.
    private static readonly decimal PartialDeductionLimitCzk = EpoTaxFormDefinition.Current.ControlStatementDetailLimitCzk;

    [ObservableProperty] private long id;
    [ObservableProperty] private long periodId;

    [ObservableProperty] private long? issuedInvoiceId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPartialDeduction))]
    [NotifyPropertyChangedFor(nameof(IsPartialDeductionEnabled))]
    [NotifyPropertyChangedFor(nameof(PartialDeductionTooltip))]
    [NotifyPropertyChangedFor(nameof(VatModeText))]
    [NotifyPropertyChangedFor(nameof(VatModeTooltip))]
    private string kind = "Přijatá";

    [ObservableProperty] private long? counterpartyId;
    [ObservableProperty] private string counterpartyName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPartialDeduction))]
    [NotifyPropertyChangedFor(nameof(IsPartialDeductionEnabled))]
    [NotifyPropertyChangedFor(nameof(PartialDeductionTooltip))]
    [NotifyPropertyChangedFor(nameof(VatModeText))]
    [NotifyPropertyChangedFor(nameof(VatModeTooltip))]
    private string counterpartyDic = "";

    [ObservableProperty] private CounterpartyViewModel? counterparty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPartialDeduction))]
    [NotifyPropertyChangedFor(nameof(IsPartialDeductionEnabled))]
    [NotifyPropertyChangedFor(nameof(PartialDeductionTooltip))]
    [NotifyPropertyChangedFor(nameof(VatModeText))]
    [NotifyPropertyChangedFor(nameof(VatModeTooltip))]
    private string evidenceNumber = "";
    [ObservableProperty] private string taxableSupplyDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    [ObservableProperty] private string taxBaseCzk = "0";
    [ObservableProperty] private string vatCzk = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPartialDeductionEnabled))]
    [NotifyPropertyChangedFor(nameof(PartialDeductionTooltip))]
    private string grossCzk = "0";

    [ObservableProperty] private string vatRate = "21";
    [ObservableProperty] private bool partialDeduction;

    // Skutečný režim řádku: Vydaná podle výběru, u Přijaté rozhoduje DIČ dodavatele (a výjimka
    // pro souhrn B3). Zobrazuje se ve sloupci „Režim“, aby automatika byla vidět.
    public InvoiceKind DerivedKind => Kind == "Vydaná"
        ? InvoiceKind.IssuedDomestic
        : InvoiceKindClassifier.ClassifyReceived(CounterpartyDic, EvidenceNumber);

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
            + "Určeno podle českého DIČ dodavatele (prefix CZ nebo jen číslice), příp. souhrnu B3.",
        _ => InvoiceKindClassifier.IsEuSupplier(CounterpartyDic)
            ? "Reverse charge – dodavatel registrovaný v EU (podle prefixu DIČ): ř.5/6 + odpočet ř.43/44, KH oddíl A.2."
            : "Reverse charge – dodavatel ze třetí země / bez EU DIČ: ř.12/13 + odpočet ř.43/44, KH oddíl A.2. "
              + "Tuzemská přijatá faktura se pozná podle DIČ s prefixem CZ – vyplň ho. "
              + "Souhrn drobných tuzemských dokladů bez DIČ zadej s číslem dokladu B3."
    };

    // Checkbox "Část." zobrazujeme jen u přijaté tuzemské faktury – jinde je bezpředmětný.
    public bool ShowPartialDeduction => DerivedKind == InvoiceKind.ReceivedDomesticWithVat;

    // Povolený jen nad limitem KH; pod limitem necháváme uloženou hodnotu, ale needitovatelnou.
    public bool IsPartialDeductionEnabled
        => ShowPartialDeduction && ParseDecimal(GrossCzk) > PartialDeductionLimitCzk;

    public string PartialDeductionTooltip => IsPartialDeductionEnabled
        ? "Krácený / poměrný odpočet – v kontrolním hlášení nastaví pomer=A."
        : $"Pod limitem KH ({PartialDeductionLimitCzk:0} Kč vč. DPH) se poměrný odpočet do XML nepromítá.";

    [ObservableProperty] private string currency = "CZK";
    [ObservableProperty] private string foreignAmount = "";
    [ObservableProperty] private string exchangeRate = "";
    [ObservableProperty] private string note = "";

    public static InvoiceLineViewModel FromDomain(InvoiceLine invoice)
    {
        var viewModel = new InvoiceLineViewModel
        {
            Id = invoice.Id,
            PeriodId = invoice.PeriodId,
            IssuedInvoiceId = invoice.IssuedInvoiceId,
            Kind = KindText(invoice.Kind),
            CounterpartyId = invoice.CounterpartyId,
            CounterpartyName = invoice.CounterpartyName,
            CounterpartyDic = invoice.CounterpartyDic ?? "",
            EvidenceNumber = invoice.EvidenceNumber,
            TaxableSupplyDate = invoice.TaxableSupplyDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PartialDeduction = invoice.PartialDeduction,
            Currency = invoice.Currency,
            ForeignAmount = invoice.ForeignAmount is null ? "" : Format(invoice.ForeignAmount.Value),
            ExchangeRate = invoice.ExchangeRate is null ? "" : Format(invoice.ExchangeRate.Value),
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

    public InvoiceLine ToDomain() => new()
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
        TaxBaseCzk = ParseDecimal(TaxBaseCzk),
        VatCzk = ParseDecimal(VatCzk),
        VatRate = ParseVatRate(VatRate),
        // Skrytý checkbox může držet starou hodnotu – do domény jde jen tam, kde dává smysl.
        PartialDeduction = PartialDeduction && DerivedKind == InvoiceKind.ReceivedDomesticWithVat,
        Currency = Currency.NullIfWhiteSpace()?.ToUpperInvariant() ?? "CZK",
        ForeignAmount = ForeignAmount.NullIfWhiteSpace() is null ? null : ParseDecimal(ForeignAmount),
        ExchangeRate = ExchangeRate.NullIfWhiteSpace() is null ? null : ParseDecimal(ExchangeRate),
        Note = Note.NullIfWhiteSpace()
    };

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

    // Základ, DPH i částka s DPH jdou zadat libovolně; ostatní dvě se dopočítají podle sazby.
    // _isRecalculating brání zacyklení, protože každé přepsání zase spustí tyto handlery.
    partial void OnTaxBaseCzkChanged(string value)
    {
        if (_isRecalculating)
        {
            return;
        }

        _isRecalculating = true;
        var baseCzk = ParseDecimal(value);
        var vat = VatCalculator.Money(baseCzk * ParseRatePercent(VatRate));
        VatCzk = Format(vat);
        GrossCzk = Format(baseCzk + vat);
        _isRecalculating = false;
    }

    partial void OnVatCzkChanged(string value)
    {
        if (_isRecalculating)
        {
            return;
        }

        _isRecalculating = true;
        var vat = ParseDecimal(value);
        var rate = ParseRatePercent(VatRate);
        if (rate != 0)
        {
            var baseCzk = VatCalculator.Money(vat / rate);
            TaxBaseCzk = Format(baseCzk);
            GrossCzk = Format(baseCzk + vat);
        }
        else
        {
            // Při nulové sazbě nelze ze daně dopočítat základ; aktualizujeme aspoň částku s DPH.
            GrossCzk = Format(ParseDecimal(TaxBaseCzk) + vat);
        }

        _isRecalculating = false;
    }

    partial void OnGrossCzkChanged(string value)
    {
        if (_isRecalculating)
        {
            return;
        }

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

    private static string KindText(InvoiceKind kind)
        => kind == InvoiceKind.IssuedDomestic ? "Vydaná" : "Přijatá";
}
