using System.Globalization;
using Dph.Core.Domain;
using Dph.Core.Persistence;

namespace Dph.Core.Isds;

public sealed record EpoSendReport(
    int Sent,
    int Failed,
    int ArtifactsCompleted,
    /// <summary>Podání, u kterých není jisté, zda odešla – čekají na ověření v ISDS.</summary>
    int Unresolved,
    /// <summary>Podání odložená proto, že starší pokus s týmž souborem zůstal neověřený.</summary>
    int Blocked,
    IReadOnlyList<string> Messages);

/// <summary>
/// Odešle vyexportovaná XML příslušnému finančnímu úřadu datovou schránkou a stáhne k nim ZFO
/// odeslané zprávy a doručenku. Každé podání (přiznání, kontrolní hlášení) jde jako samostatná
/// datová zpráva – tak je EPO/ADIS zpracovává a každé má vlastní doručenku.
/// </summary>
public sealed class EpoSubmissionService(IIsdsClient client, DphRepository repository)
{
    public async Task<EpoSendReport> SendAsync(
        IsdsCredentials credentials,
        TaxSubject subject,
        VatPeriod period,
        IReadOnlyList<EpoSubmission> submissions,
        string recipientDataBoxId,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        var sent = 0;
        var failed = 0;
        var completed = 0;
        var unresolved = 0;
        var blocked = 0;

        // Soubory, u kterých visí nevyřešený pokus o odeslání. Dokud se nevyjasní, jestli podání
        // u úřadu vzniklo, nesmí se odeslat novější export téhož souboru – byl by to duplikát.
        var awaitingVerification = submissions
            .Where(x => x.IsSendOutcomeUnknown)
            .Select(x => x.FilePath)
            .ToHashSet(StringComparer.Ordinal);

        // Nejistá podání se řeší první, ať se jejich soubor stihne odblokovat pro novější export
        // ještě v tomhle běhu. OrderBy je stabilní, takže zbytek si drží pořadí od volajícího.
        foreach (var submission in submissions.OrderBy(x => x.IsSendOutcomeUnknown ? 0 : 1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!submission.IsSent)
            {
                // Předchozí pokus skončil bez jasného výsledku – zpráva mohla vzniknout. Než se
                // odešle znovu, musí se dohledat v ISDS, jinak by u úřadu leželo podání dvakrát.
                if (submission.IsSendOutcomeUnknown)
                {
                    var reconciled = await ReconcileAsync(credentials, submission, messages, cancellationToken);
                    if (reconciled is null && submission.IsSendOutcomeUnknown)
                    {
                        // Ověřit se nepodařilo – soubor zůstává blokovaný i pro novější export.
                        unresolved++;
                        continue;
                    }

                    // Vyřešeno (odesláno, nevytvořeno, nebo záznam překonaný a zahozený) –
                    // novější export téhož souboru se teď smí poslat.
                    awaitingVerification.Remove(submission.FilePath);

                    if (reconciled is null)
                    {
                        continue;
                    }

                    if (reconciled.Value)
                    {
                        sent++;
                    }
                }
                else if (awaitingVerification.Contains(submission.FilePath))
                {
                    blocked++;
                    messages.Add($"{submission.FileName}: neodesláno – u dřívějšího podání z téhož souboru se nepodařilo ověřit, jestli u úřadu je. Zkuste to znovu, až bude spojení v pořádku.");
                    continue;
                }

                if (!submission.IsSent)
                {
                    if (!File.Exists(submission.FilePath))
                    {
                        failed++;
                        messages.Add($"{submission.FileName}: soubor už na disku není, podání se neodeslalo.");
                        continue;
                    }

                    byte[] content;
                    try
                    {
                        content = await File.ReadAllBytesAsync(submission.FilePath, cancellationToken);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // Nečitelné XML zastaví jen tohle podání, ostatní se pošlou.
                        failed++;
                        messages.Add($"{submission.FileName}: soubor se nepodařilo přečíst: {exception.Message}");
                        continue;
                    }

                    // Značka se zapíše ještě před voláním ISDS. Když se odpověď ztratí, zůstane
                    // v evidenci stopa, podle které jde zprávu dohledat.
                    var reference = NewSendReference(submission);
                    var attemptedAt = DateTimeOffset.UtcNow;
                    await repository.MarkSubmissionAttemptedAsync(submission.Id, reference, attemptedAt, cancellationToken);
                    submission.SendReference = reference;
                    submission.SendAttemptedAt = attemptedAt;

                    try
                    {
                        var result = await client.CreateMessageAsync(
                            credentials,
                            recipientDataBoxId,
                            Annotation(submission, subject, period),
                            reference,
                            [new IsdsAttachment(submission.FileName, content)],
                            cancellationToken);

                        // Stav se ukládá dřív, než se sahá po ZFO – další pokus už podání znovu nepošle.
                        await MarkSentAsync(submission, result.MessageId, recipientDataBoxId, cancellationToken);
                        sent++;
                        messages.Add($"{submission.FileName}: odesláno, ID zprávy {result.MessageId}.");
                    }
                    catch (IsdsException exception)
                    {
                        if (exception.IsOutcomeUnknown)
                        {
                            // Značka zůstává – příští spuštění nejdřív ověří, jestli zpráva vznikla.
                            unresolved++;
                            messages.Add($"{submission.FileName}: {exception.Message} Není jisté, zda podání odešlo – při dalším pokusu se nejdřív ověří v datové schránce.");
                            continue;
                        }

                        // ISDS požadavek odmítl, zpráva nevznikla – značku zahodíme, ať jde poslat znovu.
                        await repository.ClearSubmissionAttemptAsync(submission.Id, cancellationToken);
                        submission.SendAttemptedAt = null;
                        failed++;
                        messages.Add($"{submission.FileName}: {exception.Message}");
                        // Chybná autentizace se projeví u všech dalších stejně – nemá smysl opakovat
                        // a riskovat zablokování účtu.
                        if (exception.IsAuthenticationFailure)
                        {
                            throw;
                        }

                        continue;
                    }
                }
            }

            if (await TryCompleteArtifactsAsync(credentials, submission, messages, cancellationToken))
            {
                completed++;
            }
        }

        return new EpoSendReport(sent, failed, completed, unresolved, blocked, messages);
    }

