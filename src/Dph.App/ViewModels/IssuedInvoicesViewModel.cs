using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dph.Core.Domain;
using Dph.Core.Invoicing;
using Dph.Core.Persistence;
using Dph.Core.Services;

namespace Dph.App.ViewModels;

// Agenda vydaných faktur. Žije jako child VM v MainWindowViewModel a sdílí s ním repository,
// ARES klienta, adresář (Counterparties), aktuálního dodavatele a vkládání do tabulky DPH.
public partial class IssuedInvoicesViewModel : ViewModelBase
{
    private const string PdfDirectorySettingKey = "invoice_pdf_directory";

    private readonly DphRepository _repository;
    private readonly IAresClient _aresClient;
    private readonly ObservableCollection<CounterpartyViewModel> _counterparties;
    private readonly Func<TaxSubject> _getSupplier;
    private readonly Func<Task> _saveSupplierAsync;
    private readonly Func<DateOnly, IssuedInvoiceVatPeriodState> _getVatPeriodState;
    private readonly Func<IssuedInvoice, Task<IssuedInvoiceVatUpdateResult>> _updateVatAsync;
    private readonly Func<IssuedInvoice, Task<IssuedInvoiceVatUpdateResult>> _syncOpenVatAsync;
    private readonly Func<IssuedInvoice, Task<IssuedInvoiceVatUpdateResult>> _removeFromOpenVatAsync;
    private readonly Action<string> _setStatus;
    private readonly InvoicePdfRenderer _pdfRenderer = new();
    private readonly Dictionary<long, Task> _invoiceHydrationTasks = [];

    // Potlačí automatické uložení "opouštěné" faktury při programové změně výběru (mazání),
    // kde by se právě smazaná faktura okamžitě uložila zpět.
    private bool _suppressAutoSave;

    private string _pdfDirectory = ApplicationPaths.ExportDirectory;

    [ObservableProperty] private IssuedInvoiceViewModel? selectedInvoice;
    [ObservableProperty] private CounterpartyViewModel? selectedCustomer;

    public ObservableCollection<IssuedInvoiceViewModel> Invoices { get; } = [];
    public ObservableCollection<CounterpartyViewModel> Customers => _counterparties;

    // Picker pro cílové PDF (dir, default name) -> cesta, nebo null. Navazuje se z code-behind.
    public Func<string, string, Task<string?>> PickPdfTargetAsync { get; set; } =
        (_, _) => Task.FromResult<string?>(null);
    public Func<string, string, Task<bool>> ConfirmAsync { get; set; } =
        (_, _) => Task.FromResult(true);

    public IssuedInvoicesViewModel(
        DphRepository repository,
        IAresClient aresClient,
        ObservableCollection<CounterpartyViewModel> counterparties,
        Func<TaxSubject> getSupplier,
        Func<Task> saveSupplierAsync,
        Func<DateOnly, IssuedInvoiceVatPeriodState> getVatPeriodState,
        Func<IssuedInvoice, Task<IssuedInvoiceVatUpdateResult>> updateVatAsync,
        Func<IssuedInvoice, Task<IssuedInvoiceVatUpdateResult>> syncOpenVatAsync,
        Func<IssuedInvoice, Task<IssuedInvoiceVatUpdateResult>> removeFromOpenVatAsync,
        Action<string> setStatus)
    {
        _repository = repository;
        _aresClient = aresClient;
        _counterparties = counterparties;
        _getSupplier = getSupplier;
        _saveSupplierAsync = saveSupplierAsync;
        _getVatPeriodState = getVatPeriodState;
        _updateVatAsync = updateVatAsync;
        _syncOpenVatAsync = syncOpenVatAsync;
        _removeFromOpenVatAsync = removeFromOpenVatAsync;
        _setStatus = setStatus;
    }

