namespace Dph.Core.Domain;

public sealed class VatPeriod
{
    public long Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly SubmissionDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string FormType { get; set; } = "B";

    public string Label => $"{Year:D4}-{Month:D2}";
}
