namespace Dph.Core.Domain;

public sealed class Counterparty
{
    public long Id { get; set; }
    public string CustomName { get; set; } = "";
    public string? OfficialName { get; set; }
    public string? Ico { get; set; }
    public string? Dic { get; set; }
    public string CountryCode { get; set; } = "CZ";
    public CounterpartyRole Role { get; set; } = CounterpartyRole.Supplier;
    public DateTimeOffset? AresUpdatedAt { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(CustomName)
        ? OfficialName ?? Dic ?? Ico ?? "(bez názvu)"
        : CustomName;
}
