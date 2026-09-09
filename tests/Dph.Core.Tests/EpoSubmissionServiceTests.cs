using System.Linq;
using System.Text;
using Dph.Core.Domain;
using Dph.Core.Isds;
using Dph.Core.Persistence;

namespace Dph.Core.Tests;

public sealed class EpoSubmissionServiceTests
{
    private static readonly IsdsCredentials Credentials = new("uzivatel", "heslo");

    [Fact]
    public async Task Sends_each_xml_as_its_own_message_and_stores_zfo_next_to_it()
    {
        var context = await SetupAsync();
        var client = new RecordingIsdsClient();
        var service = new EpoSubmissionService(client, context.Repository);

        var report = await service.SendAsync(Credentials, context.Subject, context.Period, context.Submissions, "7nyn2d9");

        Assert.Equal(2, report.Sent);
        Assert.Equal(0, report.Failed);
        Assert.Equal(2, report.ArtifactsCompleted);
        Assert.Equal(2, client.Sent.Count);
        Assert.All(client.Sent, x =>
        {
            Assert.Equal("7nyn2d9", x.Recipient);
            Assert.Single(x.Attachments);
        });

        // Věc rozliší přiznání od kontrolního hlášení a nese období i DIČ.
        Assert.Equal("Řádné přiznání k DPH za 06/2026, DIČ CZ7001010000", client.Sent[0].Annotation);
        Assert.Equal("Řádné kontrolní hlášení DPH za 06/2026, DIČ CZ7001010000", client.Sent[1].Annotation);
        Assert.Equal("<DPHDP3/>", Encoding.UTF8.GetString(client.Sent[0].Attachments[0].Content));

        var stored = await context.Repository.LoadSubmissionsAsync(context.Period.Id);
        Assert.All(stored, x =>
        {
            Assert.True(x.IsSent);
            Assert.Equal("7nyn2d9", x.RecipientDataBoxId);
            Assert.False(x.HasMissingArtifacts);
            Assert.True(File.Exists(x.MessageZfoPath));
            Assert.True(File.Exists(x.DeliveryZfoPath));
            Assert.NotNull(x.DeliveryFetchedAt);
        });

        // ID zprávy je součástí názvu, aby opakovaný export nepřepsal ZFO dřívějšího podání.
        var vatReturn = stored.Single(x => x.DocumentKind == "DPHDP");
        Assert.Equal("10001", vatReturn.MessageId);
        Assert.Equal(Path.Combine(context.Directory, "2026-06_DPHDP_podani_10001_zprava.zfo"), vatReturn.MessageZfoPath);
        Assert.Equal(Path.Combine(context.Directory, "2026-06_DPHDP_podani_10001_dorucenka.zfo"), vatReturn.DeliveryZfoPath);
        Assert.Equal("ZFO"u8.ToArray(), await File.ReadAllBytesAsync(vatReturn.MessageZfoPath!));
        Assert.Equal("DOR"u8.ToArray(), await File.ReadAllBytesAsync(vatReturn.DeliveryZfoPath!));
    }

    [Fact]
    public async Task Already_sent_submission_is_never_sent_again()
    {
        var context = await SetupAsync();
        var client = new RecordingIsdsClient();
        var service = new EpoSubmissionService(client, context.Repository);
        await service.SendAsync(Credentials, context.Subject, context.Period, context.Submissions, "7nyn2d9");

        var reloaded = await context.Repository.LoadSubmissionsAsync(context.Period.Id);
        var report = await service.SendAsync(Credentials, context.Subject, context.Period, reloaded, "7nyn2d9");

        Assert.Equal(0, report.Sent);
        Assert.Equal(2, client.Sent.Count);
    }

    [Fact]
    public async Task Send_is_recorded_even_when_the_zfo_download_fails()
    {
        var context = await SetupAsync();
        var client = new RecordingIsdsClient { SignedSentError = new IsdsException("Zpráva ještě není k dispozici", "1219") };
        var service = new EpoSubmissionService(client, context.Repository);

        var report = await service.SendAsync(Credentials, context.Subject, context.Period, context.Submissions, "7nyn2d9");

        Assert.Equal(2, report.Sent);
        Assert.Equal(0, report.Failed);
        var stored = await context.Repository.LoadSubmissionsAsync(context.Period.Id);
        Assert.All(stored, x =>
        {
            Assert.True(x.IsSent);
            Assert.Null(x.MessageZfoPath);
            Assert.NotNull(x.DeliveryZfoPath);
            // Podání zbývá dokončit, ale znovu odeslat ho nesmíme.
            Assert.True(x.HasMissingArtifacts);
        });
        Assert.Contains(report.Messages, x => x.Contains("ZFO odeslané zprávy"));
    }

