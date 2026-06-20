namespace Dph.Core.Domain;

public enum CounterpartyRole
{
    Customer,
    Supplier,
    Both
}

public enum InvoiceKind
{
    IssuedDomestic,
    ReceivedDomesticWithVat,
    ReverseCharge
}

public enum VatRateKind
{
    Standard21
}