    public async Task LoadAsync()
    {
        _pdfDirectory = await _repository.LoadSettingAsync(PdfDirectorySettingKey) ?? ApplicationPaths.ExportDirectory;
        _suppressAutoSave = true;
        try
        {
            SelectedInvoice = null;
            foreach (var invoice in Invoices)
            {
                invoice.PropertyChanged -= OnInvoicePropertyChanged;
            }

            Invoices.Clear();
            _invoiceHydrationTasks.Clear();
            // Jen hlavičky; položky se dotáhnou líně při výběru faktury (EnsureInvoiceHydratedAsync).
            foreach (var invoice in await _repository.LoadIssuedInvoicesAsync())
            {
                AddInvoiceViewModel(IssuedInvoiceViewModel.FromDomain(invoice, itemsLoaded: false));
            }

            SelectedInvoice = Invoices.FirstOrDefault();
        }
        finally
        {
            _suppressAutoSave = false;
        }

        _ = BackfillMissingTotalsAsync();
    }

    public void RefreshVatPeriodStates()
    {
        foreach (var invoice in Invoices)
        {
            UpdateVatPeriodState(invoice);
        }
    }

    public void MarkInvoiceVatInserted(long invoiceId, DateTimeOffset insertedAt)
    {
        var invoice = Invoices.FirstOrDefault(x => x.Id == invoiceId);
        if (invoice is null)
        {
            return;
        }

        invoice.VatInsertedAt = insertedAt;
        invoice.VatChangedAt = null;
        invoice.IsUnlocked = false;
        UpdateVatPeriodState(invoice);
    }

    private void AddInvoiceViewModel(IssuedInvoiceViewModel invoice)
    {
        invoice.PropertyChanged += OnInvoicePropertyChanged;
        UpdateVatPeriodState(invoice);
        Invoices.Add(invoice);
    }

    private void InsertInvoiceViewModel(int index, IssuedInvoiceViewModel invoice)
    {
        invoice.PropertyChanged += OnInvoicePropertyChanged;
        UpdateVatPeriodState(invoice);
        Invoices.Insert(index, invoice);
    }

