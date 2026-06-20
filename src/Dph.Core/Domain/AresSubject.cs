namespace Dph.Core.Domain;

public sealed record AresSubject(
    string Ico,
    string OfficialName,
    string? Dic,
    DateOnly? UpdatedOn);

public sealed record ExchangeRate(
    DateOnly Date,
    string CurrencyCode,
    int Amount,
    decimal RateCzk)
{
    public decimal RatePerUnit => RateCzk / Amount;
}