    [Fact]
    public async Task Missing_artifacts_are_completed_on_a_later_run_without_resending()
    {
        var context = await SetupAsync();
        var client = new RecordingIsdsClient { SignedSentError = new IsdsException("zatím ne") };
        var service = new EpoSubmissionService(client, context.Repository);
        await service.SendAsync(Credentials, context.Subject, context.Period, context.Submissions, "7nyn2d9");

        client.SignedSentError = null;
        var pending = await context.Repository.LoadSubmissionsAsync(context.Period.Id);
        var report = await service.SendAsync(Credentials, context.Subject, context.Period, pending, "7nyn2d9");

        Assert.Equal(0, report.Sent);
        Assert.Equal(2, report.ArtifactsCompleted);
        Assert.Equal(2, client.Sent.Count);
        var stored = await context.Repository.LoadSubmissionsAsync(context.Period.Id);
        Assert.All(stored, x => Assert.False(x.HasMissingArtifacts));
        // Už stažená doručenka se nestahuje podruhé.
        Assert.Equal(2, client.DeliveryDownloads.Count);
    }

    [Fact]
    public async Task A_failing_submission_does_not_stop_the_rest()
    {
        var context = await SetupAsync();
        var client = new RecordingIsdsClient { FailFirstCreate = new IsdsException("Neplatné XML", "1214") };
        var service = new EpoSubmissionService(client, context.Repository);

        var report = await service.SendAsync(Credentials, context.Subject, context.Period, context.Submissions, "7nyn2d9");

        Assert.Equal(1, report.Sent);
        Assert.Equal(1, report.Failed);
        var stored = await context.Repository.LoadSubmissionsAsync(context.Period.Id);
        Assert.False(stored.Single(x => x.DocumentKind == "DPHDP").IsSent);
        Assert.True(stored.Single(x => x.DocumentKind == "DPHKH").IsSent);
    }

    [Fact]
    public async Task Authentication_failure_stops_the_run_before_the_next_attempt()
    {
        var context = await SetupAsync();
        var client = new RecordingIsdsClient
        {
            FailFirstCreate = new IsdsException("Neplatné heslo", "401") { IsAuthenticationFailure = true }
        };
        var service = new EpoSubmissionService(client, context.Repository);

        await Assert.ThrowsAsync<IsdsException>(() =>
            service.SendAsync(Credentials, context.Subject, context.Period, context.Submissions, "7nyn2d9"));

        Assert.Empty(client.Sent);
        Assert.All(await context.Repository.LoadSubmissionsAsync(context.Period.Id), x => Assert.False(x.IsSent));
    }

    [Fact]
    public async Task Missing_xml_file_is_reported_and_not_sent()
    {
        var context = await SetupAsync();
        File.Delete(context.Submissions[0].FilePath);
        var client = new RecordingIsdsClient();
        var service = new EpoSubmissionService(client, context.Repository);

        var report = await service.SendAsync(Credentials, context.Subject, context.Period, context.Submissions, "7nyn2d9");

        Assert.Equal(1, report.Sent);
        Assert.Equal(1, report.Failed);
        Assert.Contains(report.Messages, x => x.Contains("soubor už na disku není"));
    }

    // ─── Ztracená odpověď na CreateMessage nesmí vést k duplicitnímu podání ───

