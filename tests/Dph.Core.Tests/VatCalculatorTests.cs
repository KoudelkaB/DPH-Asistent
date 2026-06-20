using Dph.Core.Calculations;
using Dph.Core.Domain;

namespace Dph.Core.Tests;

public sealed class VatCalculatorTests
{
    [Fact]
    public void Reverse_Charge_Adds_Output_And_Full_Deduction()
    {
        var calculator = new VatCalculator();
        var summary = calculator.Calculate(new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.ReverseCharge,
                TaxBaseCzk = 1_000m
            }
        });

        Assert.Equal(210m, summary.TaxDue);
        Assert.Equal(210m, summary.TaxDeduction);
        Assert.Equal(0m, summary.NetTax);
    }
}
