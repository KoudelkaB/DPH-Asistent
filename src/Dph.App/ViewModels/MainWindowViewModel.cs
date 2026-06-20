using System.Collections.ObjectModel;
using System.Globalization;
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
    private const string ExportDirectorySettingKey = "export_directory";
    private const string WindowWidthSettingKey = "window_width";
    private const string WindowHeightSettingKey = "window_height";

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
    [ObservableProperty] private CounterpartyViewModel? selectedInvoiceCounterparty;
    [ObservableProperty] private string importDirectory = "";
    [ObservableProperty] private string exportDirectory = ApplicationPaths.ExportDirectory;
    [ObservableProperty] private string statusMessage = "Připraveno.";
    [ObservableProperty] private string summaryText = "";

    public ObservableCollection<VatPeriod> Periods { get; } = [];
    public ObservableCollection<CounterpartyViewModel> Counterparties { get; } = [];
    public ObservableCollection<InvoiceLineViewModel> Invoices { get; } = [];
    public string DatabasePath => ApplicationPaths.DatabasePath;
    public Func<string?, Task<string?>> PickExportDirectoryAsync { get; set; } =
        _ => Task.FromResult<string?>(null);

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

    public async Task<(double Width, double Height)?> LoadWindowSizeAsync()
    {
        await _repository.InitializeAsync();
        var widthText = await _repository.LoadSettingAsync(WindowWidthSettingKey);
        var heightText = await _repository.LoadSettingAsync(WindowHeightSettingKey);
        if (!double.TryParse(widthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
            || !double.TryParse(heightText, NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
            || width <= 0
            || height <= 0)
        {
            return null;
        }

        return (width, height);
    }

    public async Task SaveWindowSizeAsync(double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        await _repository.InitializeAsync();
        await _repository.SaveSettingAsync(WindowWidthSettingKey, width.ToString(CultureInfo.InvariantCulture));
        await _repository.SaveSettingAsync(WindowHeightSettingKey, height.ToString(CultureInfo.InvariantCulture));
    }

    partial void OnSelectedPeriodChanged(VatPeriod? value)
    {
        _ = LoadInvoicesAsync();
    }

    partial void OnSelectedInvoiceChanged(InvoiceLineViewModel? value)
    {
        SelectedInvoiceCounterparty = value?.CounterpartyId is null
            ? null
            : Counterparties.FirstOrDefault(x => x.Id == value.CounterpartyId.Value);
    }

    partial void OnSelectedInvoiceCounterpartyChanged(CounterpartyViewModel? value)
    {
        if (SelectedInvoice is null || value is null)
        {
            return;
        }

        SelectedInvoice.CounterpartyId = value.Id == 0 ? null : value.Id;
        SelectedInvoice.CounterpartyName = value.DisplayName;
        SelectedInvoice.CounterpartyDic = value.Dic;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await _repository.InitializeAsync();
        ExportDirectory = await _repository.LoadSettingAsync(ExportDirectorySettingKey) ?? ApplicationPaths.ExportDirectory;
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

        StatusMessage = "Načteno.";
    }

    [RelayCommand]
    private async Task AddPeriodAsync()
    {
        var sourcePeriod = SelectedPeriod ?? Periods.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).FirstOrDefault();
        var newMonth = sourcePeriod is null
            ? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1)
            : new DateTime(sourcePeriod.Year, sourcePeriod.Month, 1).AddMonths(1);
        var period = new VatPeriod
        {
            Year = newMonth.Year,
            Month = newMonth.Month,
            SubmissionDate = DateOnly.FromDateTime(DateTime.Today),
            FormType = sourcePeriod?.FormType ?? "B"
        };

        await _repository.SavePeriodAsync(period);
        if (Periods.All(x => x.Id != period.Id))
        {
            Periods.Insert(0, period);
        }

        SelectedPeriod = period;
        var copied = sourcePeriod is null ? 0 : await CopyTemplateInvoicesAsync(sourcePeriod.Id, period);
        await LoadInvoicesAsync();
        StatusMessage = copied == 0
            ? $"Vytvořeno období {period.Label}."
            : $"Vytvořeno období {period.Label} z {sourcePeriod!.Label}; zkopírováno {copied} řádků jako šablona.";
    }

    [RelayCommand]
    private void AddCounterparty()
    {
        var counterparty = new CounterpartyViewModel
        {
            Name = "Nový subjekt",
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
        RefreshInvoiceCounterpartyNames(SelectedCounterparty);
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

        var (subject, fromCache) = await LookupAresByIcoAsync(normalizedIco);
        if (subject is null)
        {
            if (!StatusMessage.StartsWith("ARES chyba:", StringComparison.Ordinal)
                && StatusMessage != "ARES neodpověděl včas."
                && StatusMessage != "ARES vrátil neočekávaná data.")
            {
                StatusMessage = "ARES subjekt nenašel.";
            }

            return;
        }

        SelectedCounterparty.Ico = subject.Ico;
        SelectedCounterparty.Name = subject.OfficialName;
        SelectedCounterparty.Dic = subject.Dic ?? SelectedCounterparty.Dic;

        await SaveCounterpartyAsync();
        StatusMessage = fromCache ? $"ARES cache: {subject.OfficialName}" : $"ARES: {subject.OfficialName}";
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
        => await SaveInvoicesCoreAsync();

    private async Task<bool> SaveInvoicesCoreAsync()
    {
        if (SelectedPeriod is null)
        {
            return false;
        }

        var domains = Invoices.Select(PrepareInvoiceForSave).ToArray();
        var validationMessage = await ValidateInvoiceReferencesAsync(domains);
        if (validationMessage is not null)
        {
            StatusMessage = validationMessage;
            return false;
        }

        await _repository.SaveTaxSubjectAsync(TaxSubject);
        for (var index = 0; index < Invoices.Count; index++)
        {
            var invoice = Invoices[index];
            var domain = domains[index];
            await _repository.SaveInvoiceAsync(domain);
            invoice.Id = domain.Id;
            invoice.CounterpartyId = domain.CounterpartyId;
            invoice.CounterpartyName = domain.CounterpartyName;
            invoice.CounterpartyDic = domain.CounterpartyDic ?? "";
        }

        UpdateSummary();
        StatusMessage = "Uloženo.";
        return true;
    }

    private InvoiceLine PrepareInvoiceForSave(InvoiceLineViewModel invoice)
    {
        invoice.PeriodId = SelectedPeriod?.Id ?? invoice.PeriodId;
        var domain = invoice.ToDomain();
        if (domain.CounterpartyId is not null)
        {
            var counterparty = Counterparties.FirstOrDefault(x => x.Id == domain.CounterpartyId.Value);
            if (counterparty is not null)
            {
                domain.CounterpartyName = counterparty.DisplayName;
                domain.CounterpartyDic = counterparty.Dic.NullIfWhiteSpace();
            }
        }
        else
        {
            ApplyCounterpartyReference(domain);
        }

        return domain;
    }

    private async Task<string?> ValidateInvoiceReferencesAsync(IReadOnlyList<InvoiceLine> invoices)
    {
        var seen = new Dictionary<string, InvoiceLine>(StringComparer.OrdinalIgnoreCase);
        foreach (var invoice in invoices.Where(ShouldValidateInvoiceReference))
        {
            var key = InvoiceReferenceKey(invoice);
            if (key is null)
            {
                continue;
            }

            if (seen.TryGetValue(key, out var duplicate))
            {
                return $"Duplicitní doklad v aktuálním období: {invoice.EvidenceNumber} / {InvoiceReferenceSubject(invoice)}. Stejný řádek už je v tabulce jako {duplicate.EvidenceNumber}.";
            }

            seen[key] = invoice;
        }

        foreach (var invoice in invoices.Where(ShouldValidateInvoiceReference))
        {
            var duplicate = await _repository.FindDuplicateInvoiceReferenceAsync(invoice);
            if (duplicate is not null)
            {
                return $"Doklad {invoice.EvidenceNumber} pro subjekt {InvoiceReferenceSubject(invoice)} už je použitý v období {duplicate.PeriodLabel}.";
            }
        }

        return null;
    }

    private static bool ShouldValidateInvoiceReference(InvoiceLine invoice)
        => !string.IsNullOrWhiteSpace(invoice.EvidenceNumber)
           && !IsControlStatementSummary(invoice)
           && InvoiceReferenceKey(invoice) is not null;

    private static string? InvoiceReferenceKey(InvoiceLine invoice)
    {
        var evidenceNumber = invoice.EvidenceNumber.NullIfWhiteSpace()?.ToUpperInvariant();
        if (evidenceNumber is null)
        {
            return null;
        }

        var subject = invoice.CounterpartyId is not null
            ? $"id:{invoice.CounterpartyId.Value}"
            : invoice.CounterpartyDic.NullIfWhiteSpace() is { } dic
                ? $"dic:{dic.ToUpperInvariant()}"
                : invoice.CounterpartyName.NullIfWhiteSpace() is { } name
                    ? $"name:{name.ToUpperInvariant()}"
                    : null;

        return subject is null ? null : $"{InvoiceReferenceScope(invoice.Kind)}|{evidenceNumber}|{subject}";
    }

    private static string InvoiceReferenceScope(InvoiceKind kind)
        => kind == InvoiceKind.IssuedDomestic ? "Issued" : "Received";

    private static string InvoiceReferenceSubject(InvoiceLine invoice)
        => invoice.CounterpartyName.NullIfWhiteSpace()
           ?? invoice.CounterpartyDic.NullIfWhiteSpace()
           ?? invoice.CounterpartyId?.ToString(CultureInfo.InvariantCulture)
           ?? "(bez subjektu)";

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
        SelectedInvoice.VatCzk = VatCalculator.Money(baseCzk * VatCalculator.Rate(invoice.VatRate)).ToString("0.##");
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

        var selectedDirectory = await PickExportDirectoryAsync(ExportDirectory);
        if (string.IsNullOrWhiteSpace(selectedDirectory))
        {
            StatusMessage = "Export zrušen.";
            return;
        }

        ExportDirectory = selectedDirectory;
        await _repository.SaveSettingAsync(ExportDirectorySettingKey, ExportDirectory);
        if (!await SaveInvoicesCoreAsync())
        {
            return;
        }

        Directory.CreateDirectory(ExportDirectory);
        var invoices = Invoices.Select(x => x.ToDomain()).ToArray();
        var prefix = $"{SelectedPeriod.Year:D4}-{SelectedPeriod.Month:D2}";
        var vatReturnPath = Path.Combine(ExportDirectory, $"{prefix}_DPHDP_podani.xml");
        var controlStatementPath = Path.Combine(ExportDirectory, $"{prefix}_DPHKH_podani.xml");

        _exporter.ExportVatReturn(TaxSubject, SelectedPeriod, invoices).Save(vatReturnPath);
        _exporter.ExportControlStatement(TaxSubject, SelectedPeriod, invoices).Save(controlStatementPath);
        StatusMessage = $"Exportováno: {Path.GetFileName(vatReturnPath)} a {Path.GetFileName(controlStatementPath)} do {ExportDirectory}";
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

            await EnrichCounterpartyFromAresAsync(counterparty);
            await _repository.SaveCounterpartyAsync(counterparty);
            Counterparties.Add(CounterpartyViewModel.FromDomain(counterparty));
            existingKeys.Add(key);
        }

        var importedInvoiceCount = 0;
        foreach (var importedPeriod in imported.Periods)
        {
            await _repository.SavePeriodAsync(importedPeriod.Period);
            if (Periods.All(x => x.Id != importedPeriod.Period.Id))
            {
                Periods.Add(importedPeriod.Period);
            }

            var existingInvoices = await _repository.LoadInvoicesAsync(importedPeriod.Period.Id);
            if (existingInvoices.Count > 0)
            {
                continue;
            }

            foreach (var invoice in importedPeriod.Invoices)
            {
                invoice.PeriodId = importedPeriod.Period.Id;
                ApplyCounterpartyReference(invoice);
                await _repository.SaveInvoiceAsync(invoice);
                importedInvoiceCount++;
            }
        }

        Periods.Clear();
        foreach (var period in await _repository.LoadPeriodsAsync())
        {
            Periods.Add(period);
        }

        SelectedPeriod = Periods.FirstOrDefault();
        StatusMessage = $"Import hotový. Subjektů: {imported.Counterparties.Count}, období: {imported.Periods.Count}, řádků: {importedInvoiceCount}, přeskočeno: {imported.SkippedFiles.Count}.";
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
            var viewModel = InvoiceLineViewModel.FromDomain(invoice);
            ApplyCounterpartyReference(viewModel);
            Invoices.Add(viewModel);
        }

        UpdateSummary();
    }

    private async Task<int> CopyTemplateInvoicesAsync(long sourcePeriodId, VatPeriod targetPeriod)
    {
        var sourceInvoices = await _repository.LoadInvoicesAsync(sourcePeriodId);
        var copied = 0;
        foreach (var source in sourceInvoices)
        {
            source.Id = 0;
            source.PeriodId = targetPeriod.Id;
            if (!IsControlStatementSummary(source))
            {
                source.EvidenceNumber = "";
            }

            source.TaxableSupplyDate = new DateOnly(targetPeriod.Year, targetPeriod.Month, DateTime.DaysInMonth(targetPeriod.Year, targetPeriod.Month));
            await _repository.SaveInvoiceAsync(source);
            copied++;
        }

        return copied;
    }

    private static bool IsControlStatementSummary(InvoiceLine invoice)
        => string.Equals(invoice.EvidenceNumber, "A5", StringComparison.OrdinalIgnoreCase)
           || string.Equals(invoice.EvidenceNumber, "B3", StringComparison.OrdinalIgnoreCase);

    private void ApplyCounterpartyReference(InvoiceLine invoice)
    {
        var counterparty = FindCounterparty(invoice.CounterpartyId, invoice.CounterpartyDic);

        if (counterparty is null)
        {
            return;
        }

        invoice.CounterpartyId = counterparty.Id;
        invoice.CounterpartyName = counterparty.DisplayName;
        invoice.CounterpartyDic = counterparty.Dic.NullIfWhiteSpace();
    }

    private void ApplyCounterpartyReference(InvoiceLineViewModel invoice)
    {
        var counterparty = FindCounterparty(invoice.CounterpartyId, invoice.CounterpartyDic);

        if (counterparty is null)
        {
            return;
        }

        invoice.CounterpartyId = counterparty.Id;
        invoice.CounterpartyName = counterparty.DisplayName;
        invoice.CounterpartyDic = counterparty.Dic;
    }

    private CounterpartyViewModel? FindCounterparty(long? id, string? dic)
    {
        if (id is not null)
        {
            var byId = Counterparties.FirstOrDefault(x => x.Id == id.Value);
            if (byId is not null)
            {
                return byId;
            }
        }

        return Counterparties.FirstOrDefault(x =>
            !string.IsNullOrWhiteSpace(dic)
            && string.Equals(x.Dic, dic, StringComparison.OrdinalIgnoreCase));
    }

    private async Task EnrichCounterpartyFromAresAsync(Counterparty counterparty)
    {
        if (!string.Equals(counterparty.CountryCode, "CZ", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var normalizedIco = AresClient.NormalizeIco(counterparty.Ico ?? "");
        if (normalizedIco.Length != 8)
        {
            normalizedIco = AresClient.TryGetIcoFromDic(counterparty.Dic) ?? "";
        }

        if (normalizedIco.Length != 8)
        {
            return;
        }

        var (subject, _) = await LookupAresByIcoAsync(normalizedIco);
        if (subject is null)
        {
            return;
        }

        counterparty.Ico = subject.Ico;
        counterparty.Dic = subject.Dic ?? counterparty.Dic;
        counterparty.Name = subject.OfficialName;
        counterparty.AresUpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<(AresSubject? Subject, bool FromCache)> LookupAresByIcoAsync(string ico)
    {
        var cached = await _repository.LoadAresCacheAsync(ico);
        if (cached is not null)
        {
            return (cached, true);
        }

        try
        {
            var subject = await _aresClient.LookupByIcoAsync(ico);
            if (subject is not null)
            {
                await _repository.SaveAresCacheAsync(subject);
            }

            return (subject, false);
        }
        catch (HttpRequestException exception)
        {
            StatusMessage = $"ARES chyba: {exception.Message}";
            return (null, false);
        }
        catch (TaskCanceledException)
        {
            StatusMessage = "ARES neodpověděl včas.";
            return (null, false);
        }
        catch (System.Text.Json.JsonException)
        {
            StatusMessage = "ARES vrátil neočekávaná data.";
            return (null, false);
        }
    }

    private void RefreshInvoiceCounterpartyNames(CounterpartyViewModel counterparty)
    {
        foreach (var invoice in Invoices.Where(x =>
                     x.CounterpartyId == counterparty.Id
                     || (!string.IsNullOrWhiteSpace(x.CounterpartyDic)
                         && string.Equals(x.CounterpartyDic, counterparty.Dic, StringComparison.OrdinalIgnoreCase))))
        {
            invoice.CounterpartyId = counterparty.Id == 0 ? null : counterparty.Id;
            invoice.CounterpartyName = counterparty.DisplayName;
            invoice.CounterpartyDic = counterparty.Dic;
        }
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
