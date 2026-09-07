using Dph.App.ViewModels;
using Dph.Core.Domain;

namespace Dph.App.Tests;

public sealed class CalculationRegressionTests
{
    [Fact]
    public void New_Items_Offer_Only_Supported_Rates_But_Legacy_Zero_Remains_Visible()
    {
        Assert.DoesNotContain("0", new InvoiceLineViewModel().VatRateOptions);
        Assert.DoesNotContain("0", new IssuedInvoiceItemViewModel().VatRateOptions);
        var legacy = InvoiceLineViewModel.FromDomain(new() { VatRate = VatRateKind.Zero0 });
        Assert.Equal("0", legacy.VatRate);
        Assert.Contains("0", legacy.VatRateOptions);
        var options = legacy.VatRateOptions;
        legacy.VatRate = "21";
        Assert.Same(options, legacy.VatRateOptions);
        Assert.Contains("0", legacy.VatRateOptions);
    }

    [Fact]
    public void Opening_Item_Preserves_Quantity_And_Unit_Price_Precision()
    {
        var item = new IssuedInvoiceItem { Quantity = 12.3456m, UnitPriceCzk = 98.76543m };
        var saved = IssuedInvoiceItemViewModel.FromDomain(item).ToDomain();
        Assert.Equal(item.Quantity, saved.Quantity);
        Assert.Equal(item.UnitPriceCzk, saved.UnitPriceCzk);
        Assert.Equal(item.LineGrossCzk, saved.LineGrossCzk);
    }

    [Theory]
    [InlineData(InvoiceKind.ReverseCharge, "CZ12345678")]
    [InlineData(InvoiceKind.ReceivedDomesticWithVat, "DE123456789")]
    [InlineData(InvoiceKind.ReceivedDomesticWithVat, null)]
    public void Opening_Line_Preserves_Explicit_Tax_Mode(InvoiceKind kind, string? dic)
    {
        var line = new InvoiceLine { Kind = kind, CounterpartyDic = dic, ExchangeRate = 0.0012345m };
        var saved = InvoiceLineViewModel.FromDomain(line).ToDomain();
        Assert.Equal(kind, saved.Kind);
        Assert.Equal(line.ExchangeRate, saved.ExchangeRate);
    }

    [Fact]
    public void Invalid_Editing_Does_Not_Recalculate_Other_Amounts()
    {
        var line = new InvoiceLineViewModel { TaxBaseCzk = "1000" };
        line.TaxBaseCzk = "abc";
        Assert.Equal("210", line.VatCzk);
        Assert.Equal("1210", line.GrossCzk);
        Assert.Throws<FormatException>(() => line.ToDomain());
    }

    [Fact]
    public void Invalid_Gross_Cannot_Be_Saved()
    {
        var line = new InvoiceLineViewModel { GrossCzk = "abc" };
        Assert.Throws<FormatException>(() => line.ToDomain());
    }

    [Fact]
    public void Invalid_Date_Is_Not_Replaced_By_Today()
        => Assert.Throws<FormatException>(() => new InvoiceLineViewModel { TaxableSupplyDate = "bad" }.ToDomain());
}
