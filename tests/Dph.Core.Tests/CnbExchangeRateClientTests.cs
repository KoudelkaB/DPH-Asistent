using Dph.Core.Services;

namespace Dph.Core.Tests;

public sealed class CnbExchangeRateClientTests
{
    [Fact]
    public void Parses_Cnb_Rate_With_Comma_Decimal_Separator()
    {
        const string text = """
            19.06.2026 #117
            země|měna|množství|kód|kurz
            EMU|euro|1|EUR|24,225
            USA|dolar|1|USD|21,126
            """;

        var rate = CnbExchangeRateClient.Parse(text, "USD");

        Assert.NotNull(rate);
        Assert.Equal(new DateOnly(2026, 6, 19), rate.Date);
        Assert.Equal("USD", rate.CurrencyCode);
        Assert.Equal(21.126m, rate.RatePerUnit);
    }
}