    private void OnInvoicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IssuedInvoiceViewModel.TaxableSupplyDate)
            && sender is IssuedInvoiceViewModel invoice)
        {
            UpdateVatPeriodState(invoice);
        }
    }

    private void UpdateVatPeriodState(IssuedInvoiceViewModel invoice)
    {
        invoice.VatPeriodState = DateOnly.TryParse(invoice.TaxableSupplyDate, out var date)
            ? _getVatPeriodState(date)
            : IssuedInvoiceVatPeriodState.Missing;
    }

    // Staré faktury (uložené před zavedením denormalizovaných souhrnů) nemají uložený souhrn a v
    // seznamu by ukazovaly nuly. Jednorázově je dopočítáme z položek a uložíme; příště se načtou
    // rovnou z hlavičky. Běží na pozadí, aby se seznam zobrazil hned.
    private async Task BackfillMissingTotalsAsync()
    {
        foreach (var invoice in Invoices.Where(x => x.Id != 0 && !x.HasCachedTotals).ToList())
        {
            try
            {
                var full = await _repository.LoadIssuedInvoiceAsync(invoice.Id);
                if (full is null)
                {
                    continue;
                }

                await _repository.UpdateIssuedInvoiceTotalsAsync(full.Id, full.TotalBaseCzk, full.TotalVatCzk);
                invoice.SetCachedTotals(full.TotalBaseCzk, full.TotalVatCzk);
            }
            catch (Exception exception)
            {
                _setStatus($"Souhrn faktury {invoice.Number} se nepodařilo dopočítat: {exception.Message}");
            }
        }
    }

    // Faktura se ukládá automaticky při přepnutí na jinou a při zavření okna – "Uložit fakturu"
    // tak není potřeba (tlačítka "Aktualizovat v přiznání DPH" a "Uložit PDF" ukládají také). Přepnutí výběru
    // při mazání faktury se přeskočí, jinak by se smazaná faktura hned zase vložila zpět.
    async partial void OnSelectedInvoiceChanged(IssuedInvoiceViewModel? oldValue, IssuedInvoiceViewModel? newValue)
    {
        if (!_suppressAutoSave)
        {
            // Handler je async void – neošetřená výjimka by shodila celou aplikaci, proto ji tady
            // zachytíme a jen zobrazíme ve stavovém řádku.
            try
            {
                await SaveInvoiceAsync(oldValue);
            }
            catch (Exception exception)
            {
                _setStatus($"Fakturu se nepodařilo uložit: {exception.Message}");
            }
        }

        try
        {
            await EnsureInvoiceHydratedAsync(newValue);
        }
        catch (Exception exception)
        {
            _setStatus($"Fakturu se nepodařilo načíst: {exception.Message}");
        }
    }

    partial void OnSelectedCustomerChanged(CounterpartyViewModel? value)
    {
        if (value is null || SelectedInvoice is null)
        {
            return;
        }

        SelectedInvoice.CustomerId = value.Id == 0 ? null : value.Id;
        SelectedInvoice.CustomerName = value.DisplayName;
        SelectedInvoice.CustomerIco = value.Ico;
        SelectedInvoice.CustomerDic = value.Dic;
        SelectedInvoice.CustomerStreet = value.Street;
        SelectedInvoice.CustomerHouseNumber = value.HouseNumber;
        SelectedInvoice.CustomerCity = value.City;
        SelectedInvoice.CustomerPostalCode = value.PostalCode;
        SelectedInvoice.CustomerCountry = CountryDisplayName(value.CountryCode);
    }

    private static string CountryDisplayName(string countryCode)
        => string.IsNullOrWhiteSpace(countryCode) || countryCode.Equals("CZ", StringComparison.OrdinalIgnoreCase)
            ? "Česká republika"
            : countryCode;

    [RelayCommand]
    private async Task NewInvoiceAsync()
    {
        // Rozpracovanou fakturu uložíme jako první – číslo nové pak vychází čistě z DB a nemůže
        // kolidovat (UNIQUE constraint na issued_invoices.number). Uložení už proběhlo, takže
        // automatické uložení "opouštěné" faktury při přepnutí výběru tady potlačíme.
        if (!await SaveInvoiceAsync(SelectedInvoice, autoSyncOpenVat: false))
        {
            return;
        }

        var number = await _repository.NextInvoiceNumberAsync(DateTime.Today.Year);
        var invoice = new IssuedInvoiceViewModel
        {
            Number = number,
            IntroText = DefaultIntroText(DateTime.Today)
        };
        invoice.Items.Add(new IssuedInvoiceItemViewModel());

        _suppressAutoSave = true;
        try
        {
            InsertInvoiceViewModel(0, invoice);
            SelectedInvoice = invoice;
        }
        finally
        {
            _suppressAutoSave = false;
        }

        _setStatus($"Nová faktura {number}.");
    }

    // Při vystavení v poslední den měsíce předvyplní úvodní text šablonou s placeholdery; konkrétní
    // měsíc/rok se dosadí podle DUZP až při generování PDF (viz InvoiceText.ResolvePlaceholders).
    private static string DefaultIntroText(DateTime today)
        => today.Day == DateTime.DaysInMonth(today.Year, today.Month)
            ? InvoiceText.DefaultIntroTemplate
            : "";

    [RelayCommand]
    private void AddItem()
    {
        SelectedInvoice?.Items.Add(new IssuedInvoiceItemViewModel());
    }

    [RelayCommand]
    private void RemoveItem(IssuedInvoiceItemViewModel? item)
    {
        if (item is not null)
        {
            SelectedInvoice?.Items.Remove(item);
        }
    }

    // Chráněnou (uzamčenou) fakturu jde upravovat až po vědomém odemčení – potvrzení tak přijde
    // předem, ne až při ukládání. Detail je do té doby read-only (viz IssuedInvoiceViewModel.IsEditable).
    [RelayCommand]
    private async Task UnlockInvoiceAsync()
    {
        var invoice = SelectedInvoice;
        if (invoice is null || !invoice.IsLockedByHistory || invoice.IsUnlocked)
        {
            return;
        }

        var confirmed = await ConfirmAsync(
            "Odemknout vydanou fakturu",
            $"{invoice.LockReason} Opravdu ji chceš upravit?");
        if (confirmed)
        {
            invoice.IsUnlocked = true;
            _setStatus($"Faktura {invoice.Number} odemčena k úpravě.");
        }
    }

    public Task<bool> SaveSelectedInvoiceAsync() => SaveInvoiceAsync(SelectedInvoice);

    private async Task<bool> SaveInvoiceAsync(IssuedInvoiceViewModel? invoice, bool autoSyncOpenVat = true)
    {
        if (invoice is null)
        {
            return true;
        }

        await EnsureInvoiceHydratedAsync(invoice);
        var domain = invoice.ToDomain();
        if (string.IsNullOrWhiteSpace(domain.Number))
        {
            _setStatus("Faktura musí mít číslo.");
            return false;
        }

        var changed = await HasInvoiceChangedAsync(domain);
        if (!changed)
        {
            if (autoSyncOpenVat && ShouldAutoSyncOpenVat(invoice))
            {
                var syncResult = await _syncOpenVatAsync(invoice.ToDomain());
                if (syncResult.Updated)
                {
                    await ApplyVatSyncResultAsync(invoice, syncResult);
                }

                if (!string.IsNullOrWhiteSpace(syncResult.Message))
                {
                    _setStatus($"Faktura {domain.Number} je beze změn. {syncResult.Message}");
                }
            }

            return true;
        }

        await _repository.SaveIssuedInvoiceAsync(domain);
        invoice.Id = domain.Id;
        invoice.ItemsLoaded = true;
        if (invoice.IsLockedByHistory)
        {
            var changedAt = DateTimeOffset.UtcNow;
            await _repository.MarkIssuedInvoiceChangedAsync(invoice.Id, changedAt);
            if (invoice.PdfExportedAt is not null)
            {
                invoice.PdfChangedAt = changedAt;
            }

            if (invoice.VatInsertedAt is not null)
            {
                invoice.VatChangedAt = changedAt;
            }
        }

        var status = $"Uložena faktura {domain.Number}.";
        if (autoSyncOpenVat)
        {
            var syncResult = await _syncOpenVatAsync(invoice.ToDomain());
            if (syncResult.Updated)
            {
                await ApplyVatSyncResultAsync(invoice, syncResult);
            }

            if (!string.IsNullOrWhiteSpace(syncResult.Message))
            {
                status = $"{status} {syncResult.Message}";
            }
        }

        _setStatus(status);
        return true;
    }

    private static bool ShouldAutoSyncOpenVat(IssuedInvoiceViewModel invoice)
        => invoice.VatPeriodState == IssuedInvoiceVatPeriodState.Open
           && (invoice.VatInsertedAt is null || invoice.HasVatPendingChanges);

    private async Task ApplyVatSyncResultAsync(IssuedInvoiceViewModel invoice, IssuedInvoiceVatUpdateResult result)
    {
        switch (result.Outcome)
        {
            case IssuedInvoiceVatSyncOutcome.InsertedOrUpdated:
                var insertedAt = DateTimeOffset.UtcNow;
                await _repository.MarkIssuedInvoiceVatInsertedAsync(invoice.Id, insertedAt);
                invoice.VatInsertedAt = insertedAt;
                invoice.VatChangedAt = null;
                invoice.IsUnlocked = false;
                break;
            case IssuedInvoiceVatSyncOutcome.Removed:
                await _repository.ClearIssuedInvoiceVatInsertedAsync(invoice.Id);
                invoice.VatInsertedAt = null;
                invoice.VatChangedAt = null;
                invoice.IsUnlocked = false;
                break;
        }

        UpdateVatPeriodState(invoice);
    }

    // Seznam vydaných faktur drží jen hlavičky; před uložením nebo použitím jako šablony musí být
    // položky načtené, jinak by se do DB propsala prázdná sada řádků.
    private async Task EnsureInvoiceHydratedAsync(IssuedInvoiceViewModel? invoice)
    {
        if (invoice is null || invoice.Id == 0 || invoice.ItemsLoaded)
        {
            return;
        }

        if (!_invoiceHydrationTasks.TryGetValue(invoice.Id, out var task))
        {
            task = HydrateInvoiceAsync(invoice);
            _invoiceHydrationTasks[invoice.Id] = task;
        }

        try
        {
            await task;
        }
        finally
        {
            _invoiceHydrationTasks.Remove(invoice.Id);
        }
    }

    private async Task HydrateInvoiceAsync(IssuedInvoiceViewModel invoice)
    {
        var full = await _repository.LoadIssuedInvoiceAsync(invoice.Id);
        if (full is null)
        {
            return;
        }

        invoice.Items.Clear();
        foreach (var item in full.Items)
        {
            invoice.Items.Add(IssuedInvoiceItemViewModel.FromDomain(item));
        }

        invoice.ItemsLoaded = true;
    }

    private async Task<bool> HasInvoiceChangedAsync(IssuedInvoice current)
    {
        if (current.Id == 0)
        {
            return true;
        }

        var saved = await _repository.LoadIssuedInvoiceAsync(current.Id);
        return saved is null || !InvoiceContentEquals(saved, current);
    }

    private static bool InvoiceContentEquals(IssuedInvoice left, IssuedInvoice right)
    {
        return left.Number == right.Number
               && left.IssueDate == right.IssueDate
               && left.TaxableSupplyDate == right.TaxableSupplyDate
               && left.DueDate == right.DueDate
               && left.CustomerId == right.CustomerId
               && NullOrEmpty(left.CustomerName) == NullOrEmpty(right.CustomerName)
               && NullOrEmpty(left.CustomerIco) == NullOrEmpty(right.CustomerIco)
               && NullOrEmpty(left.CustomerDic) == NullOrEmpty(right.CustomerDic)
               && NullOrEmpty(left.CustomerStreet) == NullOrEmpty(right.CustomerStreet)
               && NullOrEmpty(left.CustomerHouseNumber) == NullOrEmpty(right.CustomerHouseNumber)
               && NullOrEmpty(left.CustomerCity) == NullOrEmpty(right.CustomerCity)
               && NullOrEmpty(left.CustomerPostalCode) == NullOrEmpty(right.CustomerPostalCode)
               && NullOrEmpty(left.CustomerCountry) == NullOrEmpty(right.CustomerCountry)
               && NullOrEmpty(left.Currency) == NullOrEmpty(right.Currency)
               && NullOrEmpty(left.VariableSymbol) == NullOrEmpty(right.VariableSymbol)
               && NullOrEmpty(left.PaymentMethod) == NullOrEmpty(right.PaymentMethod)
               && NullOrEmpty(left.IntroText) == NullOrEmpty(right.IntroText)
               && NullOrEmpty(left.Note) == NullOrEmpty(right.Note)
               && NullOrEmpty(left.Footer) == NullOrEmpty(right.Footer)
               && InvoiceItemsEqual(left.Items, right.Items);
    }

    private static bool InvoiceItemsEqual(IReadOnlyList<IssuedInvoiceItem> left, IReadOnlyList<IssuedInvoiceItem> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!InvoiceItemEquals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool InvoiceItemEquals(IssuedInvoiceItem left, IssuedInvoiceItem right)
        => NullOrEmpty(left.Description) == NullOrEmpty(right.Description)
           && left.Quantity == right.Quantity
           && NullOrEmpty(left.Unit) == NullOrEmpty(right.Unit)
           && left.UnitPriceCzk == right.UnitPriceCzk
           && left.VatRate == right.VatRate;

    private static string NullOrEmpty(string? value) => value ?? "";

    [RelayCommand]
    private async Task DeleteInvoiceAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        var invoice = SelectedInvoice;
        // Smazání celé faktury je jednorázová akce s vlastním potvrzením – nevyžaduje odemčení,
        // ale u uzamčené faktury se ptáme (odpovídá chování mazání celého období).
        if (invoice.IsLockedByHistory
            && !await ConfirmAsync("Smazat vydanou fakturu", $"{invoice.LockReason} Opravdu ji chceš smazat?"))
        {
            _setStatus("Smazání zrušeno.");
            return;
        }

        var vatRemoval = invoice.Id == 0
            ? new IssuedInvoiceVatUpdateResult(false, "")
            : await _removeFromOpenVatAsync(invoice.ToDomain());
        if (!vatRemoval.Updated && !string.IsNullOrWhiteSpace(vatRemoval.Message))
        {
            _setStatus(vatRemoval.Message);
            return;
        }

        if (invoice.Id != 0)
        {
            await _repository.DeleteIssuedInvoiceAsync(invoice.Id);
        }

        invoice.PropertyChanged -= OnInvoicePropertyChanged;
        _suppressAutoSave = true;
        try
        {
            Invoices.Remove(invoice);
            SelectedInvoice = Invoices.FirstOrDefault();
        }
        finally
        {
            _suppressAutoSave = false;
        }

        _setStatus(vatRemoval.Updated
            ? $"Faktura {invoice.Number} smazána. {vatRemoval.Message}"
            : $"Faktura {invoice.Number} smazána.");
    }

    // Z vybrané faktury udělá novou (šablonu): zkopíruje odběratele a položky, přidělí nové číslo.
    [RelayCommand]
    private async Task UseAsTemplateAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        // Šablonu (zdroj) uložíme jako první, aby další číslo vyšlo čistě z DB (viz NewInvoiceAsync).
        if (!await SaveInvoiceAsync(SelectedInvoice, autoSyncOpenVat: false))
        {
            return;
        }
        var source = SelectedInvoice.ToDomain();
        var number = await _repository.NextInvoiceNumberAsync(DateTime.Today.Year);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var copy = new IssuedInvoice
        {
            Number = number,
            IssueDate = today,
            TaxableSupplyDate = today,
            DueDate = today.AddDays(14),
            CustomerId = source.CustomerId,
            CustomerName = source.CustomerName,
            CustomerIco = source.CustomerIco,
            CustomerDic = source.CustomerDic,
            CustomerStreet = source.CustomerStreet,
            CustomerHouseNumber = source.CustomerHouseNumber,
            CustomerCity = source.CustomerCity,
            CustomerPostalCode = source.CustomerPostalCode,
            CustomerCountry = source.CustomerCountry,
            Currency = source.Currency,
            PaymentMethod = source.PaymentMethod,
            Footer = source.Footer,
            Items = source.Items.Select(x => new IssuedInvoiceItem
            {
                Description = x.Description,
                Quantity = x.Quantity,
                Unit = x.Unit,
                UnitPriceCzk = x.UnitPriceCzk,
                VatRate = x.VatRate
            }).ToList()
        };

        var viewModel = IssuedInvoiceViewModel.FromDomain(copy);
        if (!await SaveInvoiceAsync(viewModel))
        {
            return;
        }
        _suppressAutoSave = true;
        try
        {
            InsertInvoiceViewModel(0, viewModel);
            SelectedInvoice = viewModel;
        }
        finally
        {
            _suppressAutoSave = false;
        }

        _setStatus($"Vytvořena faktura {number} ze šablony {source.Number}.");
    }

    [RelayCommand]
    private async Task FillCustomerFromAresAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        var ico = AresClient.NormalizeIco(SelectedInvoice.CustomerIco);
        if (ico.Length != 8)
        {
            ico = AresClient.TryGetIcoFromDic(SelectedInvoice.CustomerDic) ?? "";
        }

        if (ico.Length != 8)
        {
            _setStatus("Vyplň IČO odběratele (8 číslic) nebo české DIČ.");
            return;
        }

        AresSubjectDetail? detail;
        try
        {
            detail = await _aresClient.LookupDetailByIcoAsync(ico);
        }
        catch (HttpRequestException exception)
        {
            _setStatus($"ARES chyba: {exception.Message}");
            return;
        }
        catch (TaskCanceledException)
        {
            _setStatus("ARES neodpověděl včas.");
            return;
        }
        catch (System.Text.Json.JsonException)
        {
            _setStatus("ARES vrátil neočekávaná data.");
            return;
        }

        if (detail is null)
        {
            _setStatus("ARES subjekt nenašel.");
            return;
        }

        SelectedInvoice.CustomerIco = detail.Ico;
        SelectedInvoice.CustomerName = detail.OfficialName;
        SelectedInvoice.CustomerDic = detail.Dic ?? SelectedInvoice.CustomerDic;
        SelectedInvoice.CustomerStreet = detail.Street ?? "";
        SelectedInvoice.CustomerHouseNumber = detail.HouseNumber ?? "";
        SelectedInvoice.CustomerCity = detail.City ?? "";
        SelectedInvoice.CustomerPostalCode = detail.PostalCode ?? "";
        SelectedInvoice.CustomerCountry = "Česká republika";

        // Je-li odběratel navázaný na adresář, obnovíme uložený subjekt na aktuální data z ARES.
        if (SelectedInvoice.CustomerId is long counterpartyId)
        {
            var linked = _counterparties.FirstOrDefault(c => c.Id == counterpartyId);
            if (linked is not null)
            {
                linked.ApplyAresDetail(detail);
                await _repository.SaveCounterpartyAsync(linked.ToDomain());
            }
        }

        _setStatus($"ARES: {detail.OfficialName}");
    }

    [RelayCommand]
    private async Task SavePdfAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        if (!await SaveInvoiceAsync(SelectedInvoice))
        {
            return;
        }
        await _saveSupplierAsync();
        var domain = SelectedInvoice.ToDomain();
        var supplier = _getSupplier();
        var defaultName = BuildPdfFileName(supplier, domain);
        var target = await PickPdfTargetAsync(_pdfDirectory, defaultName);
        if (string.IsNullOrWhiteSpace(target))
        {
            _setStatus("Uložení PDF zrušeno.");
            return;
        }

        try
        {
            _pdfRenderer.Render(supplier, domain, target);
        }
        catch (Exception exception)
        {
            _setStatus($"Chyba při generování PDF: {exception.Message}");
            return;
        }

        _pdfDirectory = Path.GetDirectoryName(target) ?? _pdfDirectory;
        await _repository.SaveSettingAsync(PdfDirectorySettingKey, _pdfDirectory);
        var exportedAt = DateTimeOffset.UtcNow;
        await _repository.MarkIssuedInvoicePdfExportedAsync(SelectedInvoice.Id, exportedAt);
        SelectedInvoice.PdfExportedAt = exportedAt;
        SelectedInvoice.PdfChangedAt = null;
        SelectedInvoice.IsUnlocked = false; // po exportu se faktura zase zamkne
        _setStatus($"PDF uloženo: {target}");
    }

    [RelayCommand]
    private async Task UpdateVatAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        if (!SelectedInvoice.ShowVatActionButton)
        {
            _setStatus("Přiznání DPH není potřeba měnit.");
            return;
        }

        if (!await SaveInvoiceAsync(SelectedInvoice))
        {
            return;
        }
        await _saveSupplierAsync();
        var result = await _updateVatAsync(SelectedInvoice.ToDomain());
        if (!result.Updated)
        {
            _setStatus(result.Message);
            return;
        }

        await ApplyVatSyncResultAsync(SelectedInvoice, result);
        _setStatus(result.Message);
    }

    // Název PDF = "číslo – dodavatel – odběratel.pdf", očištěný od znaků nepovolených v cestě.
    private static string BuildPdfFileName(TaxSubject supplier, IssuedInvoice invoice)
    {
        var parts = new[] { invoice.Number, SupplierName(supplier), invoice.CustomerName }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim());
        var raw = string.Join(" – ", parts);
        var sanitized = string.Concat(raw.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return $"{(string.IsNullOrWhiteSpace(sanitized) ? "faktura" : sanitized)}.pdf";
    }

    private static string? SupplierName(TaxSubject supplier)
    {
        var personName = string.Join(" ", new[] { supplier.FirstName, supplier.LastName }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!string.IsNullOrWhiteSpace(supplier.DisplayName))
        {
            return supplier.DisplayName.Trim();
        }

        return string.IsNullOrWhiteSpace(personName) ? null : personName.Trim();
    }
}

public sealed record IssuedInvoiceVatUpdateResult(
    bool Updated,
    string Message,
    IssuedInvoiceVatSyncOutcome Outcome = IssuedInvoiceVatSyncOutcome.InsertedOrUpdated);

public enum IssuedInvoiceVatSyncOutcome
{
    None,
    InsertedOrUpdated,
    Removed
}
