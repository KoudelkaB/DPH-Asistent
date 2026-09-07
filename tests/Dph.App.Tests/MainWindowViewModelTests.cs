using Dph.App.ViewModels;
using Dph.Core.Domain;
using Dph.Core.Epo;
using Dph.Core.Persistence;
using Dph.Core.Services;

namespace Dph.App.Tests;

// Integrační testy hlavního VM nad skutečnou SQLite v temp souboru; síťové služby jsou falešné.
public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task Switching_Period_Flushes_Pending_Edits_Before_Loading_The_New_Period()
    {
        // Regrese souběhu: flush autosave starého období a načtení nového se musí serializovat,
        // jinak se uložené hodnoty zapíšou do právě načtených řádků cizího období.
        var repository = await CreateRepositoryAsync();
        var older = await SeedPeriodAsync(repository, 2026, 5);
        var newer = await SeedPeriodAsync(repository, 2026, 6);
        var olderLine = await SeedLineAsync(repository, older, "B-1", "Dodavatel B", 200m, 42m);
        await SeedLineAsync(repository, newer, "A-1", "Dodavatel A", 100m, 21m);

        var viewModel = CreateViewModel(repository);
        await WaitForAsync(() => viewModel.StatusMessage == "Načteno.", "načtení dat");
        await WaitForAsync(
            () => viewModel.Invoices.Count == 1 && viewModel.Invoices[0].PeriodId == newer.Id,
            "řádky novějšího období");

        // Rozpracovaná změna (autosave čeká na 500ms prodlevu) + okamžité přepnutí období.
        viewModel.Invoices[0].TaxBaseCzk = "999";
        viewModel.SelectedPeriod = viewModel.Periods.Single(x => x.Id == older.Id);

        await WaitForAsync(
            () => viewModel.Invoices.Count == 1 && viewModel.Invoices[0].PeriodId == older.Id,
            "řádky staršího období");
        await WaitForAsync(
            async () => (await repository.LoadInvoicesAsync(newer.Id)).Single().TaxBaseCzk == 999m,
            "uložení rozpracované změny opouštěného období");

        // Řádek starého období zůstal v gridu se svou vlastní identitou i hodnotami…
        Assert.Equal(olderLine.Id, viewModel.Invoices[0].Id);
        Assert.Equal("B-1", viewModel.Invoices[0].EvidenceNumber);
        Assert.Equal("200", viewModel.Invoices[0].TaxBaseCzk);

        // …a v DB ho flush nepřepsal daty z druhého období.
        var olderRow = (await repository.LoadInvoicesAsync(older.Id)).Single();
        Assert.Equal(200m, olderRow.TaxBaseCzk);
        Assert.Equal(42m, olderRow.VatCzk);
    }

    [Fact]
    public async Task Autosave_Persists_Grid_Edits_After_Delay()
    {
        var repository = await CreateRepositoryAsync();
        var period = await SeedPeriodAsync(repository, 2026, 5);
        await SeedLineAsync(repository, period, "B-1", "Dodavatel B", 200m, 42m);

        var viewModel = CreateViewModel(repository);
        await WaitForAsync(() => viewModel.StatusMessage == "Načteno.", "načtení dat");
        await WaitForAsync(() => viewModel.Invoices.Count == 1, "načtení řádků");

        viewModel.Invoices[0].TaxBaseCzk = "555";

        await WaitForAsync(
            async () => (await repository.LoadInvoicesAsync(period.Id)).Single().TaxBaseCzk == 555m,
            "automatické uložení po prodlevě");
    }

    [Fact]
    public async Task Declined_Protected_Period_Confirmation_Discards_Changes_Without_Deadlock()
    {
        // Zahození změn běží uvnitř zámku ukládání a načítá řádky znovu – nesmí se zaseknout
        // (LoadInvoicesCoreAsync se volá bez opětovného čekání na _saveInvoicesLock).
        var repository = await CreateRepositoryAsync();
        var period = await SeedPeriodAsync(repository, 2026, 5);
        await SeedLineAsync(repository, period, "B-1", "Dodavatel B", 200m, 42m);
        await repository.MarkPeriodExportedAsync(period.Id, DateTimeOffset.UtcNow);

        var viewModel = CreateViewModel(repository);
        await WaitForAsync(() => viewModel.StatusMessage == "Načteno.", "načtení dat");
        await WaitForAsync(() => viewModel.Invoices.Count == 1, "načtení řádků");
        viewModel.ConfirmAsync = (_, _) => Task.FromResult(false);

        viewModel.Invoices[0].TaxBaseCzk = "999";
        var save = viewModel.SaveInvoicesCommand.ExecuteAsync(null);
        var finished = await Task.WhenAny(save, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(finished == save, "Uložení se zaseklo – pravděpodobný deadlock na _saveInvoicesLock.");
        await save;

        // Odmítnutá změna chráněného období se zahodí v UI i v DB.
        await WaitForAsync(
            () => viewModel.Invoices.Count == 1 && viewModel.Invoices[0].TaxBaseCzk == "200",
            "vrácení původní hodnoty do gridu");
        Assert.Equal(200m, (await repository.LoadInvoicesAsync(period.Id)).Single().TaxBaseCzk);
    }

    [Fact]
    public async Task AddInvoice_Persists_A_Row_Immediately()
    {
        var repository = await CreateRepositoryAsync();
        var period = await SeedPeriodAsync(repository, 2026, 5);

        var viewModel = CreateViewModel(repository);
        await WaitForAsync(() => viewModel.StatusMessage == "Načteno.", "načtení dat");

        await viewModel.AddInvoiceCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.SelectedInvoice);
        Assert.NotEqual(0, viewModel.SelectedInvoice!.Id);
        var row = Assert.Single(await repository.LoadInvoicesAsync(period.Id));
        Assert.Equal(viewModel.SelectedInvoice.Id, row.Id);
        Assert.Equal(new DateOnly(2026, 5, 31), row.TaxableSupplyDate);
    }

    [Fact]
    public async Task Bank_Account_Proxy_Computes_Iban()
    {
        var repository = await CreateRepositoryAsync();
        await SeedPeriodAsync(repository, 2026, 5);

        var viewModel = CreateViewModel(repository);
        await WaitForAsync(() => viewModel.StatusMessage == "Načteno.", "načtení dat");

        viewModel.BankAccount = "19-2000145399/0800";

        Assert.Equal("CZ6508000000192000145399", viewModel.Iban);
    }

    [Fact]
    public async Task Multi_Rate_Document_Can_Be_Saved_Without_False_Duplicate()
    {
        var repository = await CreateRepositoryAsync();
        var period = await SeedPeriodAsync(repository, 2026, 5);
        await SeedLineAsync(repository, period, "F1", "Dodavatel", 6000m, 1260m);
        var second = new InvoiceLine { PeriodId = period.Id, Kind = InvoiceKind.ReceivedDomesticWithVat,
            EvidenceNumber = "F1", CounterpartyName = "Dodavatel",
            TaxableSupplyDate = new(2026, 5, 15), VatRate = VatRateKind.Reduced12, TaxBaseCzk = 3000m, VatCzk = 360m };
        await repository.SaveInvoiceAsync(second);
        var vm = CreateViewModel(repository);
        await WaitForAsync(() => vm.StatusMessage == "Načteno.", "načtení");
        await WaitForAsync(() => vm.Invoices.Count == 2, "řádky");
        vm.Invoices.Single(x => x.VatRate == "12").TaxBaseCzk = "3100";
        await vm.SaveInvoicesCommand.ExecuteAsync(null);
        Assert.Equal(3100m, (await repository.LoadInvoicesAsync(period.Id)).Single(x => x.VatRate == VatRateKind.Reduced12).TaxBaseCzk);
    }

    [Fact]
    public async Task Invalid_Input_Does_Not_Overwrite_Stored_Amount()
    {
        var repository = await CreateRepositoryAsync();
        var period = await SeedPeriodAsync(repository, 2026, 5);
        await SeedLineAsync(repository, period, "F1", "Dodavatel", 1000m, 210m);
        var vm = CreateViewModel(repository);
        await WaitForAsync(() => vm.StatusMessage == "Načteno.", "načtení");
        await WaitForAsync(() => vm.Invoices.Count == 1, "řádky");
        vm.Invoices[0].TaxBaseCzk = "chyba";
        await vm.SaveInvoicesCommand.ExecuteAsync(null);
        Assert.Equal(1000m, Assert.Single(await repository.LoadInvoicesAsync(period.Id)).TaxBaseCzk);
        Assert.Contains("Neplatné číslo", vm.StatusMessage);
    }

    [Fact]
    public async Task Foreign_Invoice_Is_Not_Inserted_As_Czk()
    {
        var repository = await CreateRepositoryAsync();
        var period = await SeedPeriodAsync(repository, 2026, 5);
        var vm = CreateViewModel(repository);
        await WaitForAsync(() => vm.StatusMessage == "Načteno.", "načtení");
        vm.Issuing.SelectedInvoice = new IssuedInvoiceViewModel
        {
            Number = "EUR1", Currency = "EUR", TaxableSupplyDate = "2026-05-31"
        };
        vm.Issuing.SelectedInvoice.Items.Add(new() { UnitPriceCzk = "100" });
        Assert.False(await vm.Issuing.SaveSelectedInvoiceAsync());
        Assert.Empty(await repository.LoadInvoicesAsync(period.Id));
    }

    [Fact]
    public async Task Applying_Cnb_Rate_Refreshes_Vat_When_Base_Is_Unchanged()
    {
        var repository = await CreateRepositoryAsync();
        var period = await SeedPeriodAsync(repository, 2026, 5);
        var row = await SeedLineAsync(repository, period, "F1", "Dodavatel", 1000m, 123m);
        row.Currency = "EUR";
        row.ForeignAmount = 40m;
        await repository.SaveInvoiceAsync(row);
        var vm = new MainWindowViewModel(repository, new FakeAresClient(), new FixedExchangeRateProvider(), new FakeTaxOfficeCatalog());
        await WaitForAsync(() => vm.StatusMessage == "Načteno.", "načtení");
        await WaitForAsync(() => vm.Invoices.Count == 1, "řádky");
        vm.SelectedInvoice = vm.Invoices[0];
        await vm.ApplyCnbRateCommand.ExecuteAsync(null);
        Assert.Equal("1000", vm.SelectedInvoice.TaxBaseCzk);
        Assert.Equal("210", vm.SelectedInvoice.VatCzk);
        Assert.Equal("1210", vm.SelectedInvoice.GrossCzk);
        await vm.SaveInvoicesCommand.ExecuteAsync(null);
        Assert.Equal(210m, Assert.Single(await repository.LoadInvoicesAsync(period.Id)).VatCzk);
    }

    [Fact]
    public async Task Preflight_Fails_Before_Export_Dialog_And_Period_Save()
    {
        var repository = await CreateRepositoryAsync();
        var period = await SeedPeriodAsync(repository, 2026, 5);
        await SeedLineAsync(repository, period, "F1", "Dodavatel", 5000m, 1050m);
        var row = new InvoiceLine { PeriodId = period.Id, Kind = InvoiceKind.ReceivedDomesticWithVat,
            CounterpartyName = "Dodavatel", EvidenceNumber = "F1", TaxableSupplyDate = new(2026, 5, 15),
            VatRate = VatRateKind.Reduced12, TaxBaseCzk = 5000m, VatCzk = 600m };
        await repository.SaveInvoiceAsync(row);
        var vm = CreateViewModel(repository);
        await WaitForAsync(() => vm.StatusMessage == "Načteno.", "načtení");
        await WaitForAsync(() => vm.Invoices.Count == 2, "řádky");
        var dialogCalled = false;
        vm.PickExportDirectoryAsync = _ => { dialogCalled = true; return Task.FromResult<string?>(null); };
        await vm.ExportXmlCommand.ExecuteAsync(null);
        Assert.False(dialogCalled);
        Assert.Contains("DIČ", vm.StatusMessage);
        Assert.Contains("Odpočet:", vm.SummaryText);
        Assert.Equal("", vm.AmountToPayCopyValue);
        Assert.Equal(period.SubmissionDate, (await repository.LoadPeriodsAsync()).Single().SubmissionDate);
    }

    [Fact]
    public async Task Creating_Period_Does_Not_Insert_Legacy_Zero_Rate_Invoice()
    {
        var repository = await CreateRepositoryAsync();
        await SeedPeriodAsync(repository, 2026, 5);
        var invoice = new IssuedInvoice { Number = "ZERO1", TaxableSupplyDate = new(2026, 6, 15),
            Items = [new() { UnitPriceCzk = 500m }, new() { UnitPriceCzk = 1000m, VatRate = VatRateKind.Zero0 }] };
        await repository.SaveIssuedInvoiceAsync(invoice);
        var vm = CreateViewModel(repository);
        await WaitForAsync(() => vm.StatusMessage == "Načteno.", "načtení");
        await vm.AddPeriodCommand.ExecuteAsync(null);
        var period = (await repository.LoadPeriodsAsync()).Single(x => x.Month == 6);
        Assert.Empty(await repository.LoadInvoicesAsync(period.Id));
        Assert.Contains("ZERO1", vm.StatusMessage);
        Assert.Null((await repository.LoadIssuedInvoiceAsync(invoice.Id))!.VatInsertedAt);
    }

    [Fact]
    public async Task Saving_Zero_Rate_Invoice_Does_Not_Pollute_Vat_Period()
    {
        var repository = await CreateRepositoryAsync();
        var period = await SeedPeriodAsync(repository, 2026, 5);
        var vm = CreateViewModel(repository);
        await WaitForAsync(() => vm.StatusMessage == "Načteno.", "načtení");
        vm.Issuing.SelectedInvoice = new IssuedInvoiceViewModel { Number = "ZERO1", TaxableSupplyDate = "2026-05-15" };
        vm.Issuing.SelectedInvoice.Items.Add(new() { UnitPriceCzk = "1000", VatRate = "0" });
        Assert.False(await vm.Issuing.SaveSelectedInvoiceAsync());
        Assert.Empty(await repository.LoadInvoicesAsync(period.Id));
    }

    [Fact]
    public async Task Existing_Zero_Rate_Invoice_Can_Save_Address_Without_Entering_Vat()
    {
        var repository = await CreateRepositoryAsync();
        var period = await SeedPeriodAsync(repository, 2026, 5);
        var invoice = new IssuedInvoice { Number = "LEGACY0", TaxableSupplyDate = new(2026, 5, 15),
            Items = [new() { UnitPriceCzk = 1000m, VatRate = VatRateKind.Zero0 }] };
        await repository.SaveIssuedInvoiceAsync(invoice);
        var vm = CreateViewModel(repository);
        await WaitForAsync(() => vm.StatusMessage == "Načteno.", "načtení");
        vm.Issuing.SelectedInvoice = IssuedInvoiceViewModel.FromDomain(invoice);
        vm.Issuing.SelectedInvoice.CustomerStreet = "Opravená 123";

        Assert.True(await vm.Issuing.SaveSelectedInvoiceAsync());

        var saved = (await repository.LoadIssuedInvoiceAsync(invoice.Id))!;
        Assert.Equal("Opravená 123", saved.CustomerStreet);
        Assert.Equal(VatRateKind.Zero0, Assert.Single(saved.Items).VatRate);
        Assert.Null(saved.VatInsertedAt);
        Assert.Empty(await repository.LoadInvoicesAsync(period.Id));
    }

    [Fact]
    public async Task Legacy_Zero_Rate_Can_Be_Corrected_Without_Losing_Summary()
    {
        var repository = await CreateRepositoryAsync();
        var period = await SeedPeriodAsync(repository, 2026, 5);
        var line = await SeedLineAsync(repository, period, "LEGACY", "Dodavatel", 1000m, 0m);
        line.VatRate = VatRateKind.Zero0;
        await repository.SaveInvoiceAsync(line);
        var vm = CreateViewModel(repository);
        await WaitForAsync(() => vm.StatusMessage == "Načteno.", "načtení");
        await WaitForAsync(() => vm.Invoices.Count == 1, "řádky");
        Assert.Contains("Odpočet:", vm.SummaryText);
        Assert.Contains("Nelze exportovat:", vm.SummaryText);
        Assert.Equal("0", vm.Invoices[0].VatRate);
        vm.Invoices[0].VatRate = "21";
        await vm.SaveInvoicesCommand.ExecuteAsync(null);
        Assert.DoesNotContain("Nelze exportovat:", vm.SummaryText);
        var rows = await repository.LoadInvoicesAsync(period.Id);
        Assert.Equal(210m, Assert.Single(rows).VatCzk);
        EpoXmlExporter.ValidateSupportedLines(period, rows);
        Assert.NotNull(new EpoXmlExporter().ExportControlStatement(new(), period, rows));
    }

    private sealed class FixedExchangeRateProvider : IExchangeRateProvider
    {
        public Task<ExchangeRate?> GetRateAsync(string currencyCode, DateOnly date, CancellationToken cancellationToken = default)
            => Task.FromResult<ExchangeRate?>(new(date, currencyCode, 1, 25m));
    }

    private static async Task<DphRepository> CreateRepositoryAsync()
    {
        var repository = new DphRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite"));
        await repository.InitializeAsync();
        return repository;
    }

    private static async Task<VatPeriod> SeedPeriodAsync(DphRepository repository, int year, int month)
    {
        var period = new VatPeriod
        {
            Year = year,
            Month = month,
            SubmissionDate = new DateOnly(year, month, 1),
            FormType = "B"
        };
        await repository.SavePeriodAsync(period);
        return period;
    }

    private static async Task<InvoiceLine> SeedLineAsync(
        DphRepository repository, VatPeriod period, string evidenceNumber, string name, decimal baseCzk, decimal vatCzk)
    {
        var line = new InvoiceLine
        {
            PeriodId = period.Id,
            Kind = InvoiceKind.ReceivedDomesticWithVat,
            CounterpartyName = name,
            EvidenceNumber = evidenceNumber,
            TaxableSupplyDate = new DateOnly(period.Year, period.Month, 15),
            TaxBaseCzk = baseCzk,
            VatCzk = vatCzk,
            Currency = "CZK",
            VatRate = VatRateKind.Standard21
        };
        await repository.SaveInvoiceAsync(line);
        return line;
    }

    private static MainWindowViewModel CreateViewModel(DphRepository repository)
        => new(repository, new FakeAresClient(), new FakeExchangeRateProvider(), new FakeTaxOfficeCatalog());

    private static Task WaitForAsync(Func<bool> condition, string description)
        => WaitForAsync(() => Task.FromResult(condition()), description);

    // VM pouští načítání/ukládání jako fire-and-forget úlohy – testy proto na výsledek čekají
    // pollingem. Výjimka z podmínky (např. čtení kolekce uprostřed výměny) se počítá jako "ještě ne".
    private static async Task WaitForAsync(Func<Task<bool>> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await condition())
                {
                    return;
                }
            }
            catch
            {
                // kolekce se právě mění – zkusíme to znovu
            }

            await Task.Delay(25);
        }

        Assert.Fail($"Vypršel čas při čekání na: {description}");
    }

    private sealed class FakeAresClient : IAresClient
    {
        public Task<AresSubject?> LookupByIcoAsync(string ico, CancellationToken cancellationToken = default)
            => Task.FromResult<AresSubject?>(null);

        public Task<AresSubject?> LookupByDicAsync(string dic, CancellationToken cancellationToken = default)
            => Task.FromResult<AresSubject?>(null);

        public Task<AresSubjectDetail?> LookupDetailByIcoAsync(string ico, CancellationToken cancellationToken = default)
            => Task.FromResult<AresSubjectDetail?>(null);
    }

    private sealed class FakeExchangeRateProvider : IExchangeRateProvider
    {
        public Task<ExchangeRate?> GetRateAsync(string currencyCode, DateOnly date, CancellationToken cancellationToken = default)
            => Task.FromResult<ExchangeRate?>(null);
    }

    private sealed class FakeTaxOfficeCatalog : ITaxOfficeCatalog
    {
        public Task<TaxOfficeCatalogData?> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<TaxOfficeCatalogData?>(null);
    }
}
