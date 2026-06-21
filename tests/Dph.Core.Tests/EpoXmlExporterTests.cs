using Dph.Core.Domain;
using Dph.Core.Epo;

namespace Dph.Core.Tests;

public sealed class EpoXmlExporterTests
{
    [Fact]
    public void Exports_Domestic_Invoices_To_Dph_And_Kh_Structure()
    {
        var exporter = new EpoXmlExporter();
        var subject = Subject();
        var period = new VatPeriod { Year = 2026, Month = 5, SubmissionDate = new DateOnly(2026, 6, 20) };
        var invoices = new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.IssuedDomestic,
                EvidenceNumber = "20260005",
                CounterpartyDic = "CZ61506133",
                TaxableSupplyDate = new DateOnly(2026, 5, 31),
                TaxBaseCzk = 123_408.78m,
                VatCzk = 25_915.84m
            },
            new InvoiceLine
            {
                Kind = InvoiceKind.ReceivedDomesticWithVat,
                EvidenceNumber = "4007482971",
                CounterpartyDic = "CZ27082440",
                TaxableSupplyDate = new DateOnly(2026, 5, 12),
                TaxBaseCzk = 3_594.21m,
                VatCzk = 754.79m
            }
        };

        var dph = exporter.ExportVatReturn(subject, period, invoices);
        var kh = exporter.ExportControlStatement(subject, period, invoices);

        Assert.NotNull(dph.Descendants("Veta1").SingleOrDefault());
        Assert.NotNull(dph.Descendants("Veta4").SingleOrDefault());
        Assert.NotNull(kh.Descendants("VetaA4").SingleOrDefault());
        Assert.NotNull(kh.Descendants("VetaB3").SingleOrDefault());
    }

    [Fact]
    public void Reverse_Charge_Is_Due_And_Deducted_In_Summary_Export()
    {
        var exporter = new EpoXmlExporter();
        var document = exporter.ExportVatReturn(Subject(), new VatPeriod { Year = 2026, Month = 6 }, new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.ReverseCharge,
                EvidenceNumber = "KJH0NHOS-0001",
                TaxableSupplyDate = new DateOnly(2026, 6, 11),
                TaxBaseCzk = 412.40m
            }
        });

        var veta6 = document.Descendants("Veta6").Single();
        Assert.Equal("87", veta6.Attribute("dan_zocelk")?.Value);
        Assert.Equal("87", veta6.Attribute("odp_zocelk")?.Value);
        Assert.Equal("0", veta6.Attribute("dano_da")?.Value);
    }

    [Fact]
    public void Keeps_Imported_B3_Summary_As_B3_Even_Above_Detail_Limit()
    {
        var exporter = new EpoXmlExporter();
        var document = exporter.ExportControlStatement(Subject(), new VatPeriod { Year = 2026, Month = 6 }, new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.ReceivedDomesticWithVat,
                EvidenceNumber = "B3",
                TaxableSupplyDate = new DateOnly(2026, 6, 30),
                TaxBaseCzk = 12_000m,
                VatCzk = 2_520m
            }
        });

        Assert.NotNull(document.Descendants("VetaB3").SingleOrDefault());
        Assert.Empty(document.Descendants("VetaB2"));
    }

    [Fact]
    public void Exports_Partial_Deduction_Flag_To_B2()
    {
        var exporter = new EpoXmlExporter();
        var document = exporter.ExportControlStatement(Subject(), new VatPeriod { Year = 2026, Month = 6 }, new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.ReceivedDomesticWithVat,
                EvidenceNumber = "INV-POMER",
                CounterpartyDic = "CZ12345678",
                TaxableSupplyDate = new DateOnly(2026, 6, 30),
                TaxBaseCzk = 20_000m,
                VatCzk = 2_100m,
                PartialDeduction = true
            }
        });

        var vetaB2 = document.Descendants("VetaB2").Single();
        Assert.Equal("A", vetaB2.Attribute("pomer")?.Value);
    }

    private static TaxSubject Subject() => new()
    {
        Dic = "7503012671",
        FirstName = "Bohdan",
        LastName = "Koudelka",
        Street = "Jíkev",
        HouseNumber = "205",
        City = "Oskořínek",
        PostalCode = "28932",
        Country = "Česká Republika",
        TaxOfficeCode = "452",
        WorkplaceCode = "2118"
    };
}
