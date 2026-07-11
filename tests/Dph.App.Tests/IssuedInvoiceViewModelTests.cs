using Dph.App.ViewModels;
using Dph.Core.Domain;

namespace Dph.App.Tests;

public sealed class IssuedInvoiceViewModelTests
{
    [Fact]
    public void Item_Computes_Line_Amounts_And_Accepts_Comma_Decimals()
    {
        var item = new IssuedInvoiceItemViewModel
        {
            Quantity = "2,5",
            UnitPriceCzk = "100",
            VatRate = "21"
        };

        Assert.Equal(250m, item.LineBase);
        Assert.Equal(52.5m, item.LineVat);
        Assert.Equal("302.5", item.LineGrossText);
    }

    [Fact]
    public void Item_ToDomain_Defaults_Blank_Unit()
    {
        var item = new IssuedInvoiceItemViewModel { Unit = "  " };

        Assert.Equal("ks", item.ToDomain().Unit);
    }

    [Fact]
    public void TotalsText_Computes_From_Items_When_Loaded()
    {
        var invoice = new IssuedInvoiceViewModel();
        invoice.Items.Add(new IssuedInvoiceItemViewModel { Quantity = "1", UnitPriceCzk = "1000", VatRate = "21" });

        Assert.Equal("Základ 1000 | DPH 210 | Celkem 1210 Kč", invoice.TotalsText);
    }

    [Fact]
    public void TotalsText_Uses_Stored_Totals_For_Headers_Without_Items()
    {
        // Seznam faktur načítá jen hlavičky – souhrn musí jít z denormalizovaných sloupců.
        var invoice = IssuedInvoiceViewModel.FromDomain(new IssuedInvoice
        {
            Id = 1,
            Number = "20260001",
            StoredTotalBaseCzk = 500m,
            StoredTotalVatCzk = 105m
        }, itemsLoaded: false);

        Assert.Equal("Základ 500 | DPH 105 | Celkem 605 Kč", invoice.TotalsText);
    }

    [Fact]
    public void ToDomain_Normalizes_Currency_And_Blank_Fields()
    {
        var invoice = new IssuedInvoiceViewModel
        {
            Number = " 20260001 ",
            Currency = "eur",
            VariableSymbol = "   ",
            CustomerCountry = ""
        };

        var domain = invoice.ToDomain();

        Assert.Equal("20260001", domain.Number);
        Assert.Equal("EUR", domain.Currency);
        Assert.Null(domain.VariableSymbol);
        Assert.Equal("Česká republika", domain.CustomerCountry);
    }

    [Fact]
    public void Pdf_Export_Locks_While_Open_Vat_Insert_Does_Not()
    {
        var invoice = new IssuedInvoiceViewModel { VatPeriodState = IssuedInvoiceVatPeriodState.Open };
        Assert.False(invoice.IsLockedByHistory);

        // Vložení do otevřeného přiznání je průběžná synchronizace – nezamyká.
        invoice.VatInsertedAt = DateTimeOffset.UtcNow;
        Assert.False(invoice.IsLockedByHistory);
        Assert.True(invoice.IsEditable);

        // Uzavření období nebo export PDF už jsou historické milníky – zamknou.
        invoice.VatPeriodState = IssuedInvoiceVatPeriodState.Closed;
        Assert.True(invoice.IsLockedByHistory);

        invoice.VatPeriodState = IssuedInvoiceVatPeriodState.Open;
        invoice.PdfExportedAt = DateTimeOffset.UtcNow;
        Assert.True(invoice.IsLockedByHistory);
        Assert.False(invoice.IsEditable);
    }
}
