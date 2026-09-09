using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dph.App.ViewModels;
using Dph.App.Views;
using Dph.Core.Domain;
using Dph.Core.Epo;
using Dph.Core.Isds;
using Dph.Core.Persistence;
using Dph.Core.Services;

namespace Dph.App.Tests;

// Odeslání podání datovou schránkou nad skutečnou SQLite a skutečným exportem XML; ISDS je falešné.
[Collection("Avalonia")]
public sealed class SendToTaxOfficeTests
{
    [Fact]
    public async Task Exported_Xml_Is_Sent_As_Two_Messages_With_Zfo_And_Delivery_Note()
    {
        var context = await ExportAsync();

        Assert.True(context.ViewModel.CanSendToTaxOffice);
        Assert.Contains("k odeslání", context.Period.Label);

        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);

        // Přiznání i kontrolní hlášení jdou jako samostatná podání příslušnému finančnímu úřadu.
        Assert.Equal(2, context.Isds.SentMessages.Count);
        Assert.All(context.Isds.SentMessages, x => Assert.Equal("7nyn2d9", x.Recipient));
        Assert.Equal(
            ["2026-05_DPHDP_podani.xml", "2026-05_DPHKH_podani.xml"],
            context.Isds.SentMessages.Select(x => x.Attachments.Single().FileName));
        Assert.Contains("přiznání k DPH za 05/2026", context.Isds.SentMessages[0].Annotation);
        Assert.Contains("kontrolní hlášení DPH za 05/2026", context.Isds.SentMessages[1].Annotation);

        // Názvy ZFO nesou ID zprávy (100001 pro přiznání, 100002 pro kontrolní hlášení).
        foreach (var (name, messageId) in new[] { ("DPHDP", "100001"), ("DPHKH", "100002") })
        {
            Assert.True(File.Exists(Path.Combine(context.Directory, $"2026-05_{name}_podani_{messageId}_zprava.zfo")));
            Assert.True(File.Exists(Path.Combine(context.Directory, $"2026-05_{name}_podani_{messageId}_dorucenka.zfo")));
        }

        Assert.False(context.ViewModel.CanSendToTaxOffice);
        Assert.Contains("odesláno", context.Period.Label);
        Assert.DoesNotContain("k odeslání", context.Period.Label);
        Assert.Contains("odesláno 2", context.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Credentials_Are_Asked_For_Once_And_Reused()
    {
        var context = await ExportAsync();
        var prompts = 0;
        context.ViewModel.RequestIsdsCredentialsAsync = (_, _, _) =>
        {
            prompts++;
            return Task.FromResult<IsdsCredentials?>(new IsdsCredentials("uzivatel", "heslo"));
        };

        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);
        Assert.Equal(1, prompts);
        Assert.Equal(new IsdsCredentials("uzivatel", "heslo"), context.CredentialStore.Stored);

        // Druhé období použije uložené údaje bez dalšího dotazu.
        await ExportSecondPeriodAsync(context);
        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);

