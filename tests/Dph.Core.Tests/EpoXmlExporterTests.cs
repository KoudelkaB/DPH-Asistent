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
    public void Foreign_Service_Reverse_Charge_Maps_To_Rows_12_13_And_43_44_And_Stays_Out_Of_Kh()
    {
        var exporter = new EpoXmlExporter();
        var subject = Subject();
        var period = new VatPeriod { Year = 2026, Month = 6 };
        // Přijetí služby od osoby neusazené v tuzemsku (např. Anthropic), §108.
        var invoices = new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.ReverseCharge,
                EvidenceNumber = "OJWGTKQQ-0001",
                TaxableSupplyDate = new DateOnly(2026, 6, 11),
                TaxBaseCzk = 450m
            }
        };

        var dph = exporter.ExportVatReturn(subject, period, invoices);
        var veta1 = dph.Descendants("Veta1").Single();
        Assert.Equal("450", veta1.Attribute("p_sl23_z")?.Value);   // ř.12 základ
        Assert.Equal("95", veta1.Attribute("dan_psl23_z")?.Value); // ř.12 daň (450*0,21=94,5 -> 95)
        var veta4 = dph.Descendants("Veta4").Single();
        Assert.Equal("450", veta4.Attribute("nar_zdp23")?.Value);  // ř.43 základ
        Assert.Equal("95", veta4.Attribute("od_zdp23")?.Value);    // ř.43 odpočet
        Assert.Equal("95", veta4.Attribute("odp_sum_nar")?.Value); // ř.46 součet
        // Nesmí skončit na neplatném ř.10/11 (Veta2) ani v kontrolním hlášení.
        Assert.Empty(dph.Descendants("Veta2"));

        var kh = exporter.ExportControlStatement(subject, period, invoices);
        Assert.Empty(kh.Descendants("VetaB1"));
        Assert.Empty(kh.Descendants("VetaB2"));
    }

    [Fact]
    public void Eu_Registered_Supplier_Reverse_Charge_Maps_To_Rows_5_6_Not_12_13()
    {
        var exporter = new EpoXmlExporter();
        // OpenAI Ireland (IE VAT) – osoba registrovaná v JČS, §9(1) → ř.5/6.
        var dph = exporter.ExportVatReturn(Subject(), new VatPeriod { Year = 2026, Month = 5 }, new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.ReverseCharge,
                EvidenceNumber = "OPENAI-IE",
                CounterpartyDic = "IE4143435AH",
                TaxableSupplyDate = new DateOnly(2026, 5, 25),
                TaxBaseCzk = 412.40m
            }
        });

        var veta1 = dph.Descendants("Veta1").Single();
        Assert.Equal("412", veta1.Attribute("p_sl23_e")?.Value);   // ř.5 základ
        Assert.Equal("87", veta1.Attribute("dan_psl23_e")?.Value); // ř.5 daň
        Assert.Null(veta1.Attribute("p_sl23_z"));                  // NEpatří na ř.12
        var veta4 = dph.Descendants("Veta4").Single();
        Assert.Equal("412", veta4.Attribute("nar_zdp23")?.Value);  // ř.43 (EU i třetí země)
        Assert.Equal("87", veta4.Attribute("od_zdp23")?.Value);
    }

    [Fact]
    public void Reverse_Charge_Vat_Is_Computed_From_Reported_Whole_Crown_Bases()
    {
        var exporter = new EpoXmlExporter();
        var dph = exporter.ExportVatReturn(Subject(), new VatPeriod { Year = 2026, Month = 5 }, new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.ReverseCharge,
                EvidenceNumber = "KJH0NHOS-0001",
                CounterpartyDic = "IE4143435AH",
                TaxableSupplyDate = new DateOnly(2026, 5, 25),
                TaxBaseCzk = 412.40m,
                VatCzk = 86.60m
            },
            new InvoiceLine
            {
                Kind = InvoiceKind.ReverseCharge,
                EvidenceNumber = "ch_3TWLDZJF",
                TaxableSupplyDate = new DateOnly(2026, 5, 12),
                TaxBaseCzk = 207.14m,
                VatCzk = 43.50m
            }
        });

        var veta1 = dph.Descendants("Veta1").Single();
        Assert.Equal("412", veta1.Attribute("p_sl23_e")?.Value);
        Assert.Equal("87", veta1.Attribute("dan_psl23_e")?.Value);
        Assert.Equal("207", veta1.Attribute("p_sl23_z")?.Value);
        Assert.Equal("43", veta1.Attribute("dan_psl23_z")?.Value);

        var veta4 = dph.Descendants("Veta4").Single();
        Assert.Equal("619", veta4.Attribute("nar_zdp23")?.Value);
        Assert.Equal("130", veta4.Attribute("od_zdp23")?.Value);
        Assert.Equal("130", veta4.Attribute("odp_sum_nar")?.Value);

        var veta6 = dph.Descendants("Veta6").Single();
        Assert.Equal("130", veta6.Attribute("dan_zocelk")?.Value);
        Assert.Equal("130", veta6.Attribute("odp_zocelk")?.Value);
        Assert.Equal("0", veta6.Attribute("dano_da")?.Value);
    }

    [Fact]
    public void Reduced_Rate_Foreign_Reverse_Charge_Uses_Rows_13_And_44()
    {
        var exporter = new EpoXmlExporter();
        var dph = exporter.ExportVatReturn(Subject(), new VatPeriod { Year = 2026, Month = 6 }, new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.ReverseCharge,
                EvidenceNumber = "RC-RED",
                TaxableSupplyDate = new DateOnly(2026, 6, 11),
                TaxBaseCzk = 1_000m,
                VatRate = VatRateKind.Reduced12
            }
        });

        var veta1 = dph.Descendants("Veta1").Single();
        Assert.Equal("1000", veta1.Attribute("p_sl5_z")?.Value);
        Assert.Equal("120", veta1.Attribute("dan_psl5_z")?.Value);
        Assert.Null(veta1.Attribute("p_sl23_z"));
        var veta4 = dph.Descendants("Veta4").Single();
        Assert.Equal("1000", veta4.Attribute("nar_zdp5")?.Value);
        Assert.Equal("120", veta4.Attribute("od_zdp5")?.Value);
    }

    [Fact]
    public void Standard_Rate_Deduction_Does_Not_Emit_Lone_Reduced_Rate_Value()
    {
        // ř.41 (snížená sazba) se nesmí objevit jako osamocená hodnota – EPO hlásí kód 48.
        var exporter = new EpoXmlExporter();
        var document = exporter.ExportVatReturn(Subject(), new VatPeriod { Year = 2026, Month = 6 }, new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.ReceivedDomesticWithVat,
                EvidenceNumber = "IN-21",
                TaxableSupplyDate = new DateOnly(2026, 6, 11),
                TaxBaseCzk = 1_000m,
                VatCzk = 210m
            }
        });

        var veta4 = document.Descendants("Veta4").Single();
        Assert.Equal("1000", veta4.Attribute("pln23")?.Value);
        Assert.Equal("210", veta4.Attribute("odp_tuz23_nar")?.Value);
        Assert.Null(veta4.Attribute("pln5"));
        Assert.Null(veta4.Attribute("odp_tuz5_nar"));
    }

    [Fact]
    public void Reduced_Rate_Domestic_Deduction_Maps_To_Row_41_As_A_Pair()
    {
        var exporter = new EpoXmlExporter();
        var document = exporter.ExportVatReturn(Subject(), new VatPeriod { Year = 2026, Month = 6 }, new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.ReceivedDomesticWithVat,
                EvidenceNumber = "IN-12",
                TaxableSupplyDate = new DateOnly(2026, 6, 11),
                TaxBaseCzk = 1_000m,
                VatCzk = 120m,
                VatRate = VatRateKind.Reduced12
            }
        });

        var veta4 = document.Descendants("Veta4").Single();
        Assert.Equal("1000", veta4.Attribute("pln5")?.Value);
        Assert.Equal("120", veta4.Attribute("odp_tuz5_nar")?.Value);
        Assert.Null(veta4.Attribute("pln23"));
        Assert.Equal("120", veta4.Attribute("odp_sum_nar")?.Value);
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

    [Fact]
    public void Net_Tax_Equals_Difference_Of_Reported_Whole_Crown_Lines()
    {
        // Output VAT 1.40, input VAT 0.50: rounding each line independently gives 1 and 1.
        // The declared net must be 1 - 1 = 0, not WholeCrowns(0.90) = 1 (which EPO would reject).
        var exporter = new EpoXmlExporter();
        var document = exporter.ExportVatReturn(Subject(), new VatPeriod { Year = 2026, Month = 6 }, new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.IssuedDomestic,
                EvidenceNumber = "OUT-1",
                TaxableSupplyDate = new DateOnly(2026, 6, 10),
                TaxBaseCzk = 6.67m,
                VatCzk = 1.40m
            },
            new InvoiceLine
            {
                Kind = InvoiceKind.ReceivedDomesticWithVat,
                EvidenceNumber = "IN-1",
                TaxableSupplyDate = new DateOnly(2026, 6, 11),
                TaxBaseCzk = 2.38m,
                VatCzk = 0.50m
            }
        });

        var veta6 = document.Descendants("Veta6").Single();
        var due = int.Parse(veta6.Attribute("dan_zocelk")!.Value);
        var deduction = int.Parse(veta6.Attribute("odp_zocelk")!.Value);
        Assert.Equal(due - deduction, int.Parse(veta6.Attribute("dano_da")!.Value));
        Assert.Equal("0", veta6.Attribute("dano_da")!.Value);
    }

    [Fact]
    public void Corrective_Form_Type_Sets_Opravne_Flag_On_Both_Documents()
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
                TaxBaseCzk = 1_000m,
                VatCzk = 210m
            }
        };

        var dph = exporter.ExportVatReturn(subject, period, invoices, "O");
        var kh = exporter.ExportControlStatement(subject, period, invoices, "O");

        Assert.Equal("O", dph.Descendants("VetaD").Single().Attribute("dapdph_forma")?.Value);
        Assert.Equal("O", kh.Descendants("VetaD").Single().Attribute("khdph_forma")?.Value);
    }

    [Fact]
    public void Defaults_To_Regular_Form_Type_B()
    {
        var exporter = new EpoXmlExporter();
        var period = new VatPeriod { Year = 2026, Month = 5 };
        var dph = exporter.ExportVatReturn(Subject(), period, System.Array.Empty<InvoiceLine>());

        Assert.Equal("B", dph.Descendants("VetaD").Single().Attribute("dapdph_forma")?.Value);
    }

    [Fact]
    public void Net_Tax_Whole_Crowns_Matches_Declared_Liability()
    {
        var exporter = new EpoXmlExporter();
        var period = new VatPeriod { Year = 2026, Month = 6 };
        var invoices = new[]
        {
            new InvoiceLine { Kind = InvoiceKind.IssuedDomestic, EvidenceNumber = "O1", TaxableSupplyDate = new DateOnly(2026, 6, 10), TaxBaseCzk = 10_000m, VatCzk = 2_100m },
            new InvoiceLine { Kind = InvoiceKind.ReceivedDomesticWithVat, EvidenceNumber = "I1", TaxableSupplyDate = new DateOnly(2026, 6, 11), TaxBaseCzk = 1_000m, VatCzk = 210m }
        };

        var net = exporter.ComputeNetTaxWholeCrowns(invoices);
        var veta6 = exporter.ExportVatReturn(Subject(), period, invoices).Descendants("Veta6").Single();

        Assert.Equal(1_890, net);
        Assert.Equal("1890", veta6.Attribute("dano_da")?.Value);
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