    [Fact]
    public async Task Lost_response_leaves_the_submission_unresolved_instead_of_pending()
    {
        var context = await SetupAsync();
        var client = new RecordingIsdsClient
        {
            FailFirstCreate = new IsdsException("Spojení s ISDS selhalo", inner: new HttpRequestException()) { IsOutcomeUnknown = true }
        };
        var service = new EpoSubmissionService(client, context.Repository);

        var report = await service.SendAsync(Credentials, context.Subject, context.Period, context.Submissions, "7nyn2d9");

        Assert.Equal(1, report.Sent);
        Assert.Equal(1, report.Unresolved);
        Assert.Equal(0, report.Failed);

        var stuck = (await context.Repository.LoadSubmissionsAsync(context.Period.Id)).Single(x => x.DocumentKind == "DPHDP");
        Assert.False(stuck.IsSent);
        Assert.True(stuck.IsSendOutcomeUnknown);
        Assert.NotNull(stuck.SendReference);
        Assert.Contains(report.Messages, x => x.Contains("Není jisté, zda podání odešlo"));
        // V evidenci nesmí zůstat jako „prostě neodeslané“, jinak by ho další běh poslal naslepo.
        Assert.Equal(new SubmissionState(0, 1, 0, 1), (await context.Repository.LoadSubmissionStatesAsync())[context.Period.Id]);
    }

    [Fact]
    public async Task Submission_that_actually_went_through_is_reconciled_and_not_sent_twice()
    {
        var context = await SetupAsync();
        // ISDS zprávu přijalo, ale odpověď se ztratila cestou zpět.
        var client = new LostResponseIsdsClient();
        var service = new EpoSubmissionService(client, context.Repository);
        await service.SendAsync(Credentials, context.Subject, context.Period, context.Submissions, "7nyn2d9");

        var stuck = (await context.Repository.LoadSubmissionsAsync(context.Period.Id)).Single(x => x.DocumentKind == "DPHDP");
        Assert.True(stuck.IsSendOutcomeUnknown);

        // Další pokus zprávu dohledá podle značky a podání označí za odeslané – neposílá znovu.
        client.LoseNextResponse = false;
        var reloaded = await context.Repository.LoadSubmissionsAsync(context.Period.Id);
        var report = await service.SendAsync(Credentials, context.Subject, context.Period, reloaded, "7nyn2d9");

        Assert.Equal(1, report.Sent);
        Assert.Equal(0, report.Unresolved);
        // Zpráva do ISDS šla jen jednou, i když aplikace o výsledku prvního pokusu nevěděla.
        Assert.Single(client.Sent.Where(x => x.Attachments[0].FileName.Contains("DPHDP")));
        Assert.Contains(report.Messages, x => x.Contains("dřívější pokus přece jen prošel"));

        var resolved = (await context.Repository.LoadSubmissionsAsync(context.Period.Id)).Single(x => x.DocumentKind == "DPHDP");
        Assert.True(resolved.IsSent);
        Assert.False(resolved.IsSendOutcomeUnknown);
        Assert.Equal(client.Sent[0].Reference, stuck.SendReference);
    }

    [Fact]
    public async Task Submission_that_did_not_go_through_is_sent_on_the_next_run()
    {
        var context = await SetupAsync();
        var client = new RecordingIsdsClient
        {
            FailFirstCreate = new IsdsException("Spojení selhalo") { IsOutcomeUnknown = true }
        };
        var service = new EpoSubmissionService(client, context.Repository);
        await service.SendAsync(Credentials, context.Subject, context.Period, context.Submissions, "7nyn2d9");

        // V ISDS žádná zpráva se značkou není → pokus podání nevytvořil, smí se poslat znovu.
        var reloaded = await context.Repository.LoadSubmissionsAsync(context.Period.Id);
        var report = await service.SendAsync(Credentials, context.Subject, context.Period, reloaded, "7nyn2d9");

        Assert.Equal(1, report.Sent);
        Assert.Equal(0, report.Unresolved);
        Assert.Contains(report.Messages, x => x.Contains("nevytvořil, odesílá se znovu"));
        Assert.True((await context.Repository.LoadSubmissionsAsync(context.Period.Id)).All(x => x.IsSent));
    }

