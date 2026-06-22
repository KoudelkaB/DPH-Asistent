namespace Dph.Core.Domain;

public enum CounterpartyRole
{
    Customer,
    Supplier,
    Both
}

public enum InvoiceKind
{
    /// <summary>Tuzemské vydané plnění (daň na výstupu, ř.1/2; KH oddíl A4/A5).</summary>
    IssuedDomestic,

    /// <summary>Tuzemské přijaté plnění s českou DPH od plátce (odpočet ř.40/41; KH oddíl B2/B3).</summary>
    ReceivedDomesticWithVat,

    /// <summary>
    /// Reverse charge = <b>přijetí služby ze zahraničí</b>, kde daň přiznává příjemce – typicky
    /// zahraniční SaaS/digitální služba (Anthropic, GitHub, OpenAI…). Řádek přiznání se určí podle
    /// původu dodavatele (z prefixu jeho DIČ):
    /// <list type="bullet">
    /// <item>dodavatel registrovaný v JČS (EU prefix, např. IE) → §9(1) → <b>ř.5/6</b>;</item>
    /// <item>dodavatel ze třetí země / bez EU DIČ (USA) → §108 → <b>ř.12/13</b>.</item>
    /// </list>
    /// Odpočet u obojího jde na <b>ř.43/44</b>; do kontrolního hlášení <b>nepatří</b>.
    /// POZOR: NEjde o tuzemský režim přenesení daňové povinnosti §92a (stavební práce, kovy…),
    /// který by patřil na ř.10/11 a do KH oddílu B1 – ten zde záměrně nemodelujeme.
    /// </summary>
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