    /// <summary>
    /// Dohledá v ISDS zprávu z nejistého pokusu podle značky odesílatele.
    /// Vrací <c>true</c>, když se zpráva našla a podání se označilo za odeslané, <c>false</c>, když
    /// prokazatelně nevznikla (smí se odeslat znovu), a <c>null</c>, když se ověřit nepodařilo –
    /// pak se podání v tomto běhu přeskočí, aby nevzniklo duplicitní podání.
    /// </summary>
    private async Task<bool?> ReconcileAsync(
        IsdsCredentials credentials,
        EpoSubmission submission,
        List<string> messages,
        CancellationToken cancellationToken)
    {
        if (submission.SendReference is not { Length: > 0 } reference || submission.SendAttemptedAt is not { } attemptedAt)
        {
            // Pokus bez značky (řádek z verze, která ji ještě neevidovala) neumíme ověřit.
            messages.Add($"{submission.FileName}: dřívější pokus o odeslání nemá značku pro ověření. Zkontrolujte odeslané zprávy v datové schránce ručně.");
            return null;
        }

        IReadOnlyList<IsdsSentMessageInfo> recent;
        try
        {
            // Úzké okno kolem pokusu: zpráva mohla vzniknout jen během něj, takže se nemusí
            // procházet celá historie odeslaných zpráv. Rezerva pokrývá rozdíl hodin klienta
            // a serveru i zpoždění na straně ISDS.
            recent = await client.GetSentMessagesAsync(
                credentials,
                attemptedAt.AddMinutes(-15),
                attemptedAt.AddHours(2),
                cancellationToken);
        }
        catch (IsdsException exception)
        {
            if (exception.IsAuthenticationFailure)
            {
                throw;
            }

            messages.Add($"{submission.FileName}: nepodařilo se ověřit, zda dřívější pokus o odeslání prošel ({exception.Message}). Podání se pro jistotu neodeslalo znovu.");
            return null;
        }

        var match = recent.FirstOrDefault(x => x.SenderReference == reference);
        if (match is null)
        {
            // Zpráva se značkou v ISDS není – pokus tedy nic nevytvořil. Pokud mezitím vznikl
            // novější export téhož souboru, je tenhle záznam překonaný (jeho XML se přepsalo)
            // a zahodí se; jinak se prostě odešle znovu.
            if (await repository.DeleteSupersededSubmissionAttemptAsync(submission.Id, cancellationToken))
            {
                // Záznam je pryč – už není nejistý ani k odeslání, pošle se novější export.
                submission.SendAttemptedAt = null;
                messages.Add($"{submission.FileName}: dřívější nejistý pokus podání nevytvořil; soubor byl mezitím znovu vyexportován, odešle se jeho nová verze.");
                return null;
            }

            await repository.ClearSubmissionAttemptAsync(submission.Id, cancellationToken);
            submission.SendAttemptedAt = null;
            messages.Add($"{submission.FileName}: dřívější nejistý pokus podání nevytvořil, odesílá se znovu.");
            return false;
        }

        await MarkSentAsync(submission, match.MessageId, match.RecipientDataBoxId, cancellationToken);
        messages.Add($"{submission.FileName}: dřívější pokus přece jen prošel – dohledáno v datové schránce, ID zprávy {match.MessageId}. Podruhé se neodesílá.");
        return true;
    }

