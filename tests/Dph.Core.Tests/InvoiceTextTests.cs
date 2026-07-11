using Dph.Core.Invoicing;

namespace Dph.Core.Tests;

public sealed class InvoiceTextTests
{
    [Fact]
    public void Resolves_Month_And_Year_Placeholders_From_Taxable_Supply_Date()
    {
        var resolved = InvoiceText.ResolvePlaceholders(InvoiceText.DefaultIntroTemplate, new DateOnly(2026, 6, 30));

        Assert.Equal("Za červen 2026 Vám fakturujeme:", resolved);
    }

    [Fact]
    public void Resolves_Ascii_And_Differently_Cased_Placeholders()
    {
        var resolved = InvoiceText.ResolvePlaceholders("Za {MESIC} {Rok}.", new DateOnly(2026, 1, 31));

        Assert.Equal("Za leden 2026.", resolved);
    }

    [Fact]
    public void Keeps_Text_Without_Placeholders_And_Handles_Null()
    {
        Assert.Equal("Fakturujeme:", InvoiceText.ResolvePlaceholders("Fakturujeme:", new DateOnly(2026, 6, 30)));
        Assert.Equal("", InvoiceText.ResolvePlaceholders(null, new DateOnly(2026, 6, 30)));
    }

    [Theory]
    [InlineData(1, "leden")]
    [InlineData(9, "září")]
    [InlineData(12, "prosinec")]
    public void Month_Names_Are_Nominative(int month, string expected)
        => Assert.Equal(expected, InvoiceText.MonthNominative(month));
}
