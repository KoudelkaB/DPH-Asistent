namespace Dph.Core.Domain;

public sealed class Counterparty
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Ico { get; set; }
    public string? Dic { get; set; }
    public string CountryCode { get; set; } = "CZ";
    public CounterpartyRole Role { get; set; } = CounterpartyRole.Supplier;
    public DateTimeOffset? AresUpdatedAt { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Dic ?? Ico ?? "(bez názvu)" : Name;
}
