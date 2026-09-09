namespace Dph.Core.Domain;

/// <summary>
/// Jedno exportované XML podání a jeho stav odeslání datovou schránkou. Řádek vzniká při exportu,
/// takže „vyexportováno, ale neodesláno“ je evidovaný stav, ne odhad ze souborů na disku.
/// Přiznání a kontrolní hlášení jsou dvě samostatná podání – každé má vlastní řádek, vlastní
/// datovou zprávu i vlastní doručenku.
/// </summary>
public sealed class EpoSubmission
{
    public long Id { get; set; }
    public long PeriodId { get; set; }

    /// <summary>DPHDP = přiznání k DPH, DPHKH = kontrolní hlášení.</summary>
    public string DocumentKind { get; set; } = "";

    /// <summary>Forma podání v okamžiku exportu: B řádné, O opravné, D dodatečné, N následné.</summary>
    public string FormType { get; set; } = "B";

    public string FilePath { get; set; } = "";
    public DateTimeOffset ExportedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? MessageId { get; set; }
    public string? RecipientDataBoxId { get; set; }
    /// <summary>Jednoznačná značka pokusu o odeslání (dmSenderRefNumber) pro dohledání zprávy v ISDS.</summary>
    public string? SendReference { get; set; }

    /// <summary>
    /// Kdy začal poslední pokus o odeslání. Zůstane vyplněné jen tehdy, když pokus skončil bez
    /// jasného výsledku – pak se před dalším odesláním musí zpráva nejdřív dohledat v ISDS.
    /// </summary>
    public DateTimeOffset? SendAttemptedAt { get; set; }

    public string? MessageZfoPath { get; set; }
    public string? DeliveryZfoPath { get; set; }

    /// <summary>Kdy se podařilo stáhnout doručenku – ne čas doručení podle ISDS.</summary>
    public DateTimeOffset? DeliveryFetchedAt { get; set; }

    public string FileName => Path.GetFileName(FilePath);

    public bool IsSent => SentAt is not null;

    /// <summary>
    /// Pokus o odeslání skončil nejednoznačně – podání mohlo u úřadu vzniknout. Odeslat znovu se
    /// smí až po ověření v ISDS, jinak by vzniklo duplicitní podání.
    /// </summary>
    public bool IsSendOutcomeUnknown => !IsSent && SendAttemptedAt is not null;

    /// <summary>Odesláno, ale ZFO zprávy nebo doručenka se ještě nestáhly – dá se doplnit později.</summary>
    public bool HasMissingArtifacts => IsSent && (MessageZfoPath is null || DeliveryZfoPath is null);

    public string DocumentTitle => DocumentKind switch
    {
        "DPHDP" => "přiznání k DPH",
        "DPHKH" => "kontrolní hlášení",
        _ => DocumentKind
    };

    public string FormTitle => FormType switch
    {
        "B" => "řádné",
        "O" => "opravné",
        "D" => "dodatečné",
        "N" => "následné",
        _ => FormType
    };
}