    private async Task MarkSentAsync(EpoSubmission submission, string messageId, string recipientDataBoxId, CancellationToken cancellationToken)
    {
        var sentAt = DateTimeOffset.UtcNow;
        submission.SentAt = sentAt;
        submission.MessageId = messageId;
        submission.RecipientDataBoxId = recipientDataBoxId;
        submission.SendAttemptedAt = null;

        if (!await repository.MarkSubmissionSentAsync(submission.Id, sentAt, messageId, recipientDataBoxId, cancellationToken))
        {
            // Řádek mezitím zmizel (souběžný export). Zpráva ale odešla, takže se doplní zpátky –
            // jinak by o podání nebyl záznam a šlo by k úřadu podruhé.
            await repository.RecordSentSubmissionAsync(submission, cancellationToken);
        }
    }

    // Značka musí být jednoznačná i mezi opakovanými exporty téhož souboru, jinak by dohledání
    // spárovalo starou zprávu s novým podáním. dmSenderRefNumber pojme 50 znaků.
    private static string NewSendReference(EpoSubmission submission)
    {
        var reference = $"DPH-{submission.Id}-{Guid.NewGuid():N}";
        return reference.Length <= 50 ? reference : reference[..50];
    }

    /// <summary>Dotáhne ZFO odeslané zprávy a doručenku k už odeslanému podání.</summary>
    private async Task<bool> TryCompleteArtifactsAsync(
        IsdsCredentials credentials,
        EpoSubmission submission,
        List<string> messages,
        CancellationToken cancellationToken)
    {
        if (submission.MessageId is not { Length: > 0 } messageId || !submission.HasMissingArtifacts)
        {
            return false;
        }

        var directory = Path.GetDirectoryName(submission.FilePath);
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        // ID zprávy v názvu drží ZFO od sebe, když se stejný soubor exportuje a odesílá znovu
        // (řádné podání přepisuje XML) – jinak by nový důkaz přepsal ten k dřívějšímu podání.
        var baseName = $"{Path.GetFileNameWithoutExtension(submission.FilePath)}_{SafeForFileName(messageId)}";
        string? messageZfo = null;
        string? deliveryZfo = null;
        DateTimeOffset? deliveryFetchedAt = null;

        if (submission.MessageZfoPath is null)
        {
            messageZfo = await TryFetchAsync(
                () => client.DownloadSignedSentMessageAsync(credentials, messageId, cancellationToken),
                Path.Combine(directory, $"{baseName}_zprava.zfo"),
                $"{submission.FileName}: ZFO odeslané zprávy se nepodařilo stáhnout",
                messages,
                cancellationToken);
        }

        if (submission.DeliveryZfoPath is null)
        {
            deliveryZfo = await TryFetchAsync(
                () => client.DownloadSignedDeliveryInfoAsync(credentials, messageId, cancellationToken),
                Path.Combine(directory, $"{baseName}_dorucenka.zfo"),
                $"{submission.FileName}: doručenku se nepodařilo stáhnout",
                messages,
                cancellationToken);
            deliveryFetchedAt = deliveryZfo is null ? null : DateTimeOffset.UtcNow;
        }

        if (messageZfo is null && deliveryZfo is null)
        {
            return false;
        }

        await repository.SaveSubmissionArtifactsAsync(submission.Id, messageZfo, deliveryZfo, deliveryFetchedAt, cancellationToken);
        submission.MessageZfoPath ??= messageZfo;
        submission.DeliveryZfoPath ??= deliveryZfo;
        submission.DeliveryFetchedAt ??= deliveryFetchedAt;
        return true;
    }

