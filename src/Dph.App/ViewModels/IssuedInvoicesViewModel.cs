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
    private readonly Func<IssuedInvoice, Task<string>> _insertIntoVatAsync;
    private readonly Action<string> _setStatus;
    private readonly InvoicePdfRenderer _pdfRenderer = new();
    private readonly HashSet<long> _hydratedInvoiceIds = [];

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

    public IssuedInvoicesViewModel(
        DphRepository repository,
        IAresClient aresClient,
        ObservableCollection<CounterpartyViewModel> counterparties,
        Func<TaxSubject> getSupplier,
        Func<Task> saveSupplierAsync,
        Func<IssuedInvoice, Task<string>> insertIntoVatAsync,
        Action<string> setStatus)
    {
        _repository = repository;
        _aresClient = aresClient;
        _counterparties = counterparties;
        _getSupplier = getSupplier;
        _saveSupplierAsync = saveSupplierAsync;
        _insertIntoVatAsync = insertIntoVatAsync;
        _setStatus = setStatus;
    }

    public async Task LoadAsync()
    {
        _pdfDirectory = await _repository.LoadSettingAsync(PdfDirectorySettingKey) ?? ApplicationPaths.ExportDirectory;
        Invoices.Clear();
        _hydratedInvoiceIds.Clear();
        foreach (var invoice in await _repository.LoadIssuedInvoicesAsync())
        {
            Invoices.Add(IssuedInvoiceViewModel.FromDomain(invoice));
        }

        SelectedInvoice = Invoices.FirstOrDefault();
    }

    // Faktura se ukládá automaticky při přepnutí na jinou a při zavření okna – "Uložit fakturu"
    // tak není potřeba (tlačítka "Vložit do DPH" a "Uložit PDF" ukládají také). Přepnutí výběru
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

        // Položky se načítají líně až při výběru faktury (seznam drží jen hlavičky).
        if (newValue is null || newValue.Id == 0 || !_hydratedInvoiceIds.Add(newValue.Id))
        {
            return;
        }

        var full = await _repository.LoadIssuedInvoiceAsync(newValue.Id);
        if (full is null)
        {
            return;
        }

        newValue.Items.Clear();
        foreach (var item in full.Items)
        {
            newValue.Items.Add(IssuedInvoiceItemViewModel.FromDomain(item));
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
        await SaveInvoiceAsync(SelectedInvoice);

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
            Invoices.Insert(0, invoice);
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

    public Task SaveSelectedInvoiceAsync() => SaveInvoiceAsync(SelectedInvoice);

    private async Task SaveInvoiceAsync(IssuedInvoiceViewModel? invoice)
    {
        if (invoice is null)
        {
            return;
        }

        var domain = invoice.ToDomain();
        if (string.IsNullOrWhiteSpace(domain.Number))
        {
            _setStatus("Faktura musí mít číslo.");
            return;
        }

        await _repository.SaveIssuedInvoiceAsync(domain);
        invoice.Id = domain.Id;
        _hydratedInvoiceIds.Add(domain.Id);
        _setStatus($"Uložena faktura {domain.Number}.");
    }

    [RelayCommand]
    private async Task DeleteInvoiceAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        var invoice = SelectedInvoice;
        if (invoice.Id != 0)
        {
            await _repository.DeleteIssuedInvoiceAsync(invoice.Id);
            _hydratedInvoiceIds.Remove(invoice.Id);
        }

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

        _setStatus($"Faktura {invoice.Number} smazána.");
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
        await SaveInvoiceAsync(SelectedInvoice);
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
        _suppressAutoSave = true;
        try
        {
            Invoices.Insert(0, viewModel);
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

        await SaveInvoiceAsync(SelectedInvoice);
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
        _setStatus($"PDF uloženo: {target}");
    }

    [RelayCommand]
    private async Task InsertIntoVatAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        await SaveInvoiceAsync(SelectedInvoice);
        await _saveSupplierAsync();
        var message = await _insertIntoVatAsync(SelectedInvoice.ToDomain());
        _setStatus(message);
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
