using Dph.Core.Domain;
using Dph.Core.Invoicing;
using Dph.Core.Persistence;

namespace Dph.Core.Tests;

public sealed class IssuedInvoiceTests
{
    [Fact]
    public void Intro_Placeholders_Resolve_From_Taxable_Supply_Date()
    {
        Assert.Equal(
            "Za květen 2026 Vám fakturujeme:",
            InvoiceText.ResolvePlaceholders(InvoiceText.DefaultIntroTemplate, new DateOnly(2026, 5, 31)));

        // ASCII varianta i jiné období.
        Assert.Equal(
            "Za prosinec 2025.",
            InvoiceText.ResolvePlaceholders("Za {mesic} {rok}.", new DateOnly(2025, 12, 1)));
    }

    [Fact]
    public void Intro_Without_Placeholders_Is_Unchanged()
    {
        Assert.Equal("Děkujeme za spolupráci.",
            InvoiceText.ResolvePlaceholders("Děkujeme za spolupráci.", new DateOnly(2026, 6, 30)));
    }

    [Fact]
    public void Totals_Sum_Items_And_Vat()
    {
        var invoice = new IssuedInvoice
        {
            Items =
            {
                new IssuedInvoiceItem { Quantity = 10, UnitPriceCzk = 1200, VatRate = VatRateKind.Standard21 },
                new IssuedInvoiceItem { Quantity = 2, UnitPriceCzk = 800, VatRate = VatRateKind.Reduced12 }
            }
        };

        Assert.Equal(13600m, invoice.TotalBaseCzk);   // 12000 + 1600
        Assert.Equal(2712m, invoice.TotalVatCzk);     // 2520 + 192
        Assert.Equal(16312m, invoice.TotalGrossCzk);
    }

    [Fact]
    public void VatRecap_Groups_By_Rate_Descending()
    {
        var invoice = new IssuedInvoice
        {
            Items =
            {
                new IssuedInvoiceItem { Quantity = 1, UnitPriceCzk = 100, VatRate = VatRateKind.Reduced12 },
                new IssuedInvoiceItem { Quantity = 1, UnitPriceCzk = 200, VatRate = VatRateKind.Standard21 },
                new IssuedInvoiceItem { Quantity = 1, UnitPriceCzk = 50, VatRate = VatRateKind.Reduced12 }
            }
        };

        var recap = invoice.VatRecap();
        Assert.Equal(2, recap.Count);
        Assert.Equal(VatRateKind.Standard21, recap[0].Rate);
        Assert.Equal(200m, recap[0].BaseCzk);
        Assert.Equal(42m, recap[0].VatCzk);
        Assert.Equal(VatRateKind.Reduced12, recap[1].Rate);
        Assert.Equal(150m, recap[1].BaseCzk);
        Assert.Equal(18m, recap[1].VatCzk);
    }

    [Fact]
    public void PaymentVariableSymbol_Defaults_To_Number_Digits()
    {
        Assert.Equal("20260001", new IssuedInvoice { Number = "20260001" }.PaymentVariableSymbol);
        Assert.Equal("777", new IssuedInvoice { Number = "20260001", VariableSymbol = "VS 777" }.PaymentVariableSymbol);
    }

