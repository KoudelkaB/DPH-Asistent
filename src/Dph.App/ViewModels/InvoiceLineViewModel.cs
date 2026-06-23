using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dph.Core.Calculations;
using Dph.Core.Domain;
using Dph.Core.Epo;

namespace Dph.App.ViewModels;

public partial class InvoiceLineViewModel : ViewModelBase
{
    private bool _isRecalculating;

    // "Reverse" = přijetí služby od osoby neusazené v tuzemsku (§108, zahraniční SaaS apod.),
    // ř.12/13 + odpočet ř.43/44, mimo kontrolní hlášení. Viz InvoiceKind.ReverseCharge.
    public const string ReverseChargeLabel = "Reverse (zahr. služba)";

    public string[] KindOptions { get; } =
    [
        "Vydaná",
        "Přijatá",
        ReverseChargeLabel
    ];

    public string[] VatRateOptions { get; } = ["21", "12", "0"];

    // Poměrný/krácený odpočet (atribut pomer v KH) dává smysl jen u přijaté tuzemské faktury,
    // a do XML se promítne až nad detailním limitem KH. U vydaných a reverse je strukturálně
    // bezpředmětný, pod limitem nemá vliv na XML – viz EpoXmlExporter.VetaB2.
    private static readonly decimal PartialDeductionLimitCzk = EpoTaxFormDefinition.Current.ControlStatementDetailLimitCzk;

    [ObservableProperty] private long id;
    [ObservableProperty] private long periodId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPartialDeduction))]
    [NotifyPropertyChangedFor(nameof(IsPartialDeductionEnabled))]
    [NotifyPropertyChangedFor(nameof(PartialDeductionTooltip))]
    private string kind = "Přijatá";

    [ObservableProperty] private long? counterpartyId;
    [ObservableProperty] private string counterpartyName = "";
    [ObservableProperty] private string counterpartyDic = "";
    [ObservableProperty] private string evidenceNumber = "";
    [ObservableProperty] private string taxableSupplyDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    [ObservableProperty] private string taxBaseCzk = "0";
    [ObservableProperty] private string vatCzk = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPartialDeductionEnabled))]
    [NotifyPropertyChangedFor(nameof(PartialDeductionTooltip))]
    private string grossCzk = "0";

    [ObservableProperty] private string vatRate = "21";
    [ObservableProperty] private bool partialDeduction;

    // Checkbox "Část." zobrazujeme jen u přijaté tuzemské faktury – jinde je bezpředmětný.
    public bool ShowPartialDeduction => ParseKind(Kind) == InvoiceKind.ReceivedDomesticWithVat;

    // Povolený jen nad limitem KH; pod limitem necháváme uloženou hodnotu, ale needitovatelnou.
    public bool IsPartialDeductionEnabled
        => ShowPartialDeduction && ParseDecimal(GrossCzk) > PartialDeductionLimitCzk;

    public string PartialDeductionTooltip => IsPartialDeductionEnabled
        ? "Krácený / poměrný odpočet – v kontrolním hlášení nastaví pomer=A."
        : $"Pod limitem KH ({PartialDeductionLimitCzk:0} Kč vč. DPH) se poměrný odpočet do XML nepromítá.";

    // U vydané a reverse faktury je poměrný odpočet bezpředmětný – vyčistíme uložený příznak,
    // aby nezůstal viset z dřívějška. Pod limitem hodnotu naopak zachováváme (jen zneaktivníme).
    partial void OnKindChanged(string value)
    {
        if (ParseKind(value) != InvoiceKind.ReceivedDomesticWithVat && PartialDeduction)
        {
            PartialDeduction = false;
        }
    }
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

    private static InvoiceKind ParseKind(string value) => value switch
    {
        "Vydaná" => InvoiceKind.IssuedDomestic,
        "Přijatá" => InvoiceKind.ReceivedDomesticWithVat,
        ReverseChargeLabel or "Reverse" => InvoiceKind.ReverseCharge,
        _ => Enum.TryParse<InvoiceKind>(value, out var parsed) ? parsed : InvoiceKind.ReceivedDomesticWithVat
    };

    private static string KindText(InvoiceKind kind) => kind switch
    {
        InvoiceKind.IssuedDomestic => "Vydaná",
        InvoiceKind.ReverseCharge => ReverseChargeLabel,
        _ => "Přijatá"
    };
}
