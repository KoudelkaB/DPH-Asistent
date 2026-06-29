using Dph.Core.Invoicing;

namespace Dph.Core.Tests;

public sealed class SpaydBuilderTests
{
    [Fact]
    public void Builds_Spayd_With_All_Fields()
    {
        var spayd = SpaydBuilder.Build("CZ6508000000192000145399", 16312m, "CZK", "20260001", "Faktura 20260001");
        Assert.Equal(
            "SPD*1.0*ACC:CZ6508000000192000145399*AM:16312.00*CC:CZK*X-VS:20260001*MSG:Faktura 20260001",
            spayd);
    }

    [Fact]
    public void Formats_Amount_With_Two_Decimals_Invariant()
    {
        var spayd = SpaydBuilder.Build("CZ6508000000192000145399", 1234.5m, "czk", null, null);
        Assert.Contains("*AM:1234.50*", spayd);
        Assert.EndsWith("*CC:CZK", spayd);
        Assert.DoesNotContain("X-VS", spayd);
        Assert.DoesNotContain("MSG", spayd);
    }

    [Fact]
    public void Strips_Non_Digit_Variable_Symbol()
    {
        var spayd = SpaydBuilder.Build("CZ65", 1m, "CZK", "VS-2026/0042", "Platba");
        Assert.Contains("*X-VS:20260042*", spayd);
    }
}