    [Fact]
    public async Task Submission_stays_unresolved_when_the_check_itself_fails()
    {
        var context = await SetupAsync();
        var client = new RecordingIsdsClient
        {
            FailFirstCreate = new IsdsException("Spojení selhalo") { IsOutcomeUnknown = true }
        };
        var service = new EpoSubmissionService(client, context.Repository);
        await service.SendAsync(Credentials, context.Subject, context.Period, context.Submissions, "7nyn2d9");

        // Síť je pořád rozbitá – ověřit to nejde, takže se radši neodesílá.
        client.SentMessageListError = new IsdsException("Spojení selhalo") { IsOutcomeUnknown = true };
        var reloaded = await context.Repository.LoadSubmissionsAsync(context.Period.Id);
        var before = client.Sent.Count;
        var report = await service.SendAsync(Credentials, context.Subject, context.Period, reloaded, "7nyn2d9");

        Assert.Equal(0, report.Sent);
        Assert.Equal(1, report.Unresolved);
        Assert.Equal(before, client.Sent.Count);
        Assert.Contains(report.Messages, x => x.Contains("neodeslalo znovu"));
        Assert.True((await context.Repository.LoadSubmissionsAsync(context.Period.Id))
            .Single(x => x.DocumentKind == "DPHDP").IsSendOutcomeUnknown);
    }

    [Fact]
    public async Task Rejected_request_stays_retryable_because_no_message_was_created()
    {
        var context = await SetupAsync();
        // Stavový kód ISDS je jednoznačný – zpráva nevznikla, značka se musí zahodit.
        var client = new RecordingIsdsClient { FailFirstCreate = new IsdsException("Neplatné XML", "1214") };
        var service = new EpoSubmissionService(client, context.Repository);
        await service.SendAsync(Credentials, context.Subject, context.Period, context.Submissions, "7nyn2d9");

        var rejected = (await context.Repository.LoadSubmissionsAsync(context.Period.Id)).Single(x => x.DocumentKind == "DPHDP");
        Assert.False(rejected.IsSendOutcomeUnknown);
        Assert.Equal(0, client.SentMessageListCalls); // nic se nedohledávalo, nebylo co
    }

    [Fact]
    public async Task Newer_export_waits_while_the_older_attempt_cannot_be_verified()
    {
        var context = await SetupAsync();
        var vatReturn = context.Submissions.Single(x => x.DocumentKind == "DPHDP");

        // Starší pokus skončil nejistě…
        var client = new RecordingIsdsClient { FailFirstCreate = new IsdsException("Spojení selhalo") { IsOutcomeUnknown = true } };
        var service = new EpoSubmissionService(client, context.Repository);
        await service.SendAsync(Credentials, context.Subject, context.Period, [vatReturn], "7nyn2d9");

        // …a mezitím vznikl novější export téhož souboru.
        var reExported = new EpoSubmission
        {
            PeriodId = context.Period.Id,
            DocumentKind = "DPHDP",
            FormType = "B",
            FilePath = vatReturn.FilePath,
            ExportedAt = DateTimeOffset.UtcNow
        };
        await context.Repository.RecordExportedSubmissionAsync(reExported);

        // Ověření pořád nejde – novější export se nesmí odeslat, jinak by šlo podání dvakrát.
        client.SentMessageListError = new IsdsException("Spojení selhalo") { IsOutcomeUnknown = true };
        var vatReturnRows = (await context.Repository.LoadSubmissionsAsync(context.Period.Id))
            .Where(x => x.DocumentKind == "DPHDP")
            .ToArray();
        Assert.Equal(2, vatReturnRows.Length); // nejistý pokus + novější export téhož souboru

        var report = await service.SendAsync(Credentials, context.Subject, context.Period, vatReturnRows, "7nyn2d9");

        Assert.Equal(0, report.Sent);
        Assert.Equal(1, report.Unresolved);
        Assert.Equal(1, report.Blocked);
        Assert.Empty(client.Sent);
        Assert.Contains(report.Messages, x => x.Contains("nepodařilo ověřit"));
        Assert.True((await context.Repository.LoadSubmissionsAsync(context.Period.Id)).All(x => !x.IsSent));
    }

