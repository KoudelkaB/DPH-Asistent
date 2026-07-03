namespace Dph.Core.Epo;

public sealed class EpoTaxFormDefinition
{
    public static EpoTaxFormDefinition Current { get; } = new();

    public string VatReturnElement { get; init; } = "DPHDP3";
    public string VatReturnVersion { get; init; } = "01.02";
    public string ControlStatementElement { get; init; } = "DPHKH1";
    public string ControlStatementVersion { get; init; } = "03.01";
    public decimal ControlStatementDetailLimitCzk { get; init; } = 10_000m;
    public string SoftwareName { get; init; } = "DPH Asistent";
    public string SoftwareVersion { get; init; } = "0.2.0";

    // Reverse charge v této aplikaci = přijetí služby ze zahraničí: dodavatel z EU (§9 odst.1) →
    // ř.5/6, ze třetí země (§108) → ř.12/13; odpočet ř.43/44. Není to tuzemský přenos §92a (KH B1);
    // v kontrolním hlášení se vykazuje v oddílu A.2.
    public string ReverseChargeNotice { get; init; } =
        "Reverse charge = přijetí služby ze zahraničí: EU dodavatel → ř.5/6, třetí země → ř.12/13, odpočet ř.43/44; v kontrolním hlášení oddíl A.2.";
}
