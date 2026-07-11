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
