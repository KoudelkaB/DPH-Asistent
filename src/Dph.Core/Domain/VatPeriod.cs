using System.ComponentModel;

namespace Dph.Core.Domain;

public sealed class VatPeriod : INotifyPropertyChanged
{
    private DateTimeOffset? _importedAt;
    private DateTimeOffset? _exportedAt;
    private DateTimeOffset? _changedAt;

    public long Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly SubmissionDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
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

            return flags.Count == 0 ? $"{Year:D4}-{Month:D2}" : $"{Year:D4}-{Month:D2} ({string.Join(", ", flags)})";
        }
    }

    // Období už figuruje v podání u úřadu – ať z importu, nebo z našeho exportu.
    public bool IsLockedByHistory => ImportedAt is not null || ExportedAt is not null;

    public bool HasPendingChanges => ChangedAt is not null;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLockedByHistory)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPendingChanges)));
    }
}
