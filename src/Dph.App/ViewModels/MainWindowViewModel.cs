using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dph.Core.Calculations;
using Dph.Core.Domain;
using Dph.Core.Epo;
using Dph.Core.Invoicing;
using Dph.Core.Persistence;
using Dph.Core.Services;

namespace Dph.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string ExportDirectorySettingKey = "export_directory";
    private const string BackupDirectorySettingKey = "backup_directory";
    private const string WindowWidthSettingKey = "window_width";
    private const string WindowHeightSettingKey = "window_height";
    private const string TaxOfficeCatalogSettingKey = "tax_office_catalog";

    private readonly DphRepository _repository;
    private readonly IAresClient _aresClient;
    private readonly IExchangeRateProvider _exchangeRateProvider;
    private readonly ITaxOfficeCatalog _taxOfficeCatalog;
    private static readonly TaxOffice EmptyTaxOffice = new("", "(nevyplněno)");
    private IReadOnlyList<TaxOfficeWorkplace> _allWorkplaces = TaxOfficeDirectory.Workplaces;
    private bool _liveTaxOfficeCatalogLoaded;
    private bool _liveTaxOfficeCatalogLoading;
    private readonly EpoXmlExporter _exporter = new();
    private readonly EpoXmlImporter _importer = new();
    private readonly VatCalculator _calculator = new();
    private readonly HashSet<long> _confirmedProtectedPeriodIds = [];
    private readonly SemaphoreSlim _saveInvoicesLock = new(1, 1);
    private readonly SemaphoreSlim _counterpartySelectionLock = new(1, 1);
    private CancellationTokenSource? _autosaveInvoicesCts;
    private bool _hasPendingInvoiceChanges;
    private bool _isLoadingInvoices;
    private bool _isSavingInvoices;
    private bool _isResolvingInvoiceCounterparty;
    private bool _isLoading;

    [ObservableProperty] private TaxSubject taxSubject = new();
    [ObservableProperty] private TaxOffice? selectedTaxOffice;
    [ObservableProperty] private TaxOfficeWorkplace? selectedWorkplace;
    [ObservableProperty] private VatPeriod? selectedPeriod;
    [ObservableProperty] private CounterpartyViewModel? selectedCounterparty;
    [ObservableProperty] private InvoiceLineViewModel? selectedInvoice;
    [ObservableProperty] private string importDirectory = "";
    [ObservableProperty] private string exportDirectory = ApplicationPaths.ExportDirectory;
    [ObservableProperty] private string backupDirectory = ApplicationPaths.DataDirectory;
    [ObservableProperty] private string statusMessage = "Připraveno.";
    [ObservableProperty] private string summaryText = "";
    [ObservableProperty] private string amountToPayText = "Zaplatit: 0 Kč";
    [ObservableProperty] private string amountToPayCopyValue = "0";

    private static readonly NumberFormatInfo CzkFormat = new() { NumberGroupSeparator = " ", NumberDecimalDigits = 0 };

    public ObservableCollection<VatPeriod> Periods { get; } = [];
    public ObservableCollection<CounterpartyViewModel> Counterparties { get; } = [];
    public ObservableCollection<InvoiceLineViewModel> Invoices { get; } = [];
    public ObservableCollection<TaxOffice> TaxOffices { get; } = new(TaxOfficeDirectory.Offices);
    public ObservableCollection<TaxOfficeWorkplace> AvailableWorkplaces { get; } = [];
    private bool _isSyncingTaxOffice;
    public string DatabasePath => ApplicationPaths.DatabasePath;
    public Func<string?, Task<string?>> PickExportDirectoryAsync { get; set; } =
        _ => Task.FromResult<string?>(null);
    public Func<string, string, Task<string?>> PickDatabaseBackupTargetAsync { get; set; } =
        (_, _) => Task.FromResult<string?>(null);
    public Func<string, Task<string?>> PickDatabaseBackupSourceAsync { get; set; } =
        _ => Task.FromResult<string?>(null);
    public Func<string, string, Task<bool>> ConfirmAsync { get; set; } =
        (_, _) => Task.FromResult(true);
    public Func<string, string, string, Task<ReexportChoice>> ConfirmReexportAsync { get; set; } =
        (_, _, _) => Task.FromResult(ReexportChoice.Regular);
    public Func<string, string, string, Task<string?>> RequestTextAsync { get; set; } =
        (_, _, _) => Task.FromResult<string?>(null);
    public Func<string, Task> CopyToClipboardAsync { get; set; } = _ => Task.CompletedTask;

    public string[] CounterpartyRoleOptions { get; } =
    [
        CounterpartyRole.Customer.ToString(),
        CounterpartyRole.Supplier.ToString(),
        CounterpartyRole.Both.ToString()
    ];

    public MainWindowViewModel()
        : this(
            new DphRepository(ApplicationPaths.DatabasePath),
            new AresClient(CreateHttpClient()),
            new CnbExchangeRateClient(CreateHttpClient()),
            new MfcrTaxOfficeCatalog(CreateHttpClient()))
    {
    }

    private static HttpClient CreateHttpClient() => new() { Timeout = TimeSpan.FromSeconds(15) };

    public MainWindowViewModel(
        DphRepository repository,
        IAresClient aresClient,
        IExchangeRateProvider exchangeRateProvider,
        ITaxOfficeCatalog taxOfficeCatalog)
    {
        _repository = repository;
        _aresClient = aresClient;
        _exchangeRateProvider = exchangeRateProvider;
        _taxOfficeCatalog = taxOfficeCatalog;
        Issuing = new IssuedInvoicesViewModel(
            _repository,
            _aresClient,
            Counterparties,
            () => TaxSubject,
            SaveTaxSubjectAsync,
            ResolveIssuedInvoiceVatPeriodState,
            InsertIssuedInvoiceIntoVatAsync,
            SyncIssuedInvoiceWithOpenVatAsync,
            RemoveIssuedInvoiceFromOpenVatAsync,
            message => StatusMessage = message);
        ApplyTaxOfficeCatalog(TaxOfficeDirectory.Offices, TaxOfficeDirectory.Workplaces);
        _ = LoadAsync();
    }

    public IssuedInvoicesViewModel Issuing { get; }

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

    partial void OnSelectedTaxOfficeChanged(TaxOffice? value)
    {
        if (!_isSyncingTaxOffice)
        {
            TaxSubject.TaxOfficeCode = value?.Code ?? "";
        }

        RebuildAvailableWorkplaces(value?.Code);

        // Vybrané pracoviště přestane patřit k jinému FÚ – zruš ho (pokud zrovna nesynchronizujeme).
        // Prázdná položka (Code = "") platí pro libovolný úřad.
        if (!_isSyncingTaxOffice && SelectedWorkplace is { Code.Length: > 0 } && SelectedWorkplace.OfficeCode != value?.Code)
        {
            SelectedWorkplace = null;
        }
    }

    partial void OnSelectedWorkplaceChanged(TaxOfficeWorkplace? value)
    {
        if (!_isSyncingTaxOffice)
        {
            TaxSubject.WorkplaceCode = value?.Code ?? "";
        }
    }

    private void RebuildAvailableWorkplaces(string? officeCode)
    {
        AvailableWorkplaces.Clear();
        if (string.IsNullOrEmpty(officeCode))
        {
            return;
        }

        // Prázdná volba, aby šlo územní pracoviště zase odebrat.
        AvailableWorkplaces.Add(new TaxOfficeWorkplace("", officeCode, EmptyTaxOffice.Name));
        foreach (var workplace in _allWorkplaces.Where(x => x.OfficeCode == officeCode).OrderBy(x => x.Code, StringComparer.Ordinal))
        {
            AvailableWorkplaces.Add(workplace);
        }
    }

    // Naplní comboboxy číselníkem (živým, nebo zabudovaným jako zálohou) a promítne aktuální výběr.
    private void ApplyTaxOfficeCatalog(IReadOnlyList<TaxOffice> offices, IReadOnlyList<TaxOfficeWorkplace> workplaces)
    {
        _isSyncingTaxOffice = true;
        try
        {
            _allWorkplaces = workplaces;
            TaxOffices.Clear();
            TaxOffices.Add(EmptyTaxOffice);
            foreach (var office in offices)
            {
                TaxOffices.Add(office);
            }

            SelectTaxOfficeFromSubject();
        }
        finally
        {
            _isSyncingTaxOffice = false;
        }
    }

    // Z DB načteme naposledy stažený živý číselník (rychlé, funguje i offline). Když není, zůstane
    // zabudovaný číselník z konstruktoru.
    private async Task LoadCachedTaxOfficeCatalogAsync()
    {
        var json = await _repository.LoadSettingAsync(TaxOfficeCatalogSettingKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var data = JsonSerializer.Deserialize<TaxOfficeCatalogData>(json);
            if (data is { Offices.Count: > 0 })
            {
                ApplyTaxOfficeCatalog(data.Offices, data.Workplaces);
            }
        }
        catch (JsonException)
        {
            // Poškozená cache – ignorujeme, použije se zabudovaný číselník.
        }
    }

    // Živý číselník se stahuje až při prvním použití comboboxu (otevření rozbalovacího seznamu),
    // jednou za běh; výsledek se uloží do DB pro příště.
    public async Task EnsureLiveTaxOfficeCatalogAsync()
    {
        if (_liveTaxOfficeCatalogLoaded || _liveTaxOfficeCatalogLoading)
        {
            return;
        }

        _liveTaxOfficeCatalogLoading = true;
        try
        {
            var data = await _taxOfficeCatalog.LoadAsync();
            if (data is { Offices.Count: > 0 })
            {
                ApplyTaxOfficeCatalog(data.Offices, data.Workplaces);
                await _repository.SaveSettingAsync(TaxOfficeCatalogSettingKey, JsonSerializer.Serialize(data));
                _liveTaxOfficeCatalogLoaded = true;
            }
        }
        catch
        {
            // Necháme zabudovaný/nakešovaný číselník; zkusíme to zase při příštím otevření seznamu.
        }
        finally
        {
            _liveTaxOfficeCatalogLoading = false;
        }
    }

    // Promítne kódy c_ufo/c_pracufo z poplatníka do comboboxů (po načtení DB i po doplnění z ARES).
    private void SyncTaxOfficeSelectionFromSubject()
    {
        _isSyncingTaxOffice = true;
        try
        {
            SelectTaxOfficeFromSubject();
        }
        finally
        {
            _isSyncingTaxOffice = false;
        }
    }

    // Nastaví výběr podle poplatníka. Uložené kódy NIKDY nemažeme – když je číselník (zatím)
    // nezná, vytvoříme pro ně dočasnou položku, ať se hodnota zobrazí a neztratí.
    private void SelectTaxOfficeFromSubject()
    {
        SelectedTaxOffice = ResolveOffice(TaxSubject.TaxOfficeCode);
        RebuildAvailableWorkplaces(SelectedTaxOffice.Code);
        SelectedWorkplace = ResolveWorkplace(TaxSubject.WorkplaceCode, SelectedTaxOffice.Code);
    }

    private TaxOffice ResolveOffice(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return EmptyTaxOffice;
        }

        var existing = TaxOffices.FirstOrDefault(x => x.Code == code);
        if (existing is not null)
        {
            return existing;
        }

        var placeholder = new TaxOffice(code, $"Finanční úřad {code}");
        TaxOffices.Add(placeholder);
        return placeholder;
    }

    private TaxOfficeWorkplace? ResolveWorkplace(string? code, string officeCode)
    {
        if (string.IsNullOrEmpty(code))
        {
            return AvailableWorkplaces.FirstOrDefault(x => x.Code.Length == 0);
        }

        var existing = AvailableWorkplaces.FirstOrDefault(x => x.Code == code);
        if (existing is not null)
        {
            return existing;
        }

        var placeholder = new TaxOfficeWorkplace(code, officeCode, $"Územní pracoviště {code}");
        AvailableWorkplaces.Add(placeholder);
        return placeholder;
    }

    partial void OnSelectedPeriodChanged(VatPeriod? oldValue, VatPeriod? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSelectedPeriodStateChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSelectedPeriodStateChanged;
        }

        RaisePeriodEditabilityChanged();
        _ = LoadInvoicesAsync();
    }

    private IssuedInvoiceVatPeriodState ResolveIssuedInvoiceVatPeriodState(DateOnly taxableSupplyDate)
    {
        var period = Periods.FirstOrDefault(x => x.Year == taxableSupplyDate.Year && x.Month == taxableSupplyDate.Month);
        if (period is null)
        {
            return IssuedInvoiceVatPeriodState.Missing;
        }

        return period.IsLockedByHistory && !period.HasPendingChanges
            ? IssuedInvoiceVatPeriodState.Closed
            : IssuedInvoiceVatPeriodState.Open;
    }

    // Chráněné (podané) období je read-only do první vědomě potvrzené změny. Jakmile má ChangedAt,
    // je změněný stav trvalý a editace zůstává povolená i po restartu.
    public bool IsSelectedPeriodEditable
        => SelectedPeriod is null
           || !SelectedPeriod.IsLockedByHistory
           || SelectedPeriod.HasPendingChanges
           || _confirmedProtectedPeriodIds.Contains(SelectedPeriod.Id);

    public bool ShowPeriodStatusBanner => SelectedPeriod is not null && SelectedPeriod.IsLockedByHistory;

    public bool ShowPeriodUnlockButton
        => SelectedPeriod is { IsLockedByHistory: true, HasPendingChanges: false }
           && !_confirmedProtectedPeriodIds.Contains(SelectedPeriod.Id);

    public string SelectedPeriodStatusText
    {
        get
        {
            if (SelectedPeriod is null)
            {
                return "";
            }

            return SelectedPeriod.Id != 0
                   && _confirmedProtectedPeriodIds.Contains(SelectedPeriod.Id)
                   && !SelectedPeriod.HasPendingChanges
                ? "Období je odemčené k úpravě. Po uložení změn bude označené jako změněné."
                : SelectedPeriod.LockReason;
        }
    }

    private void OnSelectedPeriodStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VatPeriod.IsLockedByHistory) or nameof(VatPeriod.HasPendingChanges))
        {
            RaisePeriodEditabilityChanged();
        }
    }

    private void RaisePeriodEditabilityChanged()
    {
        OnPropertyChanged(nameof(IsSelectedPeriodEditable));
        OnPropertyChanged(nameof(ShowPeriodStatusBanner));
        OnPropertyChanged(nameof(ShowPeriodUnlockButton));
        OnPropertyChanged(nameof(SelectedPeriodStatusText));
    }

    [RelayCommand]
    private async Task UnlockPeriodAsync()
    {
        var period = SelectedPeriod;
        if (period is null || !period.IsLockedByHistory || period.HasPendingChanges || _confirmedProtectedPeriodIds.Contains(period.Id))
        {
            return;
        }

        var confirmed = await ConfirmAsync(
            "Odemknout období k úpravě",
            $"{period.LockReason} Opravdu ho chceš upravit?");
        if (!confirmed)
        {
            return;
        }

        _confirmedProtectedPeriodIds.Add(period.Id);
        RaisePeriodEditabilityChanged();
        StatusMessage = $"Období {period.Label} odemčeno k úpravě.";
    }

    partial void OnSelectedPeriodChanging(VatPeriod? value)
    {
        if (!_isLoading)
        {
            _ = FlushInvoicesAutosaveAsync();
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            await _repository.InitializeAsync();
            ExportDirectory = await _repository.LoadSettingAsync(ExportDirectorySettingKey) ?? ApplicationPaths.ExportDirectory;
            BackupDirectory = await _repository.LoadSettingAsync(BackupDirectorySettingKey) ?? ApplicationPaths.DataDirectory;
            TaxSubject = await _repository.LoadTaxSubjectAsync() ?? DefaultTaxSubject();

            SelectedCounterparty = null;
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

            EnsureCounterpartyDraftSelected();
            await LoadCachedTaxOfficeCatalogAsync();
            SyncTaxOfficeSelectionFromSubject();
            await Issuing.LoadAsync();
            StatusMessage = "Načteno.";
        }
        finally
        {
            _isLoading = false;
        }
    }

    // Uloží poplatníka (včetně bankovního účtu/IBAN). Volá se před generováním PDF / aktualizací přiznání DPH
    // a při zavření okna, aby se ručně zadané údaje neztratily, i když zrovna neproběhlo uložení faktur.
    public Task SaveTaxSubjectAsync() => _repository.SaveTaxSubjectAsync(TaxSubject);

    // Účet a IBAN se do formuláře vážou přes tyto proxy vlastnosti (TaxSubject je prostý objekt bez
    // notifikací). Zadání účtu rovnou dopočítá IBAN; ten zůstává ručně přepsatelný (zahraniční účet).
    public string BankAccount
    {
        get => TaxSubject.BankAccount ?? "";
        set
        {
            if ((TaxSubject.BankAccount ?? "") == value)
            {
                return;
            }

            TaxSubject.BankAccount = value;
            OnPropertyChanged();

            var iban = CzechIban.TryFromAccount(value);
            if (!string.IsNullOrEmpty(iban) && iban != TaxSubject.Iban)
            {
                TaxSubject.Iban = iban;
                OnPropertyChanged(nameof(Iban));
            }
        }
    }

    public string Iban
    {
        get => TaxSubject.Iban ?? "";
        set
        {
            if ((TaxSubject.Iban ?? "") == value)
            {
                return;
            }

            TaxSubject.Iban = value;
            OnPropertyChanged();
        }
    }

    // TaxSubject je vyměňován jako celá reference (načtení, ARES, import) – proxy pole je pak nutné
    // ručně přenotifikovat, jinak by po výměně ukazovala stará data.
    partial void OnTaxSubjectChanged(TaxSubject value)
    {
        OnPropertyChanged(nameof(BankAccount));
        OnPropertyChanged(nameof(Iban));
    }

    // Vloží nebo aktualizuje vydanou fakturu v tabulce DPH. Nové období tady záměrně nevzniká,
    // aby akce nebyla skryté "založ období a vlož".
    internal async Task<IssuedInvoiceVatUpdateResult> InsertIssuedInvoiceIntoVatAsync(IssuedInvoice invoice)
    {
        var period = Periods.FirstOrDefault(x => x.Year == invoice.TaxableSupplyDate.Year && x.Month == invoice.TaxableSupplyDate.Month);
        if (period is null)
        {
            return new(false, $"Přiznání DPH pro DUZP faktury {invoice.Number} ještě neexistuje. Faktura se do něj vloží automaticky při založení období.");
        }

        var oldPeriodIds = invoice.Id == 0
            ? new List<long>()
            : await _repository.LoadPeriodIdsForIssuedInvoiceAsync(invoice.Id);
        var isInsert = oldPeriodIds.Count == 0;

        // Potvrzení chceme i pro dotčená období, která právě nejsou v paměti – jinak by se chráněné
        // (podané) období změnilo bez dotazu. Kolekci z DB načteme jen když v paměti něco chybí.
        List<VatPeriod>? reloadedPeriods = null;
        foreach (var periodId in oldPeriodIds.Append(period.Id).Distinct())
        {
            var affectedPeriod = Periods.FirstOrDefault(x => x.Id == periodId);
            if (affectedPeriod is null)
            {
                reloadedPeriods ??= await _repository.LoadPeriodsAsync();
                affectedPeriod = reloadedPeriods.FirstOrDefault(x => x.Id == periodId);
            }

            if (affectedPeriod is not null
                && !await ConfirmProtectedPeriodChangeAsync(affectedPeriod, isInsert
                       ? "vložit vydanou fakturu do přiznání DPH"
                       : "aktualizovat vydanou fakturu v přiznání DPH"))
            {
                return new(false, isInsert ? "Vložení do přiznání DPH zrušeno." : "Aktualizace v přiznání DPH zrušena.");
            }
        }

        if (invoice.Id != 0 && oldPeriodIds.Count > 0)
        {
            await _repository.DeleteInvoiceLinesForIssuedInvoiceAsync(invoice.Id);
        }

        var inserted = await InsertIssuedInvoiceLinesAsync(invoice, period);
        if (inserted == 0)
        {
            foreach (var oldPeriodId in oldPeriodIds)
            {
                await MarkPeriodChangedAsync(oldPeriodId);
            }

            await ReloadSelectedInvoicesIfAffectedAsync(oldPeriodIds);
            return oldPeriodIds.Count > 0
                ? new(true, $"Faktura {invoice.Number} nemá žádné nenulové položky a byla z přiznání DPH vyjmuta.", IssuedInvoiceVatSyncOutcome.Removed)
                : new(false, $"Faktura {invoice.Number} zatím nemá žádné nenulové položky pro vložení do přiznání DPH.");
        }

        foreach (var oldPeriodId in oldPeriodIds.Where(id => id != period.Id))
        {
            await MarkPeriodChangedAsync(oldPeriodId);
        }

        await MarkPeriodChangedAsync(period.Id);
        if (SelectedPeriod?.Id == period.Id)
        {
            await LoadInvoicesAsync();
        }

        return new(true, isInsert
            ? $"Faktura {invoice.Number} vložena do přiznání DPH {period.Label} ({inserted} řádků)."
            : $"Faktura {invoice.Number} aktualizována v přiznání DPH {period.Label} ({inserted} řádků).");
    }

    internal async Task<IssuedInvoiceVatUpdateResult> SyncIssuedInvoiceWithOpenVatAsync(IssuedInvoice invoice)
    {
        if (invoice.Id == 0)
        {
            return new(false, "");
        }

        var period = Periods.FirstOrDefault(x => x.Year == invoice.TaxableSupplyDate.Year && x.Month == invoice.TaxableSupplyDate.Month);
        var oldPeriodIds = await _repository.LoadPeriodIdsForIssuedInvoiceAsync(invoice.Id);
        if (period is null && oldPeriodIds.Count == 0)
        {
            return new(false, "");
        }

        var affectedPeriods = await LoadAffectedPeriodsAsync(oldPeriodIds, period);
        if (affectedPeriods.Any(IsClosedForAutomaticVatSync))
        {
            return new(false, "");
        }

        if (oldPeriodIds.Count > 0)
        {
            await _repository.DeleteInvoiceLinesForIssuedInvoiceAsync(invoice.Id);
        }

        foreach (var oldPeriod in affectedPeriods.Where(x => oldPeriodIds.Contains(x.Id) && x.Id != period?.Id))
        {
            await MarkPeriodChangedAsync(oldPeriod.Id);
        }

        if (period is null)
        {
            await ReloadSelectedInvoicesIfAffectedAsync(oldPeriodIds);
            return new(true, "Faktura byla automaticky vyjmuta z otevřeného přiznání DPH.", IssuedInvoiceVatSyncOutcome.Removed);
        }

        var inserted = await InsertIssuedInvoiceLinesAsync(invoice, period);
        if (inserted == 0)
        {
            foreach (var oldPeriod in affectedPeriods.Where(x => oldPeriodIds.Contains(x.Id)))
            {
                await MarkPeriodChangedAsync(oldPeriod.Id);
            }

            await ReloadSelectedInvoicesIfAffectedAsync(oldPeriodIds);
            return oldPeriodIds.Count > 0
                ? new(true, "Faktura nemá žádné nenulové položky a byla automaticky vyjmuta z otevřeného přiznání DPH.", IssuedInvoiceVatSyncOutcome.Removed)
                : new(false, "Faktura zatím nemá žádné nenulové položky pro automatické vložení do otevřeného přiznání DPH.");
        }

        await MarkPeriodChangedAsync(period.Id);
        await ReloadSelectedInvoicesIfAffectedAsync(oldPeriodIds.Append(period.Id));

        return new(
            true,
            oldPeriodIds.Count == 0
                ? $"Faktura byla automaticky vložena do otevřeného přiznání DPH {period.Label} ({inserted} řádků)."
                : $"Faktura byla automaticky aktualizována v otevřeném přiznání DPH {period.Label} ({inserted} řádků).");
    }

    internal async Task<IssuedInvoiceVatUpdateResult> RemoveIssuedInvoiceFromOpenVatAsync(IssuedInvoice invoice)
    {
        if (invoice.Id == 0)
        {
            return new(false, "");
        }

        var oldPeriodIds = await _repository.LoadPeriodIdsForIssuedInvoiceAsync(invoice.Id);
        if (oldPeriodIds.Count == 0)
        {
            return new(false, "");
        }

        var affectedPeriods = await LoadAffectedPeriodsAsync(oldPeriodIds, targetPeriod: null);
        foreach (var period in affectedPeriods)
        {
            if (!await ConfirmProtectedPeriodChangeAsync(period, "vyjmout smazanou vydanou fakturu z přiznání DPH"))
            {
                return new(false, "Smazání faktury zrušeno, protože by změnilo uzavřené přiznání DPH.");
            }
        }

        await _repository.DeleteInvoiceLinesForIssuedInvoiceAsync(invoice.Id);
        foreach (var period in affectedPeriods)
        {
            await MarkPeriodChangedAsync(period.Id);
        }

        await ReloadSelectedInvoicesIfAffectedAsync(oldPeriodIds);
        return new(true, "Faktura byla vyjmuta z přiznání DPH.", IssuedInvoiceVatSyncOutcome.Removed);
    }

    private async Task<List<VatPeriod>> LoadAffectedPeriodsAsync(IEnumerable<long> oldPeriodIds, VatPeriod? targetPeriod)
    {
        var periodIds = oldPeriodIds.Append(targetPeriod?.Id ?? 0).Where(id => id != 0).Distinct().ToArray();
        List<VatPeriod>? reloadedPeriods = null;
        var periods = new List<VatPeriod>();
        foreach (var periodId in periodIds)
        {
            var period = Periods.FirstOrDefault(x => x.Id == periodId);
            if (period is null)
            {
                reloadedPeriods ??= await _repository.LoadPeriodsAsync();
                period = reloadedPeriods.FirstOrDefault(x => x.Id == periodId);
            }

            if (period is not null)
            {
                periods.Add(period);
            }
        }

        return periods;
    }

    private static bool IsClosedForAutomaticVatSync(VatPeriod period)
        => period.IsLockedByHistory && !period.HasPendingChanges;

    private async Task ReloadSelectedInvoicesIfAffectedAsync(IEnumerable<long> affectedPeriodIds)
    {
        if (SelectedPeriod is not null && affectedPeriodIds.Contains(SelectedPeriod.Id))
        {
            await LoadInvoicesAsync();
        }
    }

    // Najde období pro daný měsíc/rok, případně ho založí.
    private async Task<VatPeriod> EnsurePeriodForAsync(DateOnly date)
    {
        var period = Periods.FirstOrDefault(x => x.Year == date.Year && x.Month == date.Month);
        if (period is null)
        {
            period = new VatPeriod
            {
                Year = date.Year,
                Month = date.Month,
                SubmissionDate = DateOnly.FromDateTime(DateTime.Today),
                FormType = "B"
            };
            await _repository.SavePeriodAsync(period);
            Periods.Insert(0, period);
        }

        return period;
    }

    // Vloží do období řádky DPH za jednu vydanou fakturu (jeden řádek na každou sazbu). Vrací počet
    // vložených řádků. Nereloaduje – volající si řízne načtení/označení změny sám.
    private async Task<int> InsertIssuedInvoiceLinesAsync(IssuedInvoice invoice, VatPeriod period)
    {
        var inserted = 0;
        foreach (var group in invoice.VatRecap())
        {
            if (group.BaseCzk == 0 && group.VatCzk == 0)
            {
                continue;
            }

            await _repository.SaveInvoiceAsync(new InvoiceLine
            {
                PeriodId = period.Id,
                IssuedInvoiceId = invoice.Id == 0 ? null : invoice.Id,
                Kind = InvoiceKind.IssuedDomestic,
                CounterpartyId = invoice.CustomerId,
                CounterpartyName = invoice.CustomerName,
                CounterpartyDic = invoice.CustomerDic,
                EvidenceNumber = invoice.Number,
                TaxableSupplyDate = invoice.TaxableSupplyDate,
                TaxBaseCzk = group.BaseCzk,
                VatCzk = group.VatCzk,
                VatRate = group.Rate,
                Currency = "CZK"
            });
            inserted++;
        }

        return inserted;
    }

    // Keeps the subject editor bound to a real object even before the user picks one from the
    // address book, so a DIČ typed into the form is captured and "Doplnit z ARES" has something
    // to work with. The draft has to live in the address-book collection, otherwise the ListBox
    // (SelectedItem is two-way bound) would reset the selection back to null.
    private void EnsureCounterpartyDraftSelected()
    {
        if (SelectedCounterparty is not null)
        {
            return;
        }

        var reusableDraft = Counterparties.FirstOrDefault(IsBlankDraft);
        if (reusableDraft is not null)
        {
            SelectedCounterparty = reusableDraft;
            return;
        }

        var draft = new CounterpartyViewModel
        {
            CountryCode = "CZ",
            Role = CounterpartyRole.Supplier.ToString()
        };
        Counterparties.Add(draft);
        SelectedCounterparty = draft;
    }

    private static bool IsBlankDraft(CounterpartyViewModel counterparty)
        => counterparty.Id == 0
           && string.IsNullOrWhiteSpace(counterparty.Name)
           && string.IsNullOrWhiteSpace(counterparty.Dic)
           && string.IsNullOrWhiteSpace(counterparty.Ico);

    [RelayCommand]
    private async Task AddPeriodAsync()
    {
        var sourcePeriod = SelectedPeriod ?? Periods.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).FirstOrDefault();
        var newMonth = sourcePeriod is null
            ? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1)
            : new DateTime(sourcePeriod.Year, sourcePeriod.Month, 1).AddMonths(1);

        var existing = Periods.FirstOrDefault(x => x.Year == newMonth.Year && x.Month == newMonth.Month);
        if (existing is not null)
        {
            // Reuse the existing instance instead of inserting a duplicate id and re-copying
            // template invoices into an already-populated period.
            SelectedPeriod = existing;
            StatusMessage = $"Období {existing.Label} už existuje.";
            return;
        }

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

        // Když pro nové období existují vlastní vydané faktury, vloží se ony a z šablony se vynechají
        // kopírované vydané řádky (aby se vydaná plnění nezdvojila). Souhrn KH (A5) zůstává.
        var issuedInvoices = await _repository.LoadIssuedInvoicesForPeriodAsync(period.Year, period.Month);
        var hasIssued = issuedInvoices.Count > 0;

        var copied = sourcePeriod is null ? 0 : await CopyTemplateInvoicesAsync(sourcePeriod.Id, period, skipIssued: hasIssued);

        var insertedFromIssued = 0;
        var insertedAt = DateTimeOffset.UtcNow;
        foreach (var issued in issuedInvoices)
        {
            var insertedFromInvoice = await InsertIssuedInvoiceLinesAsync(issued, period);
            insertedFromIssued += insertedFromInvoice;
            if (insertedFromInvoice > 0)
            {
                await _repository.MarkIssuedInvoiceVatInsertedAsync(issued.Id, insertedAt);
                Issuing.MarkInvoiceVatInserted(issued.Id, insertedAt);
            }
        }

        await LoadInvoicesAsync();
        Issuing.RefreshVatPeriodStates();
        StatusMessage = BuildAddPeriodStatus(period, sourcePeriod, copied, issuedInvoices.Count, insertedFromIssued);
    }

    private static string BuildAddPeriodStatus(VatPeriod period, VatPeriod? sourcePeriod, int copied, int issuedInvoiceCount, int insertedFromIssued)
    {
        var parts = new List<string>();
        if (copied > 0)
        {
            parts.Add($"zkopírováno {copied} řádků jako šablona z {sourcePeriod!.Label}");
        }

        if (insertedFromIssued > 0)
        {
            parts.Add($"vloženo {insertedFromIssued} řádků z {issuedInvoiceCount} vydaných faktur");
        }

        return parts.Count == 0
            ? $"Vytvořeno období {period.Label}."
            : $"Vytvořeno období {period.Label}: {string.Join(", ", parts)}.";
    }

    [RelayCommand]
    private async Task DeleteSelectedPeriodAsync()
    {
        if (SelectedPeriod is null)
        {
            return;
        }

        var period = SelectedPeriod;
        var confirmed = await ConfirmAsync(
            "Smazat období",
            $"Opravdu chceš smazat období {period.Label} včetně všech jeho faktur? Tuto akci nejde vrátit.");
        if (!confirmed)
        {
            StatusMessage = "Smazání období zrušeno.";
            return;
        }

        await _repository.DeletePeriodAsync(period.Id);
        _confirmedProtectedPeriodIds.Remove(period.Id);
        Periods.Remove(period);
        SelectedPeriod = Periods.FirstOrDefault();
        if (SelectedPeriod is null)
        {
            Invoices.Clear();
            UpdateSummary();
        }

        Issuing.RefreshVatPeriodStates();
        StatusMessage = $"Období {period.Label} smazáno.";
    }

    [RelayCommand]
    private async Task BackupDatabaseAsync()
    {
        await _repository.InitializeAsync();
        var defaultFileName = $"dph-backup-{DateTime.Now:yyyyMMdd-HHmmss}.sqlite";
        var targetPath = await PickDatabaseBackupTargetAsync(BackupDirectory, defaultFileName);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            StatusMessage = "Záloha DB zrušena.";
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(DatabasePath, targetPath, overwrite: true);
        BackupDirectory = Path.GetDirectoryName(targetPath) ?? BackupDirectory;
        await _repository.SaveSettingAsync(BackupDirectorySettingKey, BackupDirectory);
        StatusMessage = $"DB zálohována do {targetPath}";
    }

    [RelayCommand]
    private async Task RestoreDatabaseAsync()
    {
        var sourcePath = await PickDatabaseBackupSourceAsync(BackupDirectory);
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            StatusMessage = "Obnova DB zrušena.";
            return;
        }

        if (!File.Exists(sourcePath))
        {
            StatusMessage = "Vybraný soubor zálohy neexistuje.";
            return;
        }

        var confirmed = await ConfirmAsync(
            "Obnovit databázi",
            "Obnova přepíše aktuální databázi vybranou zálohou. Opravdu chceš pokračovat?");
        if (!confirmed)
        {
            StatusMessage = "Obnova DB zrušena.";
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        var restoredBackupDirectory = Path.GetDirectoryName(sourcePath) ?? BackupDirectory;
        File.Copy(sourcePath, DatabasePath, overwrite: true);
        _confirmedProtectedPeriodIds.Clear();
        await LoadAsync();
        BackupDirectory = restoredBackupDirectory;
        await _repository.SaveSettingAsync(BackupDirectorySettingKey, BackupDirectory);
        StatusMessage = $"DB obnovena ze zálohy {sourcePath}";
    }

    [RelayCommand]
    private async Task AddCounterpartyAsync()
    {
        await _counterpartySelectionLock.WaitAsync();
        try
        {
            if (!await TrySaveCounterpartyAsync(SelectedCounterparty))
            {
                return;
            }

            var counterparty = new CounterpartyViewModel
            {
                Name = "Nový subjekt",
                CountryCode = "CZ",
                Role = CounterpartyRole.Supplier.ToString()
            };
            Counterparties.Add(counterparty);
            SelectedCounterparty = counterparty;
        }
        finally
        {
            _counterpartySelectionLock.Release();
        }
    }

    // Adresář se ukládá automaticky (jako poplatník na 1. záložce) – před přepnutím na jiný subjekt
    // a při zavření okna. Prázdné rozpracované koncepty (viz EnsureCounterpartyDraftSelected)
    // se nepersistují.
    public async Task SelectCounterpartyAsync(CounterpartyViewModel? counterparty)
    {
        await _counterpartySelectionLock.WaitAsync();
        try
        {
            if (ReferenceEquals(counterparty, SelectedCounterparty))
            {
                return;
            }

            if (_isLoading)
            {
                SelectedCounterparty = counterparty;
                return;
            }

            if (!await TrySaveCounterpartyAsync(SelectedCounterparty))
            {
                return;
            }

            SelectedCounterparty = counterparty;
        }
        finally
        {
            _counterpartySelectionLock.Release();
        }
    }

    public async Task SaveSelectedCounterpartyAsync()
    {
        await _counterpartySelectionLock.WaitAsync();
        try
        {
            await SaveCounterpartyAsync(SelectedCounterparty);
        }
        finally
        {
            _counterpartySelectionLock.Release();
        }
    }

    private async Task<bool> TrySaveCounterpartyAsync(CounterpartyViewModel? counterparty)
    {
        try
        {
            await SaveCounterpartyAsync(counterparty);
            return true;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Subjekt se nepodařilo uložit: {exception.Message}";
            return false;
        }
    }

    private async Task SaveCounterpartyAsync(CounterpartyViewModel? counterparty)
    {
        if (counterparty is null || IsBlankDraft(counterparty))
        {
            return;
        }

        var domain = counterparty.ToDomain();
        await _repository.SaveCounterpartyAsync(domain);
        counterparty.Id = domain.Id;
        if (!Counterparties.Contains(counterparty))
        {
            Counterparties.Add(counterparty);
        }

        RefreshInvoiceCounterpartyNames(counterparty);
        StatusMessage = $"Uloženo: {counterparty.DisplayName}";
    }

    [RelayCommand]
    private async Task FillCounterpartyFromAresAsync()
    {
        EnsureCounterpartyDraftSelected();
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
            // DIČ fyzické osoby = CZ + rodné číslo (9–10 číslic); IČO z něj nejde odvodit a ARES
            // neumí vyhledávat podle DIČ, takže subjekt musí dohledat uživatel přes IČO.
            StatusMessage = IsIndividualDic(SelectedCounterparty.Dic)
                ? "DIČ fyzické osoby nelze v ARES dohledat podle DIČ – vyplň IČO."
                : "Vyplň IČO nebo české DIČ ve tvaru CZ + 8 číslic.";
            return;
        }

        // Detailní dotaz (ne cache), aby se doplnila i adresa – adresář ji nově drží a vydané
        // faktury z ní vyplňují odběratele.
        AresSubjectDetail? detail;
        try
        {
            detail = await _aresClient.LookupDetailByIcoAsync(normalizedIco);
        }
        catch (HttpRequestException exception)
        {
            StatusMessage = $"ARES chyba: {exception.Message}";
            return;
        }
        catch (TaskCanceledException)
        {
            StatusMessage = "ARES neodpověděl včas.";
            return;
        }
        catch (System.Text.Json.JsonException)
        {
            StatusMessage = "ARES vrátil neočekávaná data.";
            return;
        }

        if (detail is null)
        {
            StatusMessage = "ARES subjekt nenašel.";
            return;
        }

        SelectedCounterparty.ApplyAresDetail(detail);
        await SaveCounterpartyAsync(SelectedCounterparty);
        StatusMessage = $"ARES: {detail.OfficialName}";
    }

    [RelayCommand]
    private async Task FillTaxSubjectFromAresAsync()
    {
        var normalizedIco = AresClient.NormalizeIco(TaxSubject.Ico ?? "");
        if (normalizedIco.Length != 8)
        {
            normalizedIco = AresClient.TryGetIcoFromDic(TaxSubject.Dic) ?? "";
        }

        if (normalizedIco.Length != 8)
        {
            StatusMessage = IsIndividualDic(TaxSubject.Dic)
                ? "DIČ fyzické osoby nelze v ARES dohledat podle DIČ – vyplň IČO."
                : "Vyplň IČO poplatníka nebo české DIČ ve tvaru CZ + 8 číslic.";
            return;
        }

        AresSubjectDetail? detail;
        try
        {
            detail = await _aresClient.LookupDetailByIcoAsync(normalizedIco);
        }
        catch (HttpRequestException exception)
        {
            StatusMessage = $"ARES chyba: {exception.Message}";
            return;
        }
        catch (TaskCanceledException)
        {
            StatusMessage = "ARES neodpověděl včas.";
            return;
        }
        catch (System.Text.Json.JsonException)
        {
            StatusMessage = "ARES vrátil neočekávaná data.";
            return;
        }

        if (detail is null)
        {
            StatusMessage = "ARES subjekt nenašel.";
            return;
        }

        // ARES je zdroj pravdy: nalezené údaje přepíšeme. Ručně zadané hodnoty zůstanou zachované
        // jen když subjekt v ARES není – to řeší brzký návrat výše (detail is null).
        TaxSubject.Ico = detail.Ico;
        if (detail.Dic is not null)
        {
            TaxSubject.Dic = detail.Dic;
        }

        TaxSubject.Street = detail.Street ?? "";
        TaxSubject.HouseNumber = detail.HouseNumber ?? "";
        TaxSubject.City = detail.City ?? "";
        TaxSubject.PostalCode = detail.PostalCode ?? "";

        // Cílový finanční úřad je v ARES autoritativní, takže ho přepíšeme i když už je vyplněný.
        if (!string.IsNullOrEmpty(detail.TaxOfficeCode))
        {
            TaxSubject.TaxOfficeCode = detail.TaxOfficeCode;
        }

        // TaxSubject je prostý objekt bez notifikací – přepnutím reference se přepíšou všechna
        // navázaná pole ve formuláři (jinak by se doplněné údaje ukázaly až po restartu).
        TaxSubject = CloneTaxSubject(TaxSubject);
        SyncTaxOfficeSelectionFromSubject();
        await _repository.SaveTaxSubjectAsync(TaxSubject);
        StatusMessage = detail.TaxOfficeCode is null
            ? $"ARES: {detail.OfficialName} (FÚ se nepodařilo určit, doplň ručně)"
            : $"ARES: {detail.OfficialName}, finanční úřad {detail.TaxOfficeCode}";
    }

    private static TaxSubject CloneTaxSubject(TaxSubject s) => new()
    {
        Id = s.Id,
        DisplayName = s.DisplayName,
        Dic = s.Dic,
        Ico = s.Ico,
        FirstName = s.FirstName,
        LastName = s.LastName,
        Title = s.Title,
        Street = s.Street,
        HouseNumber = s.HouseNumber,
        City = s.City,
        PostalCode = s.PostalCode,
        Country = s.Country,
        Email = s.Email,
        Phone = s.Phone,
        TaxOfficeCode = s.TaxOfficeCode,
        WorkplaceCode = s.WorkplaceCode,
        DataBoxId = s.DataBoxId,
        ActivityCode = s.ActivityCode,
        BankAccount = s.BankAccount,
        Iban = s.Iban
    };

    [RelayCommand]
    private async Task AddInvoiceAsync()
    {
        if (SelectedPeriod is null)
        {
            return;
        }

        if (!await ConfirmProtectedPeriodChangeAsync("přidat fakturu"))
        {
            return;
        }

        if (!await FlushInvoicesAutosaveAsync())
        {
            return;
        }

        var invoice = new InvoiceLineViewModel
        {
            PeriodId = SelectedPeriod.Id,
            TaxableSupplyDate = new DateOnly(SelectedPeriod.Year, SelectedPeriod.Month, DateTime.DaysInMonth(SelectedPeriod.Year, SelectedPeriod.Month)).ToString("yyyy-MM-dd"),
            Kind = "Přijatá"
        };

        var domain = PrepareInvoiceForSave(invoice);
        await _repository.SaveInvoiceAsync(domain);
        ApplySavedDomainToViewModel(invoice, domain);
        AddInvoiceViewModel(invoice);
        SelectedInvoice = invoice;
        await MarkPeriodChangedAsync(SelectedPeriod?.Id);
        StatusMessage = "Faktura přidána a uložena.";
        UpdateSummary();
    }

    [RelayCommand]
    private async Task SaveInvoicesAsync()
        => await SaveInvoicesCoreAsync(
            requirePendingChanges: false,
            successMessage: "Uloženo.",
            discardProtectedChangesOnCancel: true);

    private async Task<bool> SaveInvoicesCoreAsync(
        bool requirePendingChanges,
        string successMessage,
        bool discardProtectedChangesOnCancel = false)
    {
        if (SelectedPeriod is null)
        {
            return false;
        }

        await _saveInvoicesLock.WaitAsync();
        try
        {
            if (requirePendingChanges && !_hasPendingInvoiceChanges)
            {
                return true;
            }

            var periodId = Invoices.FirstOrDefault()?.PeriodId ?? SelectedPeriod.Id;
            var period = Periods.FirstOrDefault(x => x.Id == periodId) ?? SelectedPeriod;
            if (!await ConfirmProtectedPeriodChangeAsync(period, "uložit změny"))
            {
                if (discardProtectedChangesOnCancel)
                {
                    await DiscardInvoiceLineChangesAsync(period.Id);
                    StatusMessage = $"Změny období {period.Label} zrušeny.";
                    return true;
                }

                StatusMessage = "Změna zrušena.";
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
            _isSavingInvoices = true;
            try
            {
                for (var index = 0; index < Invoices.Count; index++)
                {
                    var invoice = Invoices[index];
                    var domain = domains[index];
                    await _repository.SaveInvoiceAsync(domain);
                    ApplySavedDomainToViewModel(invoice, domain);
                }
            }
            finally
            {
                _isSavingInvoices = false;
            }

            await MarkPeriodChangedAsync(periodId);
            _hasPendingInvoiceChanges = false;
            UpdateSummary();
            StatusMessage = successMessage;
            return true;
        }
        finally
        {
            _saveInvoicesLock.Release();
        }
    }

    private void ApplySavedDomainToViewModel(InvoiceLineViewModel invoice, InvoiceLine domain)
    {
        invoice.Id = domain.Id;
        invoice.PeriodId = domain.PeriodId;
        invoice.IssuedInvoiceId = domain.IssuedInvoiceId;
        invoice.CounterpartyId = domain.CounterpartyId;
        invoice.CounterpartyName = domain.CounterpartyName;
        invoice.CounterpartyDic = domain.CounterpartyDic ?? "";
        invoice.Counterparty = domain.CounterpartyId is null
            ? null
            : Counterparties.FirstOrDefault(x => x.Id == domain.CounterpartyId.Value);
    }

    private async Task DiscardInvoiceLineChangesAsync(long periodId)
    {
        if (SelectedPeriod?.Id == periodId)
        {
            await LoadInvoicesAsync();
        }

        _hasPendingInvoiceChanges = false;
    }

    private InvoiceLine PrepareInvoiceForSave(InvoiceLineViewModel invoice)
    {
        var domain = invoice.ToDomain();
        if (domain.PeriodId == 0)
        {
            domain.PeriodId = SelectedPeriod?.Id ?? domain.PeriodId;
        }

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

        return subject is null ? null : $"{invoice.Kind.ReferenceScope()}|{evidenceNumber}|{subject}";
    }

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

        if (!await ConfirmProtectedPeriodChangeAsync("smazat fakturu"))
        {
            StatusMessage = "Smazání zrušeno.";
            return;
        }

        if (!await FlushInvoicesAutosaveAsync())
        {
            return;
        }

        var id = SelectedInvoice.Id;
        UnsubscribeInvoiceAutosave(SelectedInvoice);
        Invoices.Remove(SelectedInvoice);
        if (id != 0)
        {
            await _repository.DeleteInvoiceAsync(id);
        }

        await MarkPeriodChangedAsync(SelectedPeriod?.Id);
        UpdateSummary();
    }

    [RelayCommand]
    private async Task ApplyCnbRateAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        if (!await ConfirmProtectedPeriodChangeAsync("změnit vybranou fakturu"))
        {
            StatusMessage = "Změna zrušena.";
            return;
        }

        var invoice = SelectedInvoice.ToDomain();
        if (invoice.Currency.Equals("CZK", StringComparison.OrdinalIgnoreCase) || invoice.ForeignAmount is null)
        {
            StatusMessage = "Pro kurz vyplň cizí měnu a částku.";
            return;
        }

        ExchangeRate? rate;
        try
        {
            rate = await _exchangeRateProvider.GetRateAsync(invoice.Currency, invoice.TaxableSupplyDate);
        }
        catch (HttpRequestException exception)
        {
            StatusMessage = $"ČNB chyba: {exception.Message}";
            return;
        }
        catch (TaskCanceledException)
        {
            StatusMessage = "ČNB neodpověděl včas.";
            return;
        }
        catch (FormatException)
        {
            StatusMessage = "ČNB vrátil neočekávaná data.";
            return;
        }

        if (rate is null)
        {
            StatusMessage = "Kurz ČNB nebyl nalezen.";
            return;
        }

        var baseCzk = VatCalculator.Money(invoice.ForeignAmount.Value * rate.RatePerUnit);
        SelectedInvoice.ExchangeRate = rate.RatePerUnit.ToString("0.####");
        SelectedInvoice.TaxBaseCzk = baseCzk.ToString("0.##");
        SelectedInvoice.VatCzk = VatCalculator.Money(baseCzk * VatCalculator.Rate(invoice.VatRate)).ToString("0.##");
        QueueInvoicesAutosave();
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

        // Už podané období (importované nebo exportované): zeptej se, zda jde o řádné (přepíše
        // stávající XML), nebo o opravu. Do lhůty pro podání je oprava „opravné“ přiznání/KH
        // (forma O); po lhůtě už opravné podat nelze – generuje se dodatečné přiznání (forma D,
        // rozdílově dle §141 DŘ) a následné kontrolní hlášení (forma N, kompletní znovu-podání).
        var corrective = false;
        var afterDeadline = FilingDeadline.IsAfterDeadline(SelectedPeriod, DateOnly.FromDateTime(DateTime.Today));
        if (SelectedPeriod.IsLockedByHistory)
        {
            var message = afterDeadline
                ? $"Období {SelectedPeriod.Label} už je vedené jako podané a lhůta pro podání ({FilingDeadline.For(SelectedPeriod.Year, SelectedPeriod.Month):dd.MM.yyyy}) uplynula. Řádné přiznání přepíše stávající XML (pokud existuje); oprava se vygeneruje jako dodatečné přiznání (rozdíly oproti poslednímu podání) a následné kontrolní hlášení."
                : $"Období {SelectedPeriod.Label} už je vedené jako podané. Řádné přiznání přepíše stávající XML (pokud existuje), opravné vytvoří nové soubory s příznakem opravného přiznání.";
            var choice = await ConfirmReexportAsync(
                "Opakovaný export období",
                message,
                afterDeadline ? "Dodatečné DP + následné KH" : "Opravné přiznání");
            switch (choice)
            {
                case ReexportChoice.Cancel:
                    StatusMessage = "Export zrušen.";
                    return;
                case ReexportChoice.Corrective:
                    corrective = true;
                    break;
            }
        }

        // Poslední známá daň pro rozdílové dodatečné přiznání; bez ní dodatečné vygenerovat nejde.
        System.Xml.Linq.XDocument[]? lastKnownReturns = null;
        if (corrective && afterDeadline)
        {
            lastKnownReturns = LoadLastKnownVatReturns(SelectedPeriod, selectedDirectory);
            if (lastKnownReturns is null)
            {
                StatusMessage = "Dodatečné přiznání nelze sestavit: nenašel se XML soubor naposledy podaného přiznání (DPHDP) pro toto období.";
                return;
            }
        }

        ExportDirectory = selectedDirectory;
        await _repository.SaveSettingAsync(ExportDirectorySettingKey, ExportDirectory);
        if (!await FlushInvoicesAutosaveAsync())
        {
            return;
        }

        await _repository.SaveTaxSubjectAsync(TaxSubject);
        Directory.CreateDirectory(ExportDirectory);

        // d_poddp (datum podání) má odpovídat dni, kdy se přiznání skutečně generuje/podává –
        // ne dni, kdy bylo období v aplikaci založeno.
        SelectedPeriod.SubmissionDate = DateOnly.FromDateTime(DateTime.Today);
        await _repository.SavePeriodAsync(SelectedPeriod);

        var invoices = Invoices.Select(x => x.ToDomain()).ToArray();
        var prefix = $"{SelectedPeriod.Year:D4}-{SelectedPeriod.Month:D2}";

        // dapdph_forma: B = řádné, O = opravné (do lhůty), D = dodatečné (po lhůtě, rozdílově).
        // khdph_forma: B = řádné, O = opravné (do lhůty), N = následné (po lhůtě, kompletní).
        var supplementary = corrective && afterDeadline;
        var vatReturnForm = corrective ? (supplementary ? "D" : "O") : "B";
        var controlStatementForm = corrective ? (supplementary ? "N" : "O") : "B";
        // Přípony podle úředního názvu dokumentu: dodatečné přiznání, ale následné kontrolní hlášení.
        var (vatReturnSuffix, controlStatementSuffix) = corrective
            ? NextCorrectiveSuffixes(prefix, supplementary ? "dodatecne" : "opravne", supplementary ? "nasledne" : "opravne")
            : ("podani", "podani");
        var vatReturnPath = Path.Combine(ExportDirectory, $"{prefix}_DPHDP_{vatReturnSuffix}.xml");
        var controlStatementPath = Path.Combine(ExportDirectory, $"{prefix}_DPHKH_{controlStatementSuffix}.xml");

        var vatReturn = _exporter.ExportVatReturn(TaxSubject, SelectedPeriod, invoices, vatReturnForm, lastKnownReturns);
        // Dodatečné přiznání beze změn (rozdíly jen v KH, např. evidenční číslo dokladu) se
        // nepodává – vygeneruje se pak jen následné kontrolní hlášení.
        var skipEmptySupplementary = supplementary
            && !vatReturn.Descendants("Veta1").Any()
            && !vatReturn.Descendants("Veta2").Any()
            && !vatReturn.Descendants("Veta4").Any();
        if (supplementary && !skipEmptySupplementary && SupplementaryTaxDifference(vatReturn) <= 0)
        {
            var reason = await RequestTextAsync(
                "Důvod dodatečného přiznání",
                "Změna daňové povinnosti na ř. 66 je menší nebo rovna nule. Portál vyžaduje důvody pro dodatečné podání.",
                "Doplnění přijatého plnění a souvisejícího následného kontrolního hlášení.");
            if (string.IsNullOrWhiteSpace(reason))
            {
                StatusMessage = "Export zrušen: pro dodatečné přiznání s ř. 66 ≤ 0 je nutné vyplnit důvod.";
                return;
            }

            vatReturn = _exporter.ExportVatReturn(TaxSubject, SelectedPeriod, invoices, vatReturnForm, lastKnownReturns, reason);
        }

        if (!skipEmptySupplementary)
        {
            vatReturn.Save(vatReturnPath);
        }

        _exporter.ExportControlStatement(TaxSubject, SelectedPeriod, invoices, controlStatementForm).Save(controlStatementPath);
        var exportedAt = DateTimeOffset.UtcNow;
        await _repository.MarkPeriodExportedAsync(SelectedPeriod.Id, exportedAt);
        SelectedPeriod.ExportedAt = exportedAt;
        SelectedPeriod.ChangedAt = null; // export odráží aktuální stav – odznak „změna“ mizí
        _confirmedProtectedPeriodIds.Remove(SelectedPeriod.Id); // po podání ať se případná další úprava znovu potvrdí
        RaisePeriodEditabilityChanged(); // období se znovu zamklo → obnovit read-only stav gridu
        Issuing.RefreshVatPeriodStates();
        StatusMessage = skipEmptySupplementary
            ? $"Přiznání beze změn (dodatečné se nepodává) – jen následné KH: {Path.GetFileName(controlStatementPath)} do {ExportDirectory}"
            : supplementary
                ? $"Dodatečné přiznání a následné KH: {Path.GetFileName(vatReturnPath)} a {Path.GetFileName(controlStatementPath)} do {ExportDirectory}"
                : $"{(corrective ? "Opravné" : "Řádné")} přiznání: {Path.GetFileName(vatReturnPath)} a {Path.GetFileName(controlStatementPath)} do {ExportDirectory}";
    }

    private static long SupplementaryTaxDifference(System.Xml.Linq.XDocument vatReturn)
    {
        var value = vatReturn.Descendants("Veta6").SingleOrDefault()?.Attribute("dano")?.Value;
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    // Oprava nesmí přepsat řádné podání ani předchozí opravy – najdeme první volné číslo společné
    // pro oba soubory (DP a KH mají po lhůtě různé přípony: dodatecne vs. nasledne).
    private (string VatReturnSuffix, string ControlStatementSuffix) NextCorrectiveSuffixes(string prefix, string vatReturnKind, string controlStatementKind)
    {
        for (var index = 1; ; index++)
        {
            var vatReturnSuffix = index == 1 ? vatReturnKind : $"{vatReturnKind}_{index}";
            var controlStatementSuffix = index == 1 ? controlStatementKind : $"{controlStatementKind}_{index}";
            var vatReturn = Path.Combine(ExportDirectory, $"{prefix}_DPHDP_{vatReturnSuffix}.xml");
            var controlStatement = Path.Combine(ExportDirectory, $"{prefix}_DPHKH_{controlStatementSuffix}.xml");
            if (!File.Exists(vatReturn) && !File.Exists(controlStatement))
            {
                return (vatReturnSuffix, controlStatementSuffix);
            }
        }
    }

    // Poslední známá daň (§141 DŘ) = hodnoty skutečně vykázané v naposledy podaném přiznání.
    // Základem je nejnovější řádné/opravné DP XML (z exportní nebo importní složky) a k němu
    // všechna novější dodatečná DP (jejich rozdíly se v exportéru přičtou). Soubory ve složkách
    // se považují za podané – nepodané vygenerované XML je potřeba smazat.
    private System.Xml.Linq.XDocument[]? LoadLastKnownVatReturns(VatPeriod period, string exportDirectory)
    {
        var prefix = $"{period.Year:D4}-{period.Month:D2}";
        var candidates = new List<string>();
        if (Directory.Exists(exportDirectory))
        {
            candidates.AddRange(Directory.EnumerateFiles(exportDirectory, $"{prefix}_DPHDP_*.xml"));
        }

        if (Directory.Exists(ImportDirectory))
        {
            candidates.AddRange(Directory.EnumerateFiles(ImportDirectory, "*.xml", SearchOption.AllDirectories));
        }

        var returns = new List<(DateTime WriteTime, string FormType, System.Xml.Linq.XDocument Document)>();
        foreach (var file in candidates.Distinct())
        {
            try
            {
                var document = System.Xml.Linq.XDocument.Load(file);
                var form = document.Root?.Elements().FirstOrDefault();
                var header = form?.Element("VetaD");
                if (form?.Name.LocalName != "DPHDP3"
                    || header?.Attribute("rok")?.Value.Trim() != period.Year.ToString(CultureInfo.InvariantCulture)
                    || !int.TryParse(header.Attribute("mesic")?.Value, out var month)
                    || month != period.Month)
                {
                    continue;
                }

                var formType = header.Attribute("dapdph_forma")?.Value.Trim().ToUpperInvariant() ?? "B";
                returns.Add((File.GetLastWriteTimeUtc(file), formType, document));
            }
            catch
            {
                // cizí nebo poškozený soubor – přeskočit
            }
        }

        var ordered = returns.OrderBy(x => x.WriteTime).ToArray();
        var baseIndex = Array.FindLastIndex(ordered, x => x.FormType is "B" or "O");
        if (baseIndex < 0)
        {
            return null;
        }

        var result = new List<System.Xml.Linq.XDocument> { ordered[baseIndex].Document };
        foreach (var item in ordered[(baseIndex + 1)..])
        {
            // Opravné dodatečné (E) nahrazuje naposledy podané dodatečné, nepřičítá se k němu.
            if (item.FormType == "E" && result.Count > 1)
            {
                result.RemoveAt(result.Count - 1);
            }

            if (item.FormType is "D" or "E")
            {
                result.Add(item.Document);
            }
        }

        return [.. result];
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
            var existingPeriod = Periods.FirstOrDefault(x => x.Year == importedPeriod.Period.Year && x.Month == importedPeriod.Period.Month);
            if (existingPeriod?.IsLockedByHistory == true
                && !await ConfirmProtectedPeriodChangeAsync(existingPeriod, "importovat data do tohoto období"))
            {
                continue;
            }

            importedPeriod.Period.ImportedAt = DateTimeOffset.UtcNow;
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
        Issuing.RefreshVatPeriodStates();
        StatusMessage = $"Import hotový. Subjektů: {imported.Counterparties.Count}, období: {imported.Periods.Count}, řádků: {importedInvoiceCount}, přeskočeno: {imported.SkippedFiles.Count}.";
    }

    private async Task ReloadPeriodsAsync(long selectedPeriodId)
    {
        Periods.Clear();
        foreach (var period in await _repository.LoadPeriodsAsync())
        {
            Periods.Add(period);
        }

        SelectedPeriod = Periods.FirstOrDefault(x => x.Id == selectedPeriodId) ?? Periods.FirstOrDefault();
        Issuing.RefreshVatPeriodStates();
    }

    // Úprava už podaného období: necháme import/export příznak a jen označíme „změna“. Děláme to
    // jednou (než zase proběhne export), takže autosave při psaní DB zbytečně netluče.
    private async Task MarkPeriodChangedAsync(long? periodId)
    {
        if (periodId is null)
        {
            return;
        }

        var period = Periods.FirstOrDefault(x => x.Id == periodId.Value);
        if (period is null || !period.IsLockedByHistory || period.HasPendingChanges)
        {
            return;
        }

        var changedAt = DateTimeOffset.UtcNow;
        await _repository.MarkPeriodChangedAsync(periodId.Value, changedAt);
        period.ChangedAt = changedAt; // VatPeriod hlásí změnu Labelu, takže se odznak v seznamu obnoví sám
        if (SelectedPeriod?.Id == period.Id)
        {
            RaisePeriodEditabilityChanged();
        }

        Issuing.RefreshVatPeriodStates();
    }

    private async Task LoadInvoicesAsync()
    {
        foreach (var invoice in Invoices)
        {
            UnsubscribeInvoiceAutosave(invoice);
        }

        _isLoadingInvoices = true;
        Invoices.Clear();
        if (SelectedPeriod is null || SelectedPeriod.Id == 0)
        {
            _isLoadingInvoices = false;
            UpdateSummary();
            return;
        }

        var loaded = await _repository.LoadInvoicesAsync(SelectedPeriod.Id);
        foreach (var invoice in loaded
                     .OrderBy(InvoiceKindOrder)
                     .ThenByDescending(x => x.GrossCzk))
        {
            var viewModel = InvoiceLineViewModel.FromDomain(invoice);
            ApplyCounterpartyReference(viewModel);
            AddInvoiceViewModel(viewModel);
        }

        _isLoadingInvoices = false;
        _hasPendingInvoiceChanges = false;
        UpdateSummary();
    }

    // Display order: vydané first, then přijaté, reverse charge last; within each group by amount
    // (gross) descending.
    private static int InvoiceKindOrder(InvoiceLine invoice) => invoice.Kind switch
    {
        InvoiceKind.IssuedDomestic => 0,
        InvoiceKind.ReceivedDomesticWithVat => 1,
        _ => 2
    };

    private void AddInvoiceViewModel(InvoiceLineViewModel invoice)
    {
        SubscribeInvoiceAutosave(invoice);
        Invoices.Add(invoice);
    }

    private void SubscribeInvoiceAutosave(InvoiceLineViewModel invoice)
        => invoice.PropertyChanged += OnInvoicePropertyChanged;

    private void UnsubscribeInvoiceAutosave(InvoiceLineViewModel invoice)
        => invoice.PropertyChanged -= OnInvoicePropertyChanged;

    private void OnInvoicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isLoadingInvoices || _isSavingInvoices || e.PropertyName is nameof(InvoiceLineViewModel.Id) or nameof(InvoiceLineViewModel.PeriodId))
        {
            return;
        }

        UpdateSummary();
        QueueInvoicesAutosave();

        if (e.PropertyName == nameof(InvoiceLineViewModel.CounterpartyDic) && sender is InvoiceLineViewModel invoice)
        {
            _ = ResolveInvoiceCounterpartyFromDicAsync(invoice);
        }
    }

    // When a Czech DIČ is typed into the invoice grid we mirror the XML-import behaviour: if no
    // subject with that DIČ exists yet (or it only has a placeholder name), pull the official name
    // from ARES, persist the subject and link the invoice to it.
    private async Task ResolveInvoiceCounterpartyFromDicAsync(InvoiceLineViewModel invoice)
    {
        if (_isResolvingInvoiceCounterparty)
        {
            return;
        }

        var dic = invoice.CounterpartyDic.NullIfWhiteSpace();
        if (dic is null)
        {
            return;
        }

        var ico = AresClient.TryGetIcoFromDic(dic);
        if (ico is null)
        {
            // Foreign or partially typed DIČ – nothing to resolve from ARES.
            return;
        }

        var normalizedDic = AresClient.NormalizeDic(dic);
        var existing = FindCounterparty(invoice.CounterpartyId, normalizedDic);
        if (existing is not null && !NeedsAresName(existing.Name))
        {
            _isResolvingInvoiceCounterparty = true;
            try
            {
                LinkInvoiceToCounterparty(invoice, existing);
            }
            finally
            {
                _isResolvingInvoiceCounterparty = false;
            }

            return;
        }

        _isResolvingInvoiceCounterparty = true;
        try
        {
            var (subject, fromCache) = await LookupAresByIcoAsync(ico);
            if (subject is null)
            {
                return;
            }

            var target = existing ?? new CounterpartyViewModel
            {
                CountryCode = "CZ",
                Role = CounterpartyRole.Supplier.ToString()
            };

            target.Ico = subject.Ico;
            target.Name = subject.OfficialName;
            target.Dic = subject.Dic ?? normalizedDic;

            var domain = target.ToDomain();
            await _repository.SaveCounterpartyAsync(domain);
            target.Id = domain.Id;
            if (!Counterparties.Contains(target))
            {
                Counterparties.Add(target);
            }

            LinkInvoiceToCounterparty(invoice, target);
            StatusMessage = fromCache ? $"ARES cache: {subject.OfficialName}" : $"ARES: {subject.OfficialName}";
        }
        finally
        {
            _isResolvingInvoiceCounterparty = false;
        }
    }

    private void LinkInvoiceToCounterparty(InvoiceLineViewModel invoice, CounterpartyViewModel counterparty)
    {
        invoice.CounterpartyId = counterparty.Id == 0 ? null : counterparty.Id;
        invoice.CounterpartyName = counterparty.DisplayName;
        invoice.CounterpartyDic = counterparty.Dic;
        invoice.Counterparty = counterparty;
    }

    private static bool NeedsAresName(string? name)
        => string.IsNullOrWhiteSpace(name) || string.Equals(name, "Nový subjekt", StringComparison.Ordinal);

    // Czech individual DIČ = CZ + rodné číslo (9–10 digits) rather than CZ + 8-digit IČO.
    private static bool IsIndividualDic(string? dic)
    {
        if (string.IsNullOrWhiteSpace(dic))
        {
            return false;
        }

        var normalized = AresClient.NormalizeDic(dic);
        var digits = normalized.StartsWith("CZ", StringComparison.OrdinalIgnoreCase) ? normalized[2..] : normalized;
        return digits.Length is 9 or 10;
    }

    private void QueueInvoicesAutosave()
    {
        _hasPendingInvoiceChanges = true;
        _autosaveInvoicesCts?.Cancel();
        var cts = new CancellationTokenSource();
        _autosaveInvoicesCts = cts;
        _ = AutosaveInvoicesAfterDelayAsync(cts.Token);
    }

    private async Task AutosaveInvoicesAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(500, cancellationToken);
            await SaveInvoicesCoreAsync(
                requirePendingChanges: true,
                successMessage: "Automaticky uloženo.",
                discardProtectedChangesOnCancel: true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<bool> FlushInvoicesAutosaveAsync()
    {
        _autosaveInvoicesCts?.Cancel();
        return await SaveInvoicesCoreAsync(
            requirePendingChanges: true,
            successMessage: "Automaticky uloženo.",
            discardProtectedChangesOnCancel: true);
    }

    // skipIssued: vynechá kopírování vydaných řádků (kromě souhrnu KH A5) – použije se, když cílové
    // období má vlastní vydané faktury, které se do něj vloží napřímo.
    private async Task<int> CopyTemplateInvoicesAsync(long sourcePeriodId, VatPeriod targetPeriod, bool skipIssued = false)
    {
        var sourceInvoices = await _repository.LoadInvoicesAsync(sourcePeriodId);
        var copied = 0;
        foreach (var source in sourceInvoices)
        {
            if (skipIssued && source.Kind == InvoiceKind.IssuedDomestic && !IsControlStatementSummary(source))
            {
                continue;
            }

            source.Id = 0;
            source.PeriodId = targetPeriod.Id;
            // Kopie je jen předloha – nesmí zůstat svázaná s vydanou fakturou zdrojového měsíce,
            // jinak by ji pozdější uložení té faktury smazalo/přepsalo v cizím období.
            source.IssuedInvoiceId = null;
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
        => InvoiceKindClassifier.IsControlStatementSummary(invoice.EvidenceNumber);

    private Task<bool> ConfirmProtectedPeriodChangeAsync(string action)
        => SelectedPeriod is null
            ? Task.FromResult(true)
            : ConfirmProtectedPeriodChangeAsync(SelectedPeriod, action);

    private Task<bool> ConfirmProtectedPeriodChangeAsync(VatPeriod period, string action)
    {
        if (!period.IsLockedByHistory || period.HasPendingChanges)
        {
            return Task.FromResult(true);
        }

        if (period.Id != 0 && _confirmedProtectedPeriodIds.Contains(period.Id))
        {
            return Task.FromResult(true);
        }

        var state = period.ImportedAt is not null && period.ExportedAt is not null
            ? "importované i exportované"
            : period.ImportedAt is not null
                ? "importované"
                : "exportované";

        return ConfirmProtectedPeriodChangeCoreAsync(period, state, action);
    }

    private async Task<bool> ConfirmProtectedPeriodChangeCoreAsync(VatPeriod period, string state, string action)
    {
        var confirmed = await ConfirmAsync(
            "Potvrdit změnu období",
            $"Období {period.Label} už je {state}. Opravdu chceš {action}?");
        if (confirmed && period.Id != 0)
        {
            _confirmedProtectedPeriodIds.Add(period.Id);
        }

        return confirmed;
    }

    private (long Id, string Name, string? Dic)? ResolveCounterpartyReference(long? id, string? dic)
    {
        var counterparty = FindCounterparty(id, dic);
        return counterparty is null
            ? null
            : (counterparty.Id, counterparty.DisplayName, counterparty.Dic.NullIfWhiteSpace());
    }

    private void ApplyCounterpartyReference(InvoiceLine invoice)
    {
        if (ResolveCounterpartyReference(invoice.CounterpartyId, invoice.CounterpartyDic) is not { } reference)
        {
            return;
        }

        invoice.CounterpartyId = reference.Id;
        invoice.CounterpartyName = reference.Name;
        invoice.CounterpartyDic = reference.Dic;
    }

    private void ApplyCounterpartyReference(InvoiceLineViewModel invoice)
    {
        if (ResolveCounterpartyReference(invoice.CounterpartyId, invoice.CounterpartyDic) is not { } reference)
        {
            return;
        }

        invoice.CounterpartyId = reference.Id;
        invoice.CounterpartyName = reference.Name;
        invoice.CounterpartyDic = reference.Dic ?? "";
        invoice.Counterparty = Counterparties.FirstOrDefault(x => x.Id == reference.Id);
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
            invoice.Counterparty = counterparty;
        }
    }

    private void UpdateSummary()
    {
        var domains = Invoices.Select(x => x.ToDomain()).ToArray();
        var summary = _calculator.Calculate(domains);
        SummaryText =
            $"Výstup: {summary.DomesticOutputBase:0.##} / {summary.DomesticOutputVat:0.##} Kč | " +
            $"Odpočet: {summary.DomesticInputBase:0.##} / {summary.DomesticInputVat:0.##} Kč | " +
            $"Reverse: {summary.ReverseChargeBase:0.##} / {summary.ReverseChargeVat:0.##} Kč";

        // Co se reálně platí = vlastní daňová povinnost v celých korunách (ř.64 DP), ne haléřový
        // součet z průběžného výpočtu.
        var net = _exporter.ComputeNetTaxWholeCrowns(domains);
        AmountToPayText = net switch
        {
            > 0 => $"Zaplatit: {net.ToString("N0", CzkFormat)} Kč",
            < 0 => $"Nadměrný odpočet: {(-net).ToString("N0", CzkFormat)} Kč",
            _ => "Bez doplatku"
        };
        // Holé číslo bez mezer a „Kč“ – ať jde rovnou vložit do platby v bance.
        AmountToPayCopyValue = Math.Abs(net).ToString(CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private async Task CopyAmountToPayAsync()
    {
        await CopyToClipboardAsync(AmountToPayCopyValue);
        StatusMessage = $"Zkopírováno do schránky: {AmountToPayCopyValue} Kč";
    }

    private static TaxSubject DefaultTaxSubject() => new()
    {
        Country = "Česká Republika",
        ActivityCode = "620000"
    };
}

public enum ReexportChoice
{
    Cancel,
    Regular,
    Corrective
}
