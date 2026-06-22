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
    Standard21,
    Reduced12,
    Zero0
}

public static class InvoiceKindExtensions
{
    // "Issued" vs "Received" bucket used to scope duplicate-document checks.
    public static string ReferenceScope(this InvoiceKind kind)
        => kind == InvoiceKind.IssuedDomestic ? "Issued" : "Received";
}
