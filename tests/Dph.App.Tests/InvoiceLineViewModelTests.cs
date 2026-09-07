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
    public void Editing_Vat_Preserves_Documented_Base_And_Updates_Gross()
    {
        var line = new InvoiceLineViewModel { TaxBaseCzk = "1000" };
        line.VatCzk = "210.01";

        Assert.Equal("1000", line.TaxBaseCzk);
        Assert.Equal("1210.01", line.GrossCzk);
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
    [InlineData("Přijatá", "DE811907980", "X-1", InvoiceKind.ReceivedDomesticWithVat)]
    [InlineData("Zahraniční služba (RC)", "DE811907980", "X-1", InvoiceKind.ReverseCharge)]
    [InlineData("Přijatá", "", "X-1", InvoiceKind.ReceivedDomesticWithVat)]
    [InlineData("Zahraniční služba (RC)", "", "X-1", InvoiceKind.ReverseCharge)]
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
    public void Proportion_Is_Independent_Of_Limit_And_Does_Not_Change_Amounts()
    {
        var line = InvoiceLineViewModel.FromDomain(new()
        {
            Kind = InvoiceKind.ReceivedDomesticWithVat,
            TaxBaseCzk = 5000m, VatCzk = 1050m,
            EvidenceNumber = "PART"
        });
        Assert.True(line.IsPartialDeductionEnabled);
        line.DocumentAboveControlLimit = true;
        Assert.True(line.IsPartialDeductionEnabled);
        line.PartialDeduction = true;
        var domain = line.ToDomain();
        Assert.Equal(5000m, domain.TaxBaseCzk);
        Assert.Equal(1050m, domain.VatCzk);
        Assert.True(domain.DocumentAboveControlLimit);
        Assert.True(domain.PartialDeduction);
        line.DocumentAboveControlLimit = false;
        Assert.True(line.IsPartialDeductionEnabled);
        Assert.True(line.IsPartialDeductionEnabled);
        line.EvidenceNumber = "B3";
        Assert.True(line.IsPartialDeductionEnabled);
        line.Kind = "Zahraniční služba (RC)";
        Assert.False(line.ShowPartialDeduction);
        Assert.False(line.ToDomain().DocumentAboveControlLimit);
    }

    [Theory]
    [InlineData("9999.99", true, true)]
    [InlineData("10000", true, true)]
    [InlineData("10000.01", true, false)]
    [InlineData("-9999.99", true, true)]
    [InlineData("-10000", true, true)]
    [InlineData("100", false, false)]
    [InlineData("invalid", true, false)]
    public void Above_Limit_Is_Visible_Only_For_Proportion_Below_Limit(string gross, bool proportion, bool visible)
    {
        var line = new InvoiceLineViewModel { GrossCzk = gross, PartialDeduction = proportion };
        Assert.Equal(visible, line.ShowDocumentAboveControlLimit);
        Assert.True(line.IsPartialDeductionEnabled);
        line.DocumentAboveControlLimit = true;
        Assert.Equal(visible, line.ShowDocumentAboveControlLimit);
        line.Kind = "Vydaná";
        Assert.False(line.ShowDocumentAboveControlLimit);
    }

    [Fact]
    public void Unticking_Proportion_Clears_Override_And_Exports_Small_Row_As_B3()
    {
        var line = new InvoiceLineViewModel { GrossCzk = "3000", PartialDeduction = true, DocumentAboveControlLimit = true };
        line.PartialDeduction = false;
        Assert.False(line.DocumentAboveControlLimit);
        Assert.False(line.ShowDocumentAboveControlLimit);
        var kh = new Dph.Core.Epo.EpoXmlExporter().ExportControlStatement(new(), new() { Year = 2026, Month = 5 }, [line.ToDomain()]);
        Assert.Single(kh.Descendants("VetaB3"));
        Assert.Empty(kh.Descendants("VetaB2"));
    }

    [Theory]
    [InlineData("base")]
    [InlineData("vat")]
    [InlineData("gross")]
    [InlineData("rate")]
    public void Editing_Imported_Nonproportional_Amounts_Releases_Explicit_Detail(string field)
    {
        var line = InvoiceLineViewModel.FromDomain(new()
        {
            Kind = InvoiceKind.ReceivedDomesticWithVat, TaxBaseCzk = 1000m, VatCzk = 210m,
            DocumentAboveControlLimit = true
        });
        Assert.True(line.ToDomain().DocumentAboveControlLimit);
        switch (field)
        {
            case "base": line.TaxBaseCzk = "2000"; break;
            case "vat": line.VatCzk = "200"; break;
            case "gross": line.GrossCzk = "2000"; break;
            case "rate": line.VatRate = "12"; break;
        }
        Assert.False(line.ToDomain().DocumentAboveControlLimit);
    }

    [Fact]
    public void ToDomain_Drops_Partial_Deduction_Outside_Domestic_Received()
    {
        // Skrytý checkbox může držet starou hodnotu – u reverse charge se nesmí propsat do domény.
        var reverseCharge = new InvoiceLineViewModel
        {
            Kind = "Zahraniční služba (RC)",
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