    // dmID je číslo, ale do názvu souboru se nesmí dostat nic, co by cestu rozbilo.
    private static string SafeForFileName(string value)
        => new(value.Where(x => char.IsLetterOrDigit(x) || x is '-' or '_').ToArray());

    // Chybějící ZFO nebo doručenka není důvod hlásit odeslání jako neúspěšné – zpráva odešla
    // a doručenka bývá k dispozici až za okamžik. Doplní se dalším stiskem tlačítka Odeslat.
    // Vrací cestu k uloženému souboru, nebo null, když se stáhnout ani uložit nepodařilo.
    private static async Task<string?> TryFetchAsync(
        Func<Task<byte[]?>> download,
        string targetPath,
        string failureMessage,
        List<string> messages,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await download();
            if (content is null)
            {
                messages.Add($"{failureMessage} (ISDS ji zatím nevrací).");
                return null;
            }

            await File.WriteAllBytesAsync(targetPath, content, cancellationToken);
            return targetPath;
        }
        catch (IsdsException exception) when (exception.IsAuthenticationFailure)
        {
            // Neplatné heslo musí propadnout k volajícímu, aby se uložené údaje zahodily. Jinak by
            // se stahování doručenek opakovalo se stále stejným odmítnutým heslem.
            throw;
        }
        catch (Exception exception) when (exception is IsdsException or IOException or UnauthorizedAccessException)
        {
            messages.Add($"{failureMessage}: {exception.Message}");
            return null;
        }
    }

    // Věc datové zprávy. Finanční úřad podle ní pozná podání i bez otevření přílohy.
    public static string Annotation(EpoSubmission submission, TaxSubject subject, VatPeriod period)
    {
        var document = submission.DocumentKind == "DPHDP"
            ? $"{Capitalize(submission.FormTitle)} přiznání k DPH"
            : $"{Capitalize(submission.FormTitle)} kontrolní hlášení DPH";
        var dic = string.IsNullOrWhiteSpace(subject.Dic) ? "" : $", DIČ {subject.Dic.Trim()}";
        return $"{document} za {period.Month.ToString("D2", CultureInfo.InvariantCulture)}/{period.Year}{dic}";
    }

    private static string Capitalize(string value)
        => value.Length == 0 ? value : char.ToUpper(value[0], new CultureInfo("cs-CZ")) + value[1..];
}
