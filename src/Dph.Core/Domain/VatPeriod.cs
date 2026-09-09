using System.ComponentModel;

namespace Dph.Core.Domain;

public sealed class VatPeriod : INotifyPropertyChanged
{
    private DateTimeOffset? _importedAt;
    private DateTimeOffset? _exportedAt;
    private DateTimeOffset? _changedAt;
    private int _pendingSubmissionCount;
    private int _sentSubmissionCount;
    private int _incompleteSubmissionCount;
    private int _unknownSubmissionCount;

    public long Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly SubmissionDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    // Vyplňuje se při exportu opravy; není to datum vytvoření XML.
    public DateOnly? DiscoveryDate { get; set; }
    public string FormType { get; set; } = "B";

    public DateTimeOffset? ImportedAt
    {
        get => _importedAt;
        set { _importedAt = value; RaiseStateChanged(); }
    }

    public DateTimeOffset? ExportedAt
    {
        get => _exportedAt;
        set { _exportedAt = value; RaiseStateChanged(); }
    }

    // Nastaveno, když se už podané (importované/exportované) období po podání upraví. Vynuluje se
    // při dalším exportu (snímek odpovídá aktuálnímu stavu).
    public DateTimeOffset? ChangedAt
    {
        get => _changedAt;
        set { _changedAt = value; RaiseStateChanged(); }
    }

    // Počet exportovaných XML období, která ještě neodešla datovou schránkou.
    public int PendingSubmissionCount
    {
        get => _pendingSubmissionCount;
        set { _pendingSubmissionCount = value; RaiseStateChanged(); }
    }

    // Počet XML odeslaných datovou schránkou.
    public int SentSubmissionCount
    {
        get => _sentSubmissionCount;
        set { _sentSubmissionCount = value; RaiseStateChanged(); }
    }

    // Odeslaná podání, u kterých ještě chybí ZFO odeslané zprávy nebo doručenka.
    public int IncompleteSubmissionCount
    {
        get => _incompleteSubmissionCount;
        set { _incompleteSubmissionCount = value; RaiseStateChanged(); }
    }

    // Podání, u kterých se ztratila odpověď ISDS – nejdřív se musí ověřit, zda přece jen odešla.
    public int UnknownSubmissionCount
    {
        get => _unknownSubmissionCount;
        set { _unknownSubmissionCount = value; RaiseStateChanged(); }
    }

    // Tlačítko Odeslat má co dělat: poslat neodeslané XML, ověřit nejistý pokus, nebo dotáhnout ZFO.
    public bool CanSubmit => PendingSubmissionCount > 0 || IncompleteSubmissionCount > 0 || UnknownSubmissionCount > 0;

    public string Label
    {
        get
        {
            var flags = new List<string>();
            if (ImportedAt is not null)
            {
                flags.Add("import");
            }

            if (ExportedAt is not null)
            {
                flags.Add("export");
            }

            if (ChangedAt is not null)
            {
                flags.Add("změna");
            }

            if (UnknownSubmissionCount > 0)
            {
                flags.Add("ověřit odeslání");
            }
            else if (PendingSubmissionCount > 0)
            {
                flags.Add("k odeslání");
            }
            else if (IncompleteSubmissionCount > 0)
            {
                flags.Add("chybí doručenka");
            }
            else if (SentSubmissionCount > 0)
            {
                flags.Add("odesláno");
            }

            return flags.Count == 0 ? $"{Year:D4}-{Month:D2}" : $"{Year:D4}-{Month:D2} ({string.Join(", ", flags)})";
        }
    }

    // Období už figuruje v podání u úřadu – ať z importu, nebo z našeho exportu.
    public bool IsLockedByHistory => ImportedAt is not null || ExportedAt is not null;

    public bool HasPendingChanges => ChangedAt is not null;

    // Lidsky čitelný stav pro banner v přiznání.
    public string LockReason
    {
        get
        {
            if (ChangedAt is not null)
            {
                return "Po exportu nebo importu bylo přiznání změněno.";
            }

            var flags = new List<string>();
            if (ImportedAt is not null)
            {
                flags.Add("importované");
            }

            if (ExportedAt is not null)
            {
                flags.Add("podané (exportované)");
            }

            return flags.Count == 0 ? "" : $"Období je uzamčené – {string.Join(" a ", flags)}.";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLockedByHistory)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPendingChanges)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSubmit)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LockReason)));
    }
}