    [Fact]
    public async Task Newer_export_goes_out_once_the_older_attempt_is_resolved_in_the_same_run()
    {
        var context = await SetupAsync();
        var vatReturn = context.Submissions.Single(x => x.DocumentKind == "DPHDP");
        var client = new RecordingIsdsClient { FailFirstCreate = new IsdsException("Spojení selhalo") { IsOutcomeUnknown = true } };
        var service = new EpoSubmissionService(client, context.Repository);
        await service.SendAsync(Credentials, context.Subject, context.Period, [vatReturn], "7nyn2d9");

        var reExported = new EpoSubmission
        {
            PeriodId = context.Period.Id,
            DocumentKind = "DPHDP",
            FormType = "B",
            FilePath = vatReturn.FilePath,
            ExportedAt = DateTimeOffset.UtcNow
        };
        await context.Repository.RecordExportedSubmissionAsync(reExported);

        // Ověření projde a zjistí, že starý pokus nic nevytvořil → je překonaný a zahodí se,
        // takže novější export se ve stejném běhu odešle.
        var stored = await context.Repository.LoadSubmissionsAsync(context.Period.Id);
        var report = await service.SendAsync(Credentials, context.Subject, context.Period, stored, "7nyn2d9");

        Assert.Equal(0, report.Blocked);
        Assert.Contains(client.Sent, x => x.Attachments[0].FileName.Contains("DPHDP"));
        var final = await context.Repository.LoadSubmissionsAsync(context.Period.Id);
        Assert.Equal([reExported.Id], final.Where(x => x.DocumentKind == "DPHDP").Select(x => x.Id));
        Assert.True(final.Single(x => x.DocumentKind == "DPHDP").IsSent);
    }

    [Fact]
    public async Task Blocking_applies_only_to_the_file_that_is_waiting()
    {
        var context = await SetupAsync();
        var vatReturn = context.Submissions.Single(x => x.DocumentKind == "DPHDP");
        var client = new RecordingIsdsClient { FailFirstCreate = new IsdsException("Spojení selhalo") { IsOutcomeUnknown = true } };
        var service = new EpoSubmissionService(client, context.Repository);
        await service.SendAsync(Credentials, context.Subject, context.Period, [vatReturn], "7nyn2d9");

        client.SentMessageListError = new IsdsException("Spojení selhalo") { IsOutcomeUnknown = true };
        var report = await service.SendAsync(
            Credentials, context.Subject, context.Period,
            await context.Repository.LoadSubmissionsAsync(context.Period.Id),
            "7nyn2d9");

        // Kontrolní hlášení je jiný dokument – nejistota u přiznání ho nemá zdržovat.
        Assert.Equal(1, report.Sent);
        Assert.Equal(0, report.Blocked);
        Assert.Contains(client.Sent, x => x.Attachments[0].FileName.Contains("DPHKH"));
    }

    [Fact]
    public async Task A_sent_message_is_recorded_even_if_its_row_vanished_mid_send()
    {
        var context = await SetupAsync();
        var vatReturn = context.Submissions.Single(x => x.DocumentKind == "DPHDP");

        // Souběžný export řádek smaže dřív, než se stihne zapsat výsledek odeslání.
        var client = new VanishingRowIsdsClient(Path.Combine(context.Directory, "dph.sqlite"), vatReturn.Id);
        var service = new EpoSubmissionService(client, context.Repository);

        var report = await service.SendAsync(Credentials, context.Subject, context.Period, [vatReturn], "7nyn2d9");

        Assert.Equal(1, report.Sent);
        // Zpráva odešla, takže o ní musí zůstat záznam – jinak by se podání dalo podat znovu.
        var recorded = (await context.Repository.LoadSubmissionsAsync(context.Period.Id))
            .Single(x => x.DocumentKind == "DPHDP");
        Assert.True(recorded.IsSent);
        Assert.Equal(client.Sent[0].Reference, recorded.SendReference);
        Assert.Equal(client.Sent[0].Recipient, recorded.RecipientDataBoxId);
        Assert.Equal(vatReturn.FilePath, recorded.FilePath);
        Assert.Equal("B", recorded.FormType);
    }

    // ─── Opakovaný export nesmí přepsat ZFO dřívějšího podání ───

