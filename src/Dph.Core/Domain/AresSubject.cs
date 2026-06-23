namespace Dph.Core.Domain;

public sealed record AresSubject(
    string Ico,
    string OfficialName,
    string? Dic,
    DateOnly? UpdatedOn);

// Plný subjekt z ARES pro doplnění poplatníka: vedle jména a DIČ nese i adresu a kód cílového
// finančního úřadu (c_ufo), odvozený z číselníku FinancniUrad.
public sealed record AresSubjectDetail(
    string Ico,
    string OfficialName,
    string? Dic,
    string? Street,
    string? HouseNumber,
    string? City,
    string? PostalCode,
    string? TaxOfficeCode);

public sealed record ExchangeRate(
    DateOnly Date,
    string CurrencyCode,
    int Amount,
    decimal RateCzk)
{
    public decimal RatePerUnit => RateCzk / Amount;
}