    [Fact]
    public void PdfRenderer_Renders_Issued_Invoice_With_Qr_And_Wide_Amounts()
    {
        var targetPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        try
        {
            var supplier = new TaxSubject
            {
                DisplayName = "Testovací dodavatel",
                Street = "Testovací",
                HouseNumber = "1",
                PostalCode = "28932",
                City = "Testov",
                Ico = "12345678",
                Dic = "CZ12345678",
                BankAccount = "123456789/0100"
            };

            var invoice = new IssuedInvoice
            {
                Number = "20260006",
                IssueDate = new DateOnly(2026, 6, 30),
                TaxableSupplyDate = new DateOnly(2026, 6, 30),
                DueDate = new DateOnly(2026, 7, 14),
                CustomerName = "Testovací odběratel s.r.o.",
                CustomerStreet = "Odběratelská",
                CustomerHouseNumber = "2532/19",
                CustomerPostalCode = "19000",
                CustomerCity = "Praha",
                CustomerIco = "87654321",
                CustomerDic = "CZ87654321",
                IntroText = "Za červen 2026 Vám fakturujeme:",
                Items =
                {
                    new IssuedInvoiceItem
                    {
                        Description = "MQPS + LS + MDP + OCRS + TMA",
                        Quantity = 147,
                        Unit = "h",
                        UnitPriceCzk = 867m,
                        VatRate = VatRateKind.Standard21
                    }
                }
            };

            new InvoicePdfRenderer().Render(supplier, invoice, targetPath);

            Assert.True(File.Exists(targetPath));
            Assert.True(new FileInfo(targetPath).Length > 1000);
        }
        finally
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
        }
    }

    [Fact]
    public async Task Repository_RoundTrips_Invoice_With_Items_And_Generates_Numbers()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        var repository = new DphRepository(path);
        await repository.InitializeAsync();

        Assert.Equal("20260001", await repository.NextInvoiceNumberAsync(2026));

        var invoice = new IssuedInvoice
        {
            Number = "20260001",
            IssueDate = new DateOnly(2026, 6, 30),
            TaxableSupplyDate = new DateOnly(2026, 6, 30),
            DueDate = new DateOnly(2026, 7, 14),
            CustomerName = "Žďár s.r.o.",
            CustomerIco = "87654321",
            CustomerDic = "CZ87654321",
            CustomerCity = "Příbram",
            IntroText = "Za červen 2026 Vám fakturujeme:",
            Items =
            {
                new IssuedInvoiceItem { Description = "Práce", Quantity = 3, Unit = "hod", UnitPriceCzk = 1000.5m, VatRate = VatRateKind.Standard21 }
            }
        };
        await repository.SaveIssuedInvoiceAsync(invoice);
        Assert.NotEqual(0, invoice.Id);

        // Po uložení faktury se další číslo posune.
        Assert.Equal("20260002", await repository.NextInvoiceNumberAsync(2026));

        var loaded = await repository.LoadIssuedInvoiceAsync(invoice.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Žďár s.r.o.", loaded!.CustomerName);
        Assert.Equal("Příbram", loaded.CustomerCity);
        Assert.Equal("Za červen 2026 Vám fakturujeme:", loaded.IntroText);
        var item = Assert.Single(loaded.Items);
        Assert.Equal("Práce", item.Description);
        Assert.Equal(3m, item.Quantity);
        Assert.Equal(1000.5m, item.UnitPriceCzk);
        Assert.Equal(VatRateKind.Standard21, item.VatRate);

        // Seznam vrací jen hlavičky (bez položek); souhrn se čte z denormalizovaných sloupců.
        var listItem = Assert.Single(await repository.LoadIssuedInvoicesAsync());
        Assert.Empty(listItem.Items);
        Assert.Equal(3001.5m, listItem.StoredTotalBaseCzk);
        Assert.Equal(630.32m, listItem.StoredTotalVatCzk);

        // Editace = smaž a vlož položky; nesmí zůstat duplicity.
        loaded.Items.Add(new IssuedInvoiceItem { Description = "Doprava", Quantity = 1, UnitPriceCzk = 200, VatRate = VatRateKind.Standard21 });
        await repository.SaveIssuedInvoiceAsync(loaded);
        var reloaded = await repository.LoadIssuedInvoiceAsync(invoice.Id);
        Assert.Equal(2, reloaded!.Items.Count);

        await repository.MarkIssuedInvoicePdfExportedAsync(invoice.Id, new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero));
        await repository.MarkIssuedInvoiceVatInsertedAsync(invoice.Id, new DateTimeOffset(2026, 7, 2, 10, 0, 0, TimeSpan.Zero));
        await repository.MarkIssuedInvoiceChangedAsync(invoice.Id, new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero));
        var protectedInvoice = await repository.LoadIssuedInvoiceAsync(invoice.Id);
        Assert.True(protectedInvoice!.IsLockedByHistory);
        Assert.True(protectedInvoice.HasPendingChanges);
        Assert.NotNull(protectedInvoice.PdfExportedAt);
        Assert.NotNull(protectedInvoice.VatInsertedAt);
        Assert.NotNull(protectedInvoice.PdfChangedAt);
        Assert.NotNull(protectedInvoice.VatChangedAt);
        Assert.True(protectedInvoice.HasPdfPendingChanges);
        Assert.True(protectedInvoice.HasVatPendingChanges);

        await repository.DeleteIssuedInvoiceAsync(invoice.Id);
        Assert.Null(await repository.LoadIssuedInvoiceAsync(invoice.Id));
    }

    [Fact]
    public async Task Repository_Tracks_Pdf_And_Vat_Pending_Changes_Separately()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        var repository = new DphRepository(path);
        await repository.InitializeAsync();

        var invoice = new IssuedInvoice
        {
            Number = "20260001",
            CustomerName = "Test",
            Items = { new IssuedInvoiceItem { Description = "x", Quantity = 1, UnitPriceCzk = 100, VatRate = VatRateKind.Standard21 } }
        };
        await repository.SaveIssuedInvoiceAsync(invoice);

        await repository.MarkIssuedInvoicePdfExportedAsync(invoice.Id, new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero));
        await repository.MarkIssuedInvoiceVatInsertedAsync(invoice.Id, new DateTimeOffset(2026, 7, 2, 10, 0, 0, TimeSpan.Zero));
        await repository.MarkIssuedInvoiceChangedAsync(invoice.Id, new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero));

        await repository.MarkIssuedInvoiceVatInsertedAsync(invoice.Id, new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero));
        var afterVatUpdate = await repository.LoadIssuedInvoiceAsync(invoice.Id);
        Assert.True(afterVatUpdate!.HasPendingChanges);
        Assert.True(afterVatUpdate.HasPdfPendingChanges);
        Assert.False(afterVatUpdate.HasVatPendingChanges);

        await repository.MarkIssuedInvoicePdfExportedAsync(invoice.Id, new DateTimeOffset(2026, 7, 5, 10, 0, 0, TimeSpan.Zero));
        var afterPdfUpdate = await repository.LoadIssuedInvoiceAsync(invoice.Id);
        Assert.False(afterPdfUpdate!.HasPendingChanges);
        Assert.False(afterPdfUpdate.HasPdfPendingChanges);
        Assert.False(afterPdfUpdate.HasVatPendingChanges);
        Assert.Null(afterPdfUpdate.PdfChangedAt);
        Assert.Null(afterPdfUpdate.VatChangedAt);
    }

    [Fact]
    public async Task Repository_Loads_Issued_Invoices_By_Duzp_Month()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        var repository = new DphRepository(path);
        await repository.InitializeAsync();

        await repository.SaveIssuedInvoiceAsync(new IssuedInvoice
        {
            Number = "20260601",
            TaxableSupplyDate = new DateOnly(2026, 6, 15),
            CustomerName = "Červen A",
            Items = { new IssuedInvoiceItem { Description = "x", Quantity = 1, UnitPriceCzk = 100, VatRate = VatRateKind.Standard21 } }
        });
        await repository.SaveIssuedInvoiceAsync(new IssuedInvoice
        {
            Number = "20260602",
            TaxableSupplyDate = new DateOnly(2026, 6, 30),
            CustomerName = "Červen B"
        });
        await repository.SaveIssuedInvoiceAsync(new IssuedInvoice
        {
            Number = "20260701",
            TaxableSupplyDate = new DateOnly(2026, 7, 1),
            CustomerName = "Červenec"
        });

        var june = await repository.LoadIssuedInvoicesForPeriodAsync(2026, 6);
        Assert.Equal(2, june.Count);
        Assert.All(june, x => Assert.Equal(6, x.TaxableSupplyDate.Month));
        Assert.Single(june.First(x => x.Number == "20260601").Items);

        Assert.Single(await repository.LoadIssuedInvoicesForPeriodAsync(2026, 7));
        Assert.Empty(await repository.LoadIssuedInvoicesForPeriodAsync(2026, 5));
    }

    [Fact]
    public async Task Repository_RoundTrips_Tax_Subject_Bank_Fields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
        var repository = new DphRepository(path);
        await repository.InitializeAsync();

        await repository.SaveTaxSubjectAsync(new TaxSubject
        {
            DisplayName = "Jan Novák",
            Dic = "CZ1234567890",
            BankAccount = "19-2000145399/0800",
            Iban = "CZ6508000000192000145399"
        });

        var loaded = await repository.LoadTaxSubjectAsync();
        Assert.Equal("19-2000145399/0800", loaded!.BankAccount);
        Assert.Equal("CZ6508000000192000145399", loaded.Iban);
    }
}