        Assert.Equal(1, prompts);
        Assert.Equal(4, context.Isds.SentMessages.Count); // 2 podání za květen + 2 za červen
    }

    [Fact]
    public async Task Wrong_Credentials_Are_Not_Stored()
    {
        var context = await ExportAsync();
        context.Isds.OwnerError = new IsdsException("Chybné jméno nebo heslo", "401") { IsAuthenticationFailure = true };

        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);

        Assert.Null(context.CredentialStore.Stored);
        Assert.Empty(context.Isds.SentMessages);
        Assert.Contains("chybí přihlašovací údaje", context.ViewModel.StatusMessage);
        Assert.True(context.ViewModel.CanSendToTaxOffice);
    }

    [Fact]
    public async Task Rejected_Stored_Password_Is_Forgotten_So_The_Next_Attempt_Asks_Again()
    {
        var context = await ExportAsync(new InMemoryCredentialStore(new IsdsCredentials("uzivatel", "stare-heslo")));
        context.Isds.CreateMessageError = new IsdsException("Chybné heslo", "401") { IsAuthenticationFailure = true };

        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);

        Assert.Null(context.CredentialStore.Stored);
        Assert.Equal(1, context.CredentialStore.ClearCount);
        Assert.Contains("Odeslání selhalo", context.ViewModel.StatusMessage);
        // Podání zůstává neodeslané, tlačítko dál aktivní.
        Assert.True(context.ViewModel.CanSendToTaxOffice);
    }

    [Fact]
    public async Task Cancelling_The_Confirmation_Sends_Nothing()
    {
        var context = await ExportAsync();
        context.ViewModel.ConfirmAsync = (_, _) => Task.FromResult(false);

        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);

        Assert.Empty(context.Isds.SentMessages);
        Assert.Equal("Odeslání zrušeno.", context.ViewModel.StatusMessage);
        Assert.True(context.ViewModel.CanSendToTaxOffice);
    }

    [Fact]
    public async Task Unknown_Tax_Office_Blocks_The_Send()
    {
        var context = await ExportAsync();
        context.ViewModel.TaxSubject.TaxOfficeCode = "";

        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);

        Assert.Empty(context.Isds.SentMessages);
        Assert.Contains("není vybraný finanční úřad", context.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Delivery_Note_Missing_At_Send_Time_Is_Fetched_On_The_Next_Run()
    {
        var context = await ExportAsync();
        context.Isds.SignedDeliveryInfo = null; // ISDS ji hned po odeslání ještě nevydá

        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);

        Assert.Equal(2, context.Isds.SentMessages.Count);
        Assert.Contains("chybí doručenka", context.Period.Label);
        // Tlačítko zůstává aktivní, protože je co dotáhnout.
        Assert.True(context.ViewModel.CanSendToTaxOffice);

        context.Isds.SignedDeliveryInfo = [9, 9, 9];
        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);

        Assert.Equal(2, context.Isds.SentMessages.Count); // znovu se nic neodeslalo
        Assert.True(File.Exists(Path.Combine(context.Directory, "2026-05_DPHDP_podani_100001_dorucenka.zfo")));
        Assert.False(context.ViewModel.CanSendToTaxOffice);
        Assert.Contains("odesláno", context.Period.Label);
    }

    [Fact]
    public async Task Send_Button_Is_Rendered_In_The_Toolbar_And_Follows_The_Selected_Period()
    {
        using var session = Avalonia.Headless.HeadlessUnitTestSession.StartNew(typeof(FullAppTestBuilder));
        await session.Dispatch(async () =>
        {
            var context = await ExportAsync();
            var window = new MainWindow { DataContext = context.ViewModel };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var send = window.GetVisualDescendants().OfType<Button>()
                    .Single(x => ReferenceEquals(x.Command, context.ViewModel.SendToTaxOfficeCommand));
                Assert.Equal("Odeslat", send.Content);
                Assert.True(send.IsVisible);
                Assert.True(send.Bounds.Width > 0 && send.Bounds.Height > 0);
                Assert.NotNull(ToolTip.GetTip(send));

                // Sedí v horní liště vedle Export XML, ve stejné výšce a napravo od něj.
                var export = window.GetVisualDescendants().OfType<Button>()
                    .Single(x => ReferenceEquals(x.Command, context.ViewModel.ExportXmlCommand));
                Assert.Equal(export.TranslatePoint(default, window)!.Value.Y, send.TranslatePoint(default, window)!.Value.Y);
                Assert.True(send.TranslatePoint(default, window)!.Value.X > export.TranslatePoint(default, window)!.Value.X);

                // Období s neodeslaným XML jde odeslat.
                Assert.True(send.IsEnabled);

                // MainWindow si při navázání DataContextu přepsal háčky na skutečné dialogy –
                // pro test je musíme vrátit na falešné, jinak by se otevřelo modální okno.
                UseTestDialogs(context);
                await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(2, context.Isds.SentMessages.Count);
                Assert.False(send.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Lost_Response_Marks_The_Period_For_Verification_Instead_Of_Resending()
    {
        var context = await ExportAsync();
        // ISDS zprávu přijme, ale odpověď se ztratí – aplikace neví, jestli podání vzniklo.
        context.Isds.CreateMessageError = new IsdsException("Spojení s ISDS selhalo") { IsOutcomeUnknown = true };

        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);

        Assert.Empty(context.Isds.SentMessages);
        Assert.Contains("ověřit odeslání", context.Period.Label);
        Assert.DoesNotContain("k odeslání", context.Period.Label);
        Assert.Contains("nejisté odeslání", context.ViewModel.StatusMessage);
        Assert.True(context.ViewModel.CanSendToTaxOffice);
    }

    [Fact]
    public async Task Verification_Finds_The_Message_And_Does_Not_File_It_Twice()
    {
        var context = await ExportAsync();
        // Zpráva u úřadu vznikla, jen se o tom aplikace nedozvěděla.
        context.Isds.SentMessageLog.Add(new IsdsSentMessageInfo("100777", "", "7nyn2d9"));
        context.Isds.CreateMessageError = new IsdsException("Spojení selhalo") { IsOutcomeUnknown = true };
        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);
        Assert.Contains("ověřit odeslání", context.Period.Label);

        // Do seznamu odeslaných doplníme značky, se kterými se aplikace pokusila odeslat.
        var stuck = await context.Repository.LoadSubmissionsAsync(context.Period.Id);
        context.Isds.SentMessageLog.Clear();
        foreach (var submission in stuck)
        {
            context.Isds.SentMessageLog.Add(new IsdsSentMessageInfo($"9{submission.Id:D5}", submission.SendReference!, "7nyn2d9"));
        }

        context.Isds.CreateMessageError = null;
        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);

        // Nic se neodeslalo znovu – podání se jen spárovala s existujícími zprávami.
        Assert.Empty(context.Isds.SentMessages);
        Assert.Contains("odesláno", context.Period.Label);
        Assert.False(context.ViewModel.CanSendToTaxOffice);
        Assert.True((await context.Repository.LoadSubmissionsAsync(context.Period.Id)).All(x => x.IsSent));
    }

    [Fact]
    public async Task Verification_That_Finds_Nothing_Sends_The_Submission()
    {
        var context = await ExportAsync();
        context.Isds.CreateMessageError = new IsdsException("Spojení selhalo") { IsOutcomeUnknown = true };
        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);

        // V ISDS žádná zpráva se značkou není → pokus podání nevytvořil, pošle se normálně.
        context.Isds.CreateMessageError = null;
        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);

        Assert.Equal(2, context.Isds.SentMessages.Count);
        Assert.Contains("odesláno", context.Period.Label);
        Assert.False(context.ViewModel.CanSendToTaxOffice);
    }

    [Fact]
    public async Task Rejected_Password_While_Fetching_A_Delivery_Note_Is_Forgotten()
    {
        var context = await ExportAsync(new InMemoryCredentialStore(new IsdsCredentials("uzivatel", "heslo")));
        context.Isds.SignedDeliveryInfo = null; // doručenka po odeslání ještě není
        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);
        Assert.Contains("chybí doručenka", context.Period.Label);

        // Heslo se mezitím změnilo – dotažení doručenky ho musí zneplatnit, ne mlčky selhávat dokola.
        context.Isds.DeliveryInfoError = new IsdsException("Chybné heslo", "401") { IsAuthenticationFailure = true };
        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);

        Assert.Null(context.CredentialStore.Stored);
        Assert.Equal(1, context.CredentialStore.ClearCount);
        Assert.Contains("Odeslání selhalo", context.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Export_Is_Blocked_While_A_Send_Is_Waiting_For_Verification()
    {
        var context = await ExportAsync();
        context.Isds.CreateMessageError = new IsdsException("Spojení selhalo") { IsOutcomeUnknown = true };
        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);
        Assert.Contains("ověřit odeslání", context.Period.Label);

        var pickerCalled = false;
        context.ViewModel.PickExportDirectoryAsync = _ => { pickerCalled = true; return Task.FromResult<string?>(context.Directory); };
        await context.ViewModel.ExportXmlCommand.ExecuteAsync(null);

        // Dokud se neví, jestli předchozí podání odešlo, nedá se rozhodnout mezi řádným
        // a opravným – export se ani nezeptá na složku.
        Assert.False(pickerCalled);
        Assert.Contains("není jisté, zda se předchozí podání odeslalo", context.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Re_export_Over_An_Unresolved_Send_Still_Verifies_Before_Filing_Again()
    {
        var context = await ExportAsync();
        context.Isds.CreateMessageError = new IsdsException("Spojení selhalo") { IsOutcomeUnknown = true };
        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);

        // Zprávy u úřadu ve skutečnosti vznikly – odpověď se jen ztratila.
        var stuck = await context.Repository.LoadSubmissionsAsync(context.Period.Id);
        foreach (var submission in stuck)
        {
            context.Isds.SentMessageLog.Add(new IsdsSentMessageInfo($"9{submission.Id:D5}", submission.SendReference!, "7nyn2d9"));
        }

        // Re-export proběhne mimo UI (jako by ho udělala starší verze aplikace nebo jiná cesta)
        // – značka pro ověření musí i tak přežít.
        await context.Repository.RecordExportedSubmissionAsync(new EpoSubmission
        {
            PeriodId = context.Period.Id,
            DocumentKind = "DPHDP",
            FormType = "B",
            FilePath = stuck.Single(x => x.DocumentKind == "DPHDP").FilePath,
            ExportedAt = DateTimeOffset.UtcNow
        });

        context.Isds.CreateMessageError = null;
        await context.ViewModel.LoadCommand.ExecuteAsync(null);
        await WaitForAsync(() => context.ViewModel.StatusMessage == "Načteno.");
        UseTestDialogs(context);
        context.ViewModel.SelectedPeriod = context.ViewModel.Periods.Single(x => x.Month == 5);
        await context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);

        var final = await context.Repository.LoadSubmissionsAsync(context.Period.Id);
        // Původní nejistá podání se dohledala; znovu se odeslal jen nový export přiznání.
        Assert.All(final, x => Assert.True(x.IsSent));
        Assert.Single(context.Isds.SentMessages);
        Assert.Contains("DPHDP", context.Isds.SentMessages[0].Attachments[0].FileName);
    }

    [Fact]
    public async Task Export_And_Send_Cannot_Run_At_The_Same_Time()
    {
        var context = await ExportAsync();

        // Odesílání pozdržíme uprostřed a mezitím zkusíme exportovat.
        var sendReachedIsds = new TaskCompletionSource();
        var releaseSend = new TaskCompletionSource();
        context.Isds.BeforeCreateMessage = async () =>
        {
            sendReachedIsds.TrySetResult();
            await releaseSend.Task;
        };

        var send = context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);
        await sendReachedIsds.Task;

        var pickerCalled = false;
        context.ViewModel.PickExportDirectoryAsync = _ => { pickerCalled = true; return Task.FromResult<string?>(context.Directory); };
        await context.ViewModel.ExportXmlCommand.ExecuteAsync(null);

        // Export nesmí sáhnout na záznamy, které právě odcházejí – smazal by řádek odeslané zprávy.
        Assert.False(pickerCalled);
        Assert.Contains("probíhá odesílání", context.ViewModel.StatusMessage);

        releaseSend.SetResult();
        await send;

        // Odeslání proběhlo v pořádku a je zaevidované.
        Assert.Equal(2, context.Isds.SentMessages.Count);
        Assert.True((await context.Repository.LoadSubmissionsAsync(context.Period.Id)).All(x => x.IsSent));
    }

    [Fact]
    public async Task Export_Is_Blocked_Already_While_Credentials_Are_Being_Verified()
    {
        // Mezi načtením seznamu podání a odesláním se čeká na ověření přihlášení. I tohle okno
        // musí být pro export zavřené, jinak by odešly načtené záznamy a nové zůstaly k odeslání.
        var context = await ExportAsync();
        var reachedVerification = new TaskCompletionSource();
        var releaseVerification = new TaskCompletionSource();
        context.Isds.BeforeGetOwner = async () =>
        {
            reachedVerification.TrySetResult();
            await releaseVerification.Task;
        };

        var send = context.ViewModel.SendToTaxOfficeCommand.ExecuteAsync(null);
        await reachedVerification.Task;

        var pickerCalled = false;
        context.ViewModel.PickExportDirectoryAsync = _ => { pickerCalled = true; return Task.FromResult<string?>(context.Directory); };
        await context.ViewModel.ExportXmlCommand.ExecuteAsync(null);

        Assert.False(pickerCalled);
        Assert.Contains("probíhá odesílání", context.ViewModel.StatusMessage);

        releaseVerification.SetResult();
        await send;

        // Odešly právě ty záznamy, které se načetly – žádné nezůstaly viset jako neodeslané.
        Assert.Equal(2, context.Isds.SentMessages.Count);
        var stored = await context.Repository.LoadSubmissionsAsync(context.Period.Id);
        Assert.Equal(2, stored.Count);
        Assert.All(stored, x => Assert.True(x.IsSent));
        Assert.False(context.ViewModel.CanSendToTaxOffice);
    }

    [Fact]
    public async Task Send_Is_Disabled_While_An_Export_Runs()
    {
        var context = await ExportAsync();
        var exportReachedPicker = new TaskCompletionSource();
        var releaseExport = new TaskCompletionSource<string?>();
        context.ViewModel.PickExportDirectoryAsync = _ =>
        {
            exportReachedPicker.TrySetResult();
            return releaseExport.Task;
        };

        var export = context.ViewModel.ExportXmlCommand.ExecuteAsync(null);
        await exportReachedPicker.Task;

        Assert.False(context.ViewModel.CanSendToTaxOffice);
        Assert.False(context.ViewModel.SendToTaxOfficeCommand.CanExecute(null));

        releaseExport.SetResult(null); // export zrušen výběrem složky
        await export;

        Assert.True(context.ViewModel.CanSendToTaxOffice);
    }

    [Fact]
    public async Task Period_Without_Recorded_Exports_Cannot_Be_Sent()
    {
        // Exporty z dřívějších verzí aplikace se neevidují – tlačítko na nich zůstává neaktivní.
        var repository = await CreateRepositoryAsync();
        var period = await SeedPeriodAsync(repository);
        await repository.MarkPeriodExportedAsync(period.Id, DateTimeOffset.UtcNow);
        var viewModel = CreateViewModel(repository, new FakeIsdsClient(), new InMemoryCredentialStore());
        await WaitForAsync(() => viewModel.StatusMessage == "Načteno.");

        Assert.False(viewModel.CanSendToTaxOffice);
        Assert.DoesNotContain("k odeslání", viewModel.SelectedPeriod!.Label);
    }

    // Dialogy nahrazené pro test; MainWindow je po navázání DataContextu přepisuje skutečnými.
    private static void UseTestDialogs(Context context)
    {
        context.ViewModel.PickExportDirectoryAsync = _ => Task.FromResult<string?>(context.Directory);
        context.ViewModel.ConfirmAsync = (_, _) => Task.FromResult(true);
        context.ViewModel.RequestIsdsCredentialsAsync = (_, _, _) => Task.FromResult<IsdsCredentials?>(new IsdsCredentials("uzivatel", "heslo"));
        context.ViewModel.ShowReportAsync = (_, _) => Task.CompletedTask;
    }

    private sealed record Context(
        MainWindowViewModel ViewModel,
        DphRepository Repository,
        FakeIsdsClient Isds,
        InMemoryCredentialStore CredentialStore,
        VatPeriod Period,
        string Directory);

    // Založí období s jedním dokladem, vyexportuje XML a nastaví dialogy tak, aby odeslání prošlo.
    private static async Task<Context> ExportAsync(InMemoryCredentialStore? credentialStore = null)
    {
        var repository = await CreateRepositoryAsync();
        var period = await SeedPeriodAsync(repository);
        await SeedLineAsync(repository, period);

        var isds = new FakeIsdsClient();
        var store = credentialStore ?? new InMemoryCredentialStore();
        var viewModel = CreateViewModel(repository, isds, store);
        await WaitForAsync(() => viewModel.StatusMessage == "Načteno.");
        await WaitForAsync(() => viewModel.Invoices.Count == 1);

        var directory = Path.Combine(Path.GetTempPath(), $"dph-send-vm-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(directory);
        viewModel.PickExportDirectoryAsync = _ => Task.FromResult<string?>(directory);
        viewModel.ConfirmAsync = (_, _) => Task.FromResult(true);
        viewModel.RequestIsdsCredentialsAsync = (_, _, _) => Task.FromResult<IsdsCredentials?>(new IsdsCredentials("uzivatel", "heslo"));
        viewModel.ShowReportAsync = (_, _) => Task.CompletedTask;

        Assert.False(viewModel.CanSendToTaxOffice);
        await viewModel.ExportXmlCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(directory, "2026-05_DPHDP_podani.xml")), viewModel.StatusMessage);

        return new Context(viewModel, repository, isds, store, viewModel.SelectedPeriod!, directory);
    }

    private static async Task ExportSecondPeriodAsync(Context context)
    {
        var period = await SeedPeriodAsync(context.Repository, 6);
        await SeedLineAsync(context.Repository, period);
        await context.ViewModel.LoadCommand.ExecuteAsync(null);
        await WaitForAsync(() => context.ViewModel.StatusMessage == "Načteno.");
        context.ViewModel.PickExportDirectoryAsync = _ => Task.FromResult<string?>(context.Directory);
        context.ViewModel.ConfirmAsync = (_, _) => Task.FromResult(true);
        context.ViewModel.SelectedPeriod = context.ViewModel.Periods.Single(x => x.Month == 6);
        await WaitForAsync(() => context.ViewModel.Invoices.Count == 1);
        await context.ViewModel.ExportXmlCommand.ExecuteAsync(null);
    }

    private static MainWindowViewModel CreateViewModel(DphRepository repository, FakeIsdsClient isds, InMemoryCredentialStore store)
    {
        var viewModel = new MainWindowViewModel(
            repository, new NoAresClient(), new NoExchangeRateProvider(), new NoTaxOfficeCatalog(), isds, store);
        return viewModel;
    }

    private static async Task<DphRepository> CreateRepositoryAsync()
    {
        var repository = new DphRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite"));
        await repository.InitializeAsync();
        await repository.SaveTaxSubjectAsync(new TaxSubject
        {
            DisplayName = "Bohdan Koudelka",
            Dic = "CZ7001010000",
            Ico = "12345678",
            FirstName = "Bohdan",
            LastName = "Koudelka",
            Street = "Krátká",
            HouseNumber = "1",
            City = "Praha",
            PostalCode = "11000",
            TaxOfficeCode = "451",
            WorkplaceCode = "2001",
            DataBoxId = "abc1234"
        });
        return repository;
    }

    private static async Task<VatPeriod> SeedPeriodAsync(DphRepository repository, int month = 5)
    {
        var period = new VatPeriod { Year = 2026, Month = month, SubmissionDate = new DateOnly(2026, month, 1), FormType = "B" };
        await repository.SavePeriodAsync(period);
        return period;
    }

    private static Task<long> SeedLineAsync(DphRepository repository, VatPeriod period)
        => repository.SaveInvoiceAsync(new InvoiceLine
        {
            PeriodId = period.Id,
            Kind = InvoiceKind.ReceivedDomesticWithVat,
            CounterpartyName = "Dodavatel s.r.o.",
            CounterpartyDic = "CZ27082440",
            EvidenceNumber = $"F{period.Month}",
            TaxableSupplyDate = new DateOnly(period.Year, period.Month, 15),
            TaxBaseCzk = 1000m,
            VatCzk = 210m,
            Currency = "CZK",
            VatRate = VatRateKind.Standard21
        });

    // VM pouští načítání jako fire-and-forget úlohy, takže se na výsledek čeká pollingem.
    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (condition())
                {
                    return;
                }
            }
            catch
            {
                // kolekce se právě vyměňuje – zkusíme znovu
            }

            await Task.Delay(25);
        }

        Assert.Fail("Vypršel čas při čekání na stav VM.");
    }

    private sealed class NoAresClient : IAresClient
    {
        public Task<AresSubject?> LookupByIcoAsync(string ico, CancellationToken cancellationToken = default)
            => Task.FromResult<AresSubject?>(null);

        public Task<AresSubject?> LookupByDicAsync(string dic, CancellationToken cancellationToken = default)
            => Task.FromResult<AresSubject?>(null);

        public Task<AresSubjectDetail?> LookupDetailByIcoAsync(string ico, CancellationToken cancellationToken = default)
            => Task.FromResult<AresSubjectDetail?>(null);
    }

    private sealed class NoExchangeRateProvider : IExchangeRateProvider
    {
        public Task<ExchangeRate?> GetRateAsync(string currencyCode, DateOnly date, CancellationToken cancellationToken = default)
            => Task.FromResult<ExchangeRate?>(null);
    }

    private sealed class NoTaxOfficeCatalog : ITaxOfficeCatalog
    {
        public Task<TaxOfficeCatalogData?> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<TaxOfficeCatalogData?>(null);
    }
}
