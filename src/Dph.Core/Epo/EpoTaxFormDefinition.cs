namespace Dph.Core.Epo;

public sealed class EpoTaxFormDefinition
{
    public static EpoTaxFormDefinition Current { get; } = new();

    public string VatReturnElement { get; init; } = "DPHDP3";
    public string VatReturnVersion { get; init; } = "01.02";
    public string ControlStatementElement { get; init; } = "DPHKH1";
    public string ControlStatementVersion { get; init; } = "03.01";
    public decimal ControlStatementDetailLimitCzk { get; init; } = 10_000m;
    public string SoftwareName { get; init; } = "DPH Assistant";
    public string SoftwareVersion { get; init; } = "0.1";

    public string ReverseChargeNotice { get; init; } =
        "Reverse-charge XML mapping must be verified against a fresh EPO reference fixture before filing.";
}