    [Fact]
    public async Task Re_exported_submission_keeps_the_earlier_zfo_files()
    {
        var context = await SetupAsync();
        var client = new RecordingIsdsClient();
        var service = new EpoSubmissionService(client, context.Repository);
        await service.SendAsync(Credentials, context.Subject, context.Period, context.Submissions, "7nyn2d9");
        var first = (await context.Repository.LoadSubmissionsAsync(context.Period.Id)).Single(x => x.DocumentKind == "DPHDP");

        // Řádný re-export přepíše XML a založí nové podání téhož souboru.
        var reExported = new EpoSubmission
        {
            PeriodId = context.Period.Id,
            DocumentKind = "DPHDP",
            FormType = "B",
            FilePath = first.FilePath,
            ExportedAt = DateTimeOffset.UtcNow
        };
        await context.Repository.RecordExportedSubmissionAsync(reExported);
        await service.SendAsync(Credentials, context.Subject, context.Period, [reExported], "7nyn2d9");

        var second = (await context.Repository.LoadSubmissionsAsync(context.Period.Id))
            .Last(x => x.DocumentKind == "DPHDP");
        Assert.NotEqual(first.MessageId, second.MessageId);
        Assert.NotEqual(first.MessageZfoPath, second.MessageZfoPath);
        Assert.NotEqual(first.DeliveryZfoPath, second.DeliveryZfoPath);
        // Důkaz o prvním podání musí na disku zůstat.
        Assert.True(File.Exists(first.MessageZfoPath));
        Assert.True(File.Exists(first.DeliveryZfoPath));
        Assert.True(File.Exists(second.MessageZfoPath));
    }

    // ─── Neplatné heslo musí probublat i při pouhém stahování doručenek ───

    [Fact]
    public async Task Authentication_failure_while_fetching_a_delivery_note_is_not_swallowed()
    {
        var context = await SetupAsync();
        var client = new RecordingIsdsClient { SignedSentError = new IsdsException("zatím ne") };
        var service = new EpoSubmissionService(client, context.Repository);
        await service.SendAsync(Credentials, context.Subject, context.Period, context.Submissions, "7nyn2d9");

        // Heslo se mezitím změnilo; při dotahování ZFO se to musí projevit, ne jen zapsat do hlášení.
        client.SignedSentError = new IsdsException("Chybné heslo", "401") { IsAuthenticationFailure = true };
        var reloaded = await context.Repository.LoadSubmissionsAsync(context.Period.Id);

        var exception = await Assert.ThrowsAsync<IsdsException>(() =>
            service.SendAsync(Credentials, context.Subject, context.Period, reloaded, "7nyn2d9"));

        Assert.True(exception.IsAuthenticationFailure);
    }

    [Theory]
    [InlineData("DPHDP", "O", "Opravné přiznání k DPH za 06/2026, DIČ CZ7001010000")]
    [InlineData("DPHDP", "D", "Dodatečné přiznání k DPH za 06/2026, DIČ CZ7001010000")]
    [InlineData("DPHKH", "N", "Následné kontrolní hlášení DPH za 06/2026, DIČ CZ7001010000")]
    public void Annotation_names_the_form(string documentKind, string formType, string expected)
    {
        var annotation = EpoSubmissionService.Annotation(
            new EpoSubmission { DocumentKind = documentKind, FormType = formType },
            new TaxSubject { Dic = "CZ7001010000" },
            new VatPeriod { Year = 2026, Month = 6 });

        Assert.Equal(expected, annotation);
    }

    private sealed record Context(
        DphRepository Repository,
        TaxSubject Subject,
        VatPeriod Period,
        string Directory,
        IReadOnlyList<EpoSubmission> Submissions);

    private static async Task<Context> SetupAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dph-send-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(root);
        var repository = new DphRepository(Path.Combine(root, "dph.sqlite"));
        await repository.InitializeAsync();

        var period = new VatPeriod { Year = 2026, Month = 6 };
        await repository.SavePeriodAsync(period);

        var exportedAt = DateTimeOffset.UtcNow;
        var submissions = new List<EpoSubmission>();
        foreach (var (kind, content) in new[] { ("DPHDP", "<DPHDP3/>"), ("DPHKH", "<DPHKH1/>") })
        {
            var path = Path.Combine(root, $"2026-06_{kind}_podani.xml");
            await File.WriteAllTextAsync(path, content);
            var submission = new EpoSubmission
            {
                PeriodId = period.Id,
                DocumentKind = kind,
                FormType = "B",
                FilePath = path,
                ExportedAt = exportedAt
            };
            await repository.RecordExportedSubmissionAsync(submission);
            submissions.Add(submission);
        }

