using System.Linq;
using Dph.Core.Domain;
using Dph.Core.Persistence;

namespace Dph.Core.Tests;

public sealed class EpoSubmissionRepositoryTests
{
    [Fact]
    public async Task Re_export_replaces_an_unsent_record_but_keeps_the_sent_history()
    {
        var repository = await NewRepositoryAsync();
        var period = await SeedPeriodAsync(repository);
        const string path = "/exports/2026-06_DPHDP_podani.xml";

        var first = await RecordAsync(repository, period.Id, "DPHDP", "B", path);
        await repository.MarkSubmissionSentAsync(first, DateTimeOffset.UtcNow, "111", "7nyn2d9");

        // Řádný re-export přepsal soubor: dřívější odeslání zůstává, nový záznam čeká na odeslání.
        await RecordAsync(repository, period.Id, "DPHDP", "B", path);
        // A ještě jeden export, než se stihlo odeslat – nesmí zůstat dva čekající záznamy.
        var third = await RecordAsync(repository, period.Id, "DPHDP", "B", path);

        var stored = await repository.LoadSubmissionsAsync(period.Id);
        Assert.Equal([first, third], stored.Select(x => x.Id));
        Assert.Equal("111", stored[0].MessageId);
        Assert.True(stored[0].IsSent);
        Assert.False(stored[1].IsSent);
        // Odeslané podání v tomto testu nemá stažené ZFO, takže je zároveň „nedokončené“.
        Assert.Equal(new SubmissionState(1, 1, 1, 0), (await repository.LoadSubmissionStatesAsync())[period.Id]);
    }

    [Fact]
    public async Task Re_export_keeps_an_unresolved_attempt_so_it_can_still_be_verified()
    {
        var repository = await NewRepositoryAsync();
        var period = await SeedPeriodAsync(repository);
        const string path = "/exports/2026-06_DPHDP_podani.xml";

        var attempted = await RecordAsync(repository, period.Id, "DPHDP", "B", path);
        await repository.MarkSubmissionAttemptedAsync(attempted, "DPH-1-ref", DateTimeOffset.UtcNow);

        var reExported = await RecordAsync(repository, period.Id, "DPHDP", "B", path);

        // Značka nejistého pokusu musí přežít – bez ní by nešlo ověřit, jestli podání u úřadu je.
        var stored = await repository.LoadSubmissionsAsync(period.Id);
        Assert.Equal([attempted, reExported], stored.Select(x => x.Id));
        Assert.Equal("DPH-1-ref", stored[0].SendReference);
        Assert.True(stored[0].IsSendOutcomeUnknown);
        Assert.False(stored[1].IsSendOutcomeUnknown);
        Assert.Equal(new SubmissionState(1, 0, 0, 1), (await repository.LoadSubmissionStatesAsync())[period.Id]);
    }

    [Fact]
    public async Task Superseded_attempt_is_dropped_only_when_a_newer_export_waits_for_the_same_file()
    {
        var repository = await NewRepositoryAsync();
        var period = await SeedPeriodAsync(repository);
        const string path = "/exports/dp.xml";

        // Bez novějšího exportu se nemaže nic – záznam se má poslat znovu, ne zmizet.
        var lonely = await RecordAsync(repository, period.Id, "DPHDP", "B", path);
        await repository.MarkSubmissionAttemptedAsync(lonely, "ref-1", DateTimeOffset.UtcNow);
        Assert.False(await repository.DeleteSupersededSubmissionAttemptAsync(lonely));
        Assert.Single(await repository.LoadSubmissionsAsync(period.Id));

        // S novějším exportem téhož souboru je starý pokus překonaný.
        var newer = await RecordAsync(repository, period.Id, "DPHDP", "B", path);
        Assert.True(await repository.DeleteSupersededSubmissionAttemptAsync(lonely));
        Assert.Equal([newer], (await repository.LoadSubmissionsAsync(period.Id)).Select(x => x.Id));
    }

    [Fact]
    public async Task Superseded_delete_never_touches_an_already_sent_submission()
    {
        var repository = await NewRepositoryAsync();
        var period = await SeedPeriodAsync(repository);
        const string path = "/exports/dp.xml";
        var sent = await RecordAsync(repository, period.Id, "DPHDP", "B", path);
        await repository.MarkSubmissionSentAsync(sent, DateTimeOffset.UtcNow, "111", "7nyn2d9");
        await RecordAsync(repository, period.Id, "DPHDP", "B", path);

        Assert.False(await repository.DeleteSupersededSubmissionAttemptAsync(sent));
        Assert.Contains(await repository.LoadSubmissionsAsync(period.Id), x => x.Id == sent && x.IsSent);
    }

