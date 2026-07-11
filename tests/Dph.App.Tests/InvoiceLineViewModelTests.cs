using Dph.App.ViewModels;
using Dph.Core.Domain;

namespace Dph.App.Tests;

public sealed class InvoiceLineViewModelTests
{
    [Fact]
    public void Editing_Base_Recomputes_Vat_And_Gross_By_Rate()
    {
        var line = new InvoiceLineViewModel { TaxBaseCzk = "1000" };

        Assert.Equal("210", line.VatCzk);
        Assert.Equal("1210", line.GrossCzk);
    }

    [Fact]
    public void Editing_Gross_Back_Computes_Base_And_Vat()
    {
        var line = new InvoiceLineViewModel { VatRate = "12", GrossCzk = "1120" };

        Assert.Equal("1000", line.TaxBaseCzk);
        Assert.Equal("120", line.VatCzk);
    }

    [Fact]
    public void Editing_Vat_Back_Computes_Base_And_Gross()
    {
        var line = new InvoiceLineViewModel { VatCzk = "210" };

        Assert.Equal("1000", line.TaxBaseCzk);
        Assert.Equal("1210", line.GrossCzk);
    }

    [Fact]
    public void Changing_Rate_Recomputes_Vat_From_Base()
    {
        var line = new InvoiceLineViewModel { TaxBaseCzk = "1000" };

        line.VatRate = "12";

        Assert.Equal("120", line.VatCzk);
        Assert.Equal("1120", line.GrossCzk);
    }

    [Fact]
    public void Editing_Vat_At_Zero_Rate_Keeps_Base_And_Updates_Gross()
    {
        var line = new InvoiceLineViewModel { VatRate = "0", TaxBaseCzk = "1000" };

        line.VatCzk = "50";

        Assert.Equal("1000", line.TaxBaseCzk);
        Assert.Equal("1050", line.GrossCzk);
    }

    [Fact]
    public void Accepts_Comma_Decimal_Input()
    {
        var line = new InvoiceLineViewModel { TaxBaseCzk = "100,5" };

        Assert.Equal("21.11", line.VatCzk);
        Assert.Equal("121.61", line.GrossCzk);
    }

    [Fact]
    public void FromDomain_Preserves_Stored_Vat_That_Does_Not_Match_The_Rate()
    {
        // Import z KH nese daň, která nemusí přesně sedět na základ × sazba – nesmí se přepočítat.
        var line = InvoiceLineViewModel.FromDomain(new InvoiceLine
        {
            Kind = InvoiceKind.ReceivedDomesticWithVat,
            TaxBaseCzk = 1000m,
            VatCzk = 210.01m,
            VatRate = VatRateKind.Standard21
        });

        Assert.Equal("1000", line.TaxBaseCzk);
        Assert.Equal("210.01", line.VatCzk);
        Assert.Equal("1210.01", line.GrossCzk);
    }

    [Theory]
    [InlineData("Vydaná", "CZ27082440", "X-1", InvoiceKind.IssuedDomestic)]
    [InlineData("Přijatá", "CZ27082440", "X-1", InvoiceKind.ReceivedDomesticWithVat)]
    [InlineData("Přijatá", "27082440", "X-1", InvoiceKind.ReceivedDomesticWithVat)]
    [InlineData("Přijatá", "DE811907980", "X-1", InvoiceKind.ReverseCharge)]
    [InlineData("Přijatá", "", "X-1", InvoiceKind.ReverseCharge)]
    [InlineData("Přijatá", "", "B3", InvoiceKind.ReceivedDomesticWithVat)]
    public void ToDomain_Derives_Kind_From_Selection_Dic_And_Summary_Code(
        string kind, string dic, string evidenceNumber, InvoiceKind expected)
    {
        var line = new InvoiceLineViewModel
        {
            Kind = kind,
            CounterpartyDic = dic,
            EvidenceNumber = evidenceNumber
        };

        Assert.Equal(expected, line.ToDomain().Kind);
    }

    [Fact]
    public void ToDomain_Drops_Partial_Deduction_Outside_Domestic_Received()
    {
        // Skrytý checkbox může držet starou hodnotu – u reverse charge se nesmí propsat do domény.
        var reverseCharge = new InvoiceLineViewModel
        {
            Kind = "Přijatá",
            CounterpartyDic = "DE811907980",
            PartialDeduction = true
        };
        Assert.False(reverseCharge.ToDomain().PartialDeduction);

        var domestic = new InvoiceLineViewModel
        {
            Kind = "Přijatá",
            CounterpartyDic = "CZ27082440",
            PartialDeduction = true
        };
        Assert.True(domestic.ToDomain().PartialDeduction);
    }

    [Fact]
    public void Editing_Counterparty_Fields_Detaches_Selected_Counterparty()
    {
        var counterparty = new CounterpartyViewModel { Id = 7, Name = "Dodavatel s.r.o.", Dic = "CZ27082440" };
        var line = new InvoiceLineViewModel { Counterparty = counterparty };

        Assert.Equal(7, line.CounterpartyId);
        Assert.Equal("Dodavatel s.r.o.", line.CounterpartyName);

        line.CounterpartyName = "Někdo jiný";

        Assert.Null(line.Counterparty);
        Assert.Null(line.CounterpartyId);
    }
}
