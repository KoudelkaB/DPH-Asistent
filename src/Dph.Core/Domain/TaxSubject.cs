namespace Dph.Core.Domain;

public sealed class TaxSubject
{
    public long Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string Dic { get; set; } = "";
    public string? Ico { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Title { get; set; }
    public string Street { get; set; } = "";
    public string? HouseNumber { get; set; }
    public string City { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Country { get; set; } = "Česká Republika";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string TaxOfficeCode { get; set; } = "";
    public string WorkplaceCode { get; set; } = "";
    public string? DataBoxId { get; set; }
    public string ActivityCode { get; set; } = "620000";
}