    [Fact]
    public async Task Submission_states_group_pending_sent_and_incomplete_by_period()
    {
        var repository = await NewRepositoryAsync();
        var june = await SeedPeriodAsync(repository, 6);
        var july = await SeedPeriodAsync(repository, 7);

        var sentComplete = await RecordAsync(repository, june.Id, "DPHDP", "B", "/exports/june-dp.xml");
        var sentIncomplete = await RecordAsync(repository, june.Id, "DPHKH", "B", "/exports/june-kh.xml");
        await RecordAsync(repository, july.Id, "DPHDP", "B", "/exports/july-dp.xml");

        await repository.MarkSubmissionSentAsync(sentComplete, DateTimeOffset.UtcNow, "1", "7nyn2d9");
        await repository.SaveSubmissionArtifactsAsync(sentComplete, "/exports/june-dp_zprava.zfo", "/exports/june-dp_dorucenka.zfo", DateTimeOffset.UtcNow);
        await repository.MarkSubmissionSentAsync(sentIncomplete, DateTimeOffset.UtcNow, "2", "7nyn2d9");
        await repository.SaveSubmissionArtifactsAsync(sentIncomplete, "/exports/june-kh_zprava.zfo", null, null);

        var states = await repository.LoadSubmissionStatesAsync();

        Assert.Equal(new SubmissionState(0, 2, 1, 0), states[june.Id]);
        Assert.Equal(new SubmissionState(1, 0, 0, 0), states[july.Id]);
    }

    [Fact]
    public async Task Artifact_paths_are_filled_in_without_clearing_the_ones_already_there()
    {
        var repository = await NewRepositoryAsync();
        var period = await SeedPeriodAsync(repository);
        var id = await RecordAsync(repository, period.Id, "DPHDP", "B", "/exports/dp.xml");
        await repository.MarkSubmissionSentAsync(id, DateTimeOffset.UtcNow, "1", "7nyn2d9");
        await repository.SaveSubmissionArtifactsAsync(id, "/exports/dp_zprava.zfo", null, null);

        var fetchedAt = DateTimeOffset.UtcNow;
        await repository.SaveSubmissionArtifactsAsync(id, null, "/exports/dp_dorucenka.zfo", fetchedAt);

        var stored = (await repository.LoadSubmissionsAsync(period.Id)).Single();
        Assert.Equal("/exports/dp_zprava.zfo", stored.MessageZfoPath);
        Assert.Equal("/exports/dp_dorucenka.zfo", stored.DeliveryZfoPath);
        Assert.Equal(fetchedAt, stored.DeliveryFetchedAt!.Value, TimeSpan.FromSeconds(1));
        Assert.False(stored.HasMissingArtifacts);
    }

    [Fact]
    public async Task Deleting_a_period_removes_its_submissions()
    {
        var repository = await NewRepositoryAsync();
        var period = await SeedPeriodAsync(repository);
        await RecordAsync(repository, period.Id, "DPHDP", "B", "/exports/dp.xml");

        await repository.DeletePeriodAsync(period.Id);

        Assert.Empty(await repository.LoadSubmissionsAsync(period.Id));
        Assert.Empty(await repository.LoadSubmissionStatesAsync());
    }

    [Fact]
    public async Task Existing_databases_start_with_no_submissions_recorded()
    {
        // Exporty z dřívějších verzí se zpětně nedoplňují – neví se, jestli už byly podané jinak.
        var repository = await NewRepositoryAsync();
        var period = await SeedPeriodAsync(repository);
        await repository.MarkPeriodExportedAsync(period.Id, DateTimeOffset.UtcNow);

        Assert.Empty(await repository.LoadSubmissionStatesAsync());
    }

    [Fact]
    public async Task Upgrades_A_Schema_3_Database_Without_Losing_Recorded_Submissions()
    {
        // DB ve schématu 3: epo_submissions existuje, ale ještě bez značky a času pokusu.
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        var repository = new DphRepository(path);
        await repository.InitializeAsync();
        var period = new VatPeriod { Year = 2026, Month = 6 };
        await repository.SavePeriodAsync(period);
        var id = await RecordAsync(repository, period.Id, "DPHDP", "B", "/exports/dp.xml");
        await repository.MarkSubmissionSentAsync(id, DateTimeOffset.UtcNow, "555", "7nyn2d9");

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                alter table epo_submissions drop column send_reference;
                alter table epo_submissions drop column send_attempted_at;
                pragma user_version = 3;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await repository.InitializeAsync();
        await repository.InitializeAsync();

        var stored = (await repository.LoadSubmissionsAsync(period.Id)).Single();
        Assert.True(stored.IsSent);
        Assert.Equal("555", stored.MessageId);
        // Starší řádky značku nemají – nejistý stav se u nich nemohl zaznamenat.
        Assert.Null(stored.SendReference);
        Assert.False(stored.IsSendOutcomeUnknown);

        // A nová evidence pokusu na migrované DB funguje.
        await repository.MarkSubmissionAttemptedAsync(id, "DPH-1-ref", DateTimeOffset.UtcNow);
        Assert.Equal("DPH-1-ref", (await repository.LoadSubmissionsAsync(period.Id)).Single().SendReference);
    }

    private static async Task<DphRepository> NewRepositoryAsync()
    {
        var repository = new DphRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite"));
        await repository.InitializeAsync();
        return repository;
    }

    private static async Task<VatPeriod> SeedPeriodAsync(DphRepository repository, int month = 6)
    {
        var period = new VatPeriod { Year = 2026, Month = month };
        await repository.SavePeriodAsync(period);
        return period;
    }

    private static Task<long> RecordAsync(DphRepository repository, long periodId, string kind, string form, string path)
        => repository.RecordExportedSubmissionAsync(new EpoSubmission
        {
            PeriodId = periodId,
            DocumentKind = kind,
            FormType = form,
            FilePath = path,
            ExportedAt = DateTimeOffset.UtcNow
        });
}
