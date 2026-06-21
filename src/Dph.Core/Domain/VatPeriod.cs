namespace Dph.Core.Domain;

public sealed class VatPeriod
{
    public long Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly SubmissionDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string FormType { get; set; } = "B";
    public DateTimeOffset? ImportedAt { get; set; }
    public DateTimeOffset? ExportedAt { get; set; }

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

            return flags.Count == 0 ? $"{Year:D4}-{Month:D2}" : $"{Year:D4}-{Month:D2} ({string.Join(", ", flags)})";
        }
    }

    public bool IsLockedByHistory => ImportedAt is not null || ExportedAt is not null;
}
