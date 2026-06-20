using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dph.Core.Calculations;
using Dph.Core.Domain;
using Dph.Core.Epo;
using Dph.Core.Persistence;
using Dph.Core.Services;

namespace Dph.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DphRepository _repository;
    private readonly IAresClient _aresClient;
    private readonly IExchangeRateProvider _exchangeRateProvider;
    private readonly EpoXmlExporter _exporter = new();
    private readonly EpoXmlImporter _importer = new();
    private readonly VatCalculator _calculator = new();

    [ObservableProperty] private TaxSubject taxSubject = new();
    [ObservableProperty] private VatPeriod? selectedPeriod;
    [ObservableProperty] private CounterpartyViewModel? selectedCounterparty;
    [ObservableProperty] private InvoiceLineViewModel? selectedInvoice;
    [ObservableProperty] private string importDirectory = "";
    [ObservableProperty] private string statusMessage = "Připraveno.";
    [ObservableProperty] private string summaryText = "";

    public ObservableCollection<VatPeriod> Periods { get; } = [];
    public ObservableCollection<CounterpartyViewModel> Counterparties { get; } = [];
    public ObservableCollection<InvoiceLineViewModel> Invoices { get; } = [];

    public string[] InvoiceKindOptions { get; } =
    [
        InvoiceKind.IssuedDomestic.ToString(),
        InvoiceKind.ReceivedDomesticWithVat.ToString(),
        InvoiceKind.ReverseCharge.ToString()
    ];

    public string[] CounterpartyRoleOptions { get; } =
    [
        CounterpartyRole.Customer.ToString(),
        CounterpartyRole.Supplier.ToString(),
        CounterpartyRole.Both.ToString()
    ];

    public MainWindowViewModel()
        : this(
            new DphRepository(ApplicationPaths.DatabasePath),
            new AresClient(new HttpClient()),
            new CnbExchangeRateClient(new HttpClient()))
    {
    }

    public MainWindowViewModel(DphRepository repository, IAresClient aresClient, IExchangeRateProvider exchangeRateProvider)
    {
        _repository = repository;
        _aresClient = aresClient;
        _exchangeRateProvider = exchangeRateProvider;
        _ = LoadAsync();
    }

    partial void OnSelectedPeriodChanged(VatPeriod? value)
    {
        _ = LoadInvoicesAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await _repository.InitializeAsync();
        TaxSubject = await _repository.LoadTaxSubjectAsync() ?? DefaultTaxSubject();

        Counterparties.Clear();
        foreach (var counterparty in await _repository.LoadCounterpartiesAsync())
        {
            Counterparties.Add(CounterpartyViewModel.FromDomain(counterparty));
        }

        Periods.Clear();
        foreach (var period in await _repository.LoadPeriodsAsync())
        {
            Periods.Add(period);
        }

        if (Periods.Count == 0)
        {
            await AddPeriodAsync();
        }
        else
        {
            SelectedPeriod = Periods[0];
        }

        StatusMessage = $"Databáze: {ApplicationPaths.DatabasePath}";
    }

    [RelayCommand]
    private async Task AddPeriodAsync()
    {
        var today = DateTime.Today;
        var previousMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
        var period = new VatPeriod
        {
            Year = previousMonth.Year,
            Month = previousMonth.Month,
            SubmissionDate = DateOnly.FromDateTime(today)
        };

        await _repository.SavePeriodAsync(period);
        if (Periods.All(x => x.Id != period.Id))
        {
            Periods.Insert(0, period);
        }

        SelectedPeriod = period;
        await LoadInvoicesAsync();
    }

    [RelayCommand]
    private void AddCounterparty()
    {
        var counterparty = new CounterpartyViewModel
        {
            CustomName = "Nový subjekt",
            CountryCode = "CZ",
            Role = CounterpartyRole.Supplier.ToString()
        };
        Counterparties.Add(counterparty);
        SelectedCounterparty = counterparty;
    }

    [RelayCommand]
    private async Task SaveCounterpartyAsync()
    {
        if (SelectedCounterparty is null)
        {
            return;
        }

        var domain = SelectedCounterparty.ToDomain();
        await _repository.SaveCounterpartyAsync(domain);
        SelectedCounterparty.Id = domain.Id;
        StatusMessage = $"Uloženo: {SelectedCounterparty.DisplayName}";
    }

    [RelayCommand]
    private async Task FillCounterpartyFromAresAsync()
    {
        if (SelectedCounterparty is null)
        {
            return;
        }

        var normalizedIco = AresClient.NormalizeIco(SelectedCounterparty.Ico);
        if (normalizedIco.Length != 8)
        {
            normalizedIco = AresClient.TryGetIcoFromDic(SelectedCounterparty.Dic) ?? "";
        }

        if (normalizedIco.Length != 8)
        {
            StatusMessage = "Vyplň IČO nebo české DIČ ve tvaru CZ + 8 číslic.";
            return;
        }

        var cached = await _repository.LoadAresCacheAsync(normalizedIco);
        var subject = cached ?? await _aresClient.LookupByIcoAsync(normalizedIco);
        if (subject is null)
        {
            StatusMessage = "ARES subjekt nenašel.";
            return;
        }

        if (cached is null)
        {
            await _repository.SaveAresCacheAsync(subject);
        }

        SelectedCounterparty.Ico = subject.Ico;
        SelectedCounterparty.OfficialName = subject.OfficialName;
        SelectedCounterparty.Dic = subject.Dic ?? SelectedCounterparty.Dic;
        if (string.IsNullOrWhiteSpace(SelectedCounterparty.CustomName) || SelectedCounterparty.CustomName == "Nový subjekt")
        {
            SelectedCounterparty.CustomName = subject.OfficialName;
        }

        await SaveCounterpartyAsync();
        StatusMessage = $"ARES: {subject.OfficialName}";
    }

    [RelayCommand]
    private void AddInvoice()
    {
        if (SelectedPeriod is null)
        {
            return;
        }

        var invoice = new InvoiceLineViewModel
        {
            PeriodId = SelectedPeriod.Id,
            TaxableSupplyDate = new DateOnly(SelectedPeriod.Year, SelectedPeriod.Month, DateTime.DaysInMonth(SelectedPeriod.Year, SelectedPeriod.Month)).ToString("yyyy-MM-dd"),
            Kind = InvoiceKind.ReceivedDomesticWithVat.ToString()
        };
        Invoices.Add(invoice);
        SelectedInvoice = invoice;
        UpdateSummary();
    }

    [RelayCommand]
    private async Task SaveInvoicesAsync()
    {
        if (SelectedPeriod is null)
        {
            return;
        }

        await _repository.SaveTaxSubjectAsync(TaxSubject);
        foreach (var invoice in Invoices)
        {
            invoice.PeriodId = SelectedPeriod.Id;
            var domain = invoice.ToDomain();
            await _repository.SaveInvoiceAsync(domain);
            invoice.Id = domain.Id;
        }

        UpdateSummary();
        StatusMessage = "Uloženo.";
    }

    [RelayCommand]
    private async Task DeleteSelectedInvoiceAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        var id = SelectedInvoice.Id;
        Invoices.Remove(SelectedInvoice);
        if (id != 0)
        {
            await _repository.DeleteInvoiceAsync(id);
        }

        UpdateSummary();
    }

    [RelayCommand]
    private async Task ApplyCnbRateAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        var invoice = SelectedInvoice.ToDomain();
        if (invoice.Currency.Equals("CZK", StringComparison.OrdinalIgnoreCase) || invoice.ForeignAmount is null)
        {
            StatusMessage = "Pro kurz vyplň cizí měnu a částku.";
            return;
        }

        var rate = await _exchangeRateProvider.GetRateAsync(invoice.Currency, invoice.TaxableSupplyDate);
        if (rate is null)
        {
            StatusMessage = "Kurz ČNB nebyl nalezen.";
            return;
        }

        var baseCzk = VatCalculator.Money(invoice.ForeignAmount.Value * rate.RatePerUnit);
        SelectedInvoice.ExchangeRate = rate.RatePerUnit.ToString("0.####");
        SelectedInvoice.TaxBaseCzk = baseCzk.ToString("0.##");
        SelectedInvoice.VatCzk = VatCalculator.Money(baseCzk * VatCalculator.StandardRate).ToString("0.##");
        StatusMessage = $"Kurz {rate.CurrencyCode}: {rate.RatePerUnit:0.####} CZK";
        UpdateSummary();
    }

    [RelayCommand]
    private async Task ExportXmlAsync()
    {
        if (SelectedPeriod is null)
        {
            return;
        }

        await SaveInvoicesAsync();
        Directory.CreateDirectory(ApplicationPaths.ExportDirectory);
        var invoices = Invoices.Select(x => x.ToDomain()).ToArray();
        var prefix = $"{SelectedPeriod.Year:D4}-{SelectedPeriod.Month:D2}";
        var vatReturnPath = Path.Combine(ApplicationPaths.ExportDirectory, $"{prefix}_DPHDP_podani.xml");
        var controlStatementPath = Path.Combine(ApplicationPaths.ExportDirectory, $"{prefix}_DPHKH_podani.xml");

        _exporter.ExportVatReturn(TaxSubject, SelectedPeriod, invoices).Save(vatReturnPath);
        _exporter.ExportControlStatement(TaxSubject, SelectedPeriod, invoices).Save(controlStatementPath);
        StatusMessage = $"Exportováno: {ApplicationPaths.ExportDirectory}";
    }

    [RelayCommand]
    private async Task ImportXmlDirectoryAsync()
    {
        if (!Directory.Exists(ImportDirectory))
        {
            StatusMessage = "Složka neexistuje.";
            return;
        }

        var imported = _importer.ImportDirectory(ImportDirectory);
        if (imported.Subject is not null)
        {
            TaxSubject = imported.Subject;
            await _repository.SaveTaxSubjectAsync(TaxSubject);
        }

        var existingKeys = Counterparties
            .Select(x => x.Dic.NullIfWhiteSpace() ?? x.Ico.NullIfWhiteSpace())
            .Where(x => x is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var counterparty in imported.Counterparties.Values)
        {
            var key = counterparty.Dic ?? counterparty.Ico;
            if (key is null || existingKeys.Contains(key))
            {
                continue;
            }

            await _repository.SaveCounterpartyAsync(counterparty);
            Counterparties.Add(CounterpartyViewModel.FromDomain(counterparty));
        }

        StatusMessage = $"Import hotový. Subjektů: {imported.Counterparties.Count}, přeskočeno: {imported.SkippedFiles.Count}.";
    }

    private async Task LoadInvoicesAsync()
    {
        Invoices.Clear();
        if (SelectedPeriod is null || SelectedPeriod.Id == 0)
        {
            UpdateSummary();
            return;
        }

        foreach (var invoice in await _repository.LoadInvoicesAsync(SelectedPeriod.Id))
        {
            Invoices.Add(InvoiceLineViewModel.FromDomain(invoice));
        }

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var summary = _calculator.Calculate(Invoices.Select(x => x.ToDomain()));
        SummaryText =
            $"Výstup: {summary.DomesticOutputBase:0.##} / {summary.DomesticOutputVat:0.##} Kč | " +
            $"Odpočet: {summary.DomesticInputBase:0.##} / {summary.DomesticInputVat:0.##} Kč | " +
            $"Reverse: {summary.ReverseChargeBase:0.##} / {summary.ReverseChargeVat:0.##} Kč | " +
            $"Daň: {summary.NetTax:0.##} Kč";
    }

    private static TaxSubject DefaultTaxSubject() => new()
    {
        DisplayName = "Bohdan Koudelka",
        FirstName = "Bohdan",
        LastName = "Koudelka",
        Country = "Česká Republika",
        ActivityCode = "620000"
    };
}