        return new Context(repository, new TaxSubject { Dic = "CZ7001010000" }, period, root, submissions);
    }

    // Simuluje souběžný export: řádek podání zmizí přesně mezi odesláním a zápisem výsledku.
    private sealed class VanishingRowIsdsClient(string databasePath, long rowToDelete) : RecordingIsdsClient
    {
        public override async Task<IsdsSentMessage> CreateMessageAsync(
            IsdsCredentials credentials,
            string recipientDataBoxId,
            string annotation,
            string senderReference,
            IReadOnlyList<IsdsAttachment> attachments,
            CancellationToken cancellationToken = default)
        {
            var result = await base.CreateMessageAsync(credentials, recipientDataBoxId, annotation, senderReference, attachments, cancellationToken);

            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"delete from epo_submissions where id = {rowToDelete}";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return result;
        }
    }

    // ISDS zprávu přijme, ale odpověď se ztratí – přesně ta situace, kdy hrozí duplicitní podání.
    private sealed class LostResponseIsdsClient : RecordingIsdsClient
    {
        public bool LoseNextResponse { get; set; } = true;

        public override async Task<IsdsSentMessage> CreateMessageAsync(
            IsdsCredentials credentials,
            string recipientDataBoxId,
            string annotation,
            string senderReference,
            IReadOnlyList<IsdsAttachment> attachments,
            CancellationToken cancellationToken = default)
        {
            var result = await base.CreateMessageAsync(credentials, recipientDataBoxId, annotation, senderReference, attachments, cancellationToken);
            if (!LoseNextResponse)
            {
                return result;
            }

            LoseNextResponse = false;
            throw new IsdsException("Spojení s ISDS selhalo") { IsOutcomeUnknown = true };
        }
    }

    private class RecordingIsdsClient : IIsdsClient
    {
        public List<(string Recipient, string Annotation, string Reference, IReadOnlyList<IsdsAttachment> Attachments)> Sent { get; } = [];
        public List<string> DeliveryDownloads { get; } = [];
        public Exception? FailFirstCreate { get; set; }
        public Exception? SignedSentError { get; set; }
        // Co ISDS vrátí v seznamu odeslaných zpráv – tudy se simuluje ztracená odpověď.
        public List<IsdsSentMessageInfo> SentMessageLog { get; } = [];
        public Exception? SentMessageListError { get; set; }
        public int SentMessageListCalls { get; private set; }

        public Task<IsdsOwner> GetOwnerAsync(IsdsCredentials credentials, CancellationToken cancellationToken = default)
            => Task.FromResult(new IsdsOwner("abc1234", "Poplatník", "FO"));

        public virtual Task<IsdsSentMessage> CreateMessageAsync(
            IsdsCredentials credentials,
            string recipientDataBoxId,
            string annotation,
            string senderReference,
            IReadOnlyList<IsdsAttachment> attachments,
            CancellationToken cancellationToken = default)
        {
            if (FailFirstCreate is not null)
            {
                var error = FailFirstCreate;
                FailFirstCreate = null;
                return Task.FromException<IsdsSentMessage>(error);
            }

            Sent.Add((recipientDataBoxId, annotation, senderReference, attachments));
            var messageId = $"1000{Sent.Count}";
            SentMessageLog.Add(new IsdsSentMessageInfo(messageId, senderReference, recipientDataBoxId));
            return Task.FromResult(new IsdsSentMessage(messageId, "OK"));
        }

        public Task<IReadOnlyList<IsdsSentMessageInfo>> GetSentMessagesAsync(
            IsdsCredentials credentials,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default)
        {
            SentMessageListCalls++;
            return SentMessageListError is null
                ? Task.FromResult<IReadOnlyList<IsdsSentMessageInfo>>(SentMessageLog.ToArray())
                : Task.FromException<IReadOnlyList<IsdsSentMessageInfo>>(SentMessageListError);
        }

        public Task<byte[]?> DownloadSignedSentMessageAsync(IsdsCredentials credentials, string messageId, CancellationToken cancellationToken = default)
            => SignedSentError is null
                ? Task.FromResult<byte[]?>("ZFO"u8.ToArray())
                : Task.FromException<byte[]?>(SignedSentError);

        public Task<byte[]?> DownloadSignedDeliveryInfoAsync(IsdsCredentials credentials, string messageId, CancellationToken cancellationToken = default)
        {
            DeliveryDownloads.Add(messageId);
            return Task.FromResult<byte[]?>("DOR"u8.ToArray());
        }
    }
}
