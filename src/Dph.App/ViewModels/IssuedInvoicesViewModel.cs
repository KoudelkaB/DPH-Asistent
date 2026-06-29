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

    // Položky se načítají líně až při výběru faktury (seznam drží jen hlavičky).
    async partial void OnSelectedInvoiceChanged(IssuedInvoiceViewModel? value)
    {
        if (value is null || value.Id == 0 || !_hydratedInvoiceIds.Add(value.Id))
        {
            return;
        }

        var full = await _repository.LoadIssuedInvoiceAsync(value.Id);
        if (full is null)
        {
            return;
        }

        value.Items.Clear();
        foreach (var item in full.Items)
        {
            value.Items.Add(IssuedInvoiceItemViewModel.FromDomain(item));
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
    }

    [RelayCommand]
    private async Task NewInvoiceAsync()
    {
        var number = await _repository.NextInvoiceNumberAsync(DateTime.Today.Year);
        var invoice = new IssuedInvoiceViewModel
        {
            Number = number,
            IntroText = DefaultIntroText(DateTime.Today)
        };
        invoice.Items.Add(new IssuedInvoiceItemViewModel());
        Invoices.Insert(0, invoice);
        SelectedInvoice = invoice;
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

    [RelayCommand]
    private async Task SaveInvoiceAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        var domain = SelectedInvoice.ToDomain();
        if (string.IsNullOrWhiteSpace(domain.Number))
        {
            _setStatus("Faktura musí mít číslo.");
            return;
        }

        await _repository.SaveIssuedInvoiceAsync(domain);
        SelectedInvoice.Id = domain.Id;
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

        Invoices.Remove(invoice);
        SelectedInvoice = Invoices.FirstOrDefault();
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
        Invoices.Insert(0, viewModel);
        SelectedInvoice = viewModel;
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

        SelectedInvoice.CustomerId = null;
        SelectedInvoice.CustomerIco = detail.Ico;
        SelectedInvoice.CustomerName = detail.OfficialName;
        SelectedInvoice.CustomerDic = detail.Dic ?? SelectedInvoice.CustomerDic;
        SelectedInvoice.CustomerStreet = detail.Street ?? "";
        SelectedInvoice.CustomerHouseNumber = detail.HouseNumber ?? "";
        SelectedInvoice.CustomerCity = detail.City ?? "";
        SelectedInvoice.CustomerPostalCode = detail.PostalCode ?? "";
        SelectedInvoice.CustomerCountry = "Česká republika";
        _setStatus($"ARES: {detail.OfficialName}");
    }

    [RelayCommand]
    private async Task SavePdfAsync()
    {
        if (SelectedInvoice is null)
        {
            return;
        }

        await SaveInvoiceAsync();
        await _saveSupplierAsync();
        var domain = SelectedInvoice.ToDomain();
        var defaultName = BuildPdfFileName(domain);
        var target = await PickPdfTargetAsync(_pdfDirectory, defaultName);
        if (string.IsNullOrWhiteSpace(target))
        {
            _setStatus("Uložení PDF zrušeno.");
            return;
        }

        try
        {
            _pdfRenderer.Render(_getSupplier(), domain, target);
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

        await SaveInvoiceAsync();
        await _saveSupplierAsync();
        var message = await _insertIntoVatAsync(SelectedInvoice.ToDomain());
        _setStatus(message);
    }

    // Název PDF = "číslo-odběratel.pdf" (text z levého sloupce), očištěný od znaků nepovolených v cestě.
    private static string BuildPdfFileName(IssuedInvoice invoice)
    {
        var raw = string.IsNullOrWhiteSpace(invoice.CustomerName)
            ? invoice.Number
            : $"{invoice.Number}-{invoice.CustomerName}";
        var sanitized = string.Concat(raw.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return $"{(string.IsNullOrWhiteSpace(sanitized) ? "faktura" : sanitized)}.pdf";
    }
}
