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
    public void Foreign_Service_Reverse_Charge_Maps_To_Rows_12_13_And_43_44_And_Kh_Section_A2()
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
        // Nesmí skončit na neplatném ř.10/11 (Veta2).
        Assert.Empty(dph.Descendants("Veta2"));

        // V KH patří do oddílu A.2 (ne B.1/B.2); třetí země bez EU DIČ má prázdnou identifikaci.
        var kh = exporter.ExportControlStatement(subject, period, invoices);
        var vetaA2 = kh.Descendants("VetaA2").Single();
        Assert.Equal("", vetaA2.Attribute("k_stat")?.Value);
        Assert.Equal("", vetaA2.Attribute("vatid_dod")?.Value);
        Assert.Equal("OJWGTKQQ-0001", vetaA2.Attribute("c_evid_dd")?.Value);
        Assert.Equal("450", vetaA2.Attribute("zakl_dane1")?.Value);
        Assert.Equal("94.5", vetaA2.Attribute("dan1")?.Value);
        Assert.Equal("450", kh.Descendants("VetaC").Single().Attribute("celk_zd_a2")?.Value);
        Assert.Empty(kh.Descendants("VetaB1"));
        Assert.Empty(kh.Descendants("VetaB2"));
    }

    [Fact]
    public void Eu_Supplier_Reverse_Charge_Splits_Vat_Id_In_Kh_Section_A2()
    {
        var exporter = new EpoXmlExporter();
        var kh = exporter.ExportControlStatement(Subject(), new VatPeriod { Year = 2026, Month = 5 }, new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.ReverseCharge,
                EvidenceNumber = "OPENAI-IE",
                CounterpartyDic = "IE4143435AH",
                TaxableSupplyDate = new DateOnly(2026, 5, 25),
                TaxBaseCzk = 412.40m,
                VatCzk = 86.60m
            }
        });

        var vetaA2 = kh.Descendants("VetaA2").Single();
        Assert.Equal("IE", vetaA2.Attribute("k_stat")?.Value);
        Assert.Equal("4143435AH", vetaA2.Attribute("vatid_dod")?.Value);
        Assert.Equal("412.4", vetaA2.Attribute("zakl_dane1")?.Value);
        Assert.Equal("86.6", vetaA2.Attribute("dan1")?.Value);
    }

    [Fact]
    public void Reduced_Rate_Supplies_Use_Second_Rate_Columns_In_Kh()
    {
        var exporter = new EpoXmlExporter();
        var kh = exporter.ExportControlStatement(Subject(), new VatPeriod { Year = 2026, Month = 6 }, new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.IssuedDomestic,
                EvidenceNumber = "20260007",
                CounterpartyDic = "CZ61506133",
                TaxableSupplyDate = new DateOnly(2026, 6, 30),
                TaxBaseCzk = 20_000m,
                VatCzk = 2_400m,
                VatRate = VatRateKind.Reduced12
            },
            new InvoiceLine
            {
                Kind = InvoiceKind.ReceivedDomesticWithVat,
                EvidenceNumber = "FV-12",
                CounterpartyDic = "CZ27082440",
                TaxableSupplyDate = new DateOnly(2026, 6, 12),
                TaxBaseCzk = 1_000m,
                VatCzk = 120m,
                VatRate = VatRateKind.Reduced12
            }
        });

        var vetaA4 = kh.Descendants("VetaA4").Single();
        Assert.Null(vetaA4.Attribute("zakl_dane1"));
        Assert.Equal("20000", vetaA4.Attribute("zakl_dane2")?.Value);
        Assert.Equal("2400", vetaA4.Attribute("dan2")?.Value);

        var vetaB3 = kh.Descendants("VetaB3").Single();
        Assert.Null(vetaB3.Attribute("zakl_dane1"));
        Assert.Equal("1000", vetaB3.Attribute("zakl_dane2")?.Value);
        Assert.Equal("120", vetaB3.Attribute("dan2")?.Value);

        // Kontrolní součty musí sedět na ř.2/ř.41 přiznání (snížená sazba), ne na ř.1/ř.40.
        var vetaC = kh.Descendants("VetaC").Single();
        Assert.Equal("0", vetaC.Attribute("obrat23")?.Value);
        Assert.Equal("20000", vetaC.Attribute("obrat5")?.Value);
        Assert.Equal("0", vetaC.Attribute("pln23")?.Value);
        Assert.Equal("1000", vetaC.Attribute("pln5")?.Value);
    }

    [Fact]
    public void Multi_Rate_Document_Is_One_Kh_Row_And_Limit_Applies_To_Whole_Document()
    {
        // Faktura 20260009 má dvě sazby (dva řádky tabulky): 6 000+1 260 a 3 000+360. Jednotlivé
        // řádky jsou pod 10 000 Kč, ale doklad jako celek (10 620 Kč) limit překračuje – musí být
        // v A.4 jako jeden řádek se sloupci obou sazeb, ne rozpadnutý do A.5.
        var exporter = new EpoXmlExporter();
        var kh = exporter.ExportControlStatement(Subject(), new VatPeriod { Year = 2026, Month = 6 }, new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.IssuedDomestic,
                EvidenceNumber = "20260009",
                CounterpartyDic = "CZ61506133",
                TaxableSupplyDate = new DateOnly(2026, 6, 30),
                TaxBaseCzk = 6_000m,
                VatCzk = 1_260m
            },
            new InvoiceLine
            {
                Kind = InvoiceKind.IssuedDomestic,
                EvidenceNumber = "20260009",
                CounterpartyDic = "CZ61506133",
                TaxableSupplyDate = new DateOnly(2026, 6, 30),
                TaxBaseCzk = 3_000m,
                VatCzk = 360m,
                VatRate = VatRateKind.Reduced12
            }
        });

        var vetaA4 = kh.Descendants("VetaA4").Single();
        Assert.Equal("6000", vetaA4.Attribute("zakl_dane1")?.Value);
        Assert.Equal("1260", vetaA4.Attribute("dan1")?.Value);
        Assert.Equal("3000", vetaA4.Attribute("zakl_dane2")?.Value);
        Assert.Equal("360", vetaA4.Attribute("dan2")?.Value);
        Assert.Empty(kh.Descendants("VetaA5"));
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
    public void Mixed_Eu_And_Third_Country_Reverse_Charge_Cancels_In_Net_Tax()
    {
        // Reprodukce hlášené chyby: reverse charge (EU 412,40 + třetí země 208,14) se musí ve
        // vlastní dani vyrušit. Daň po řádcích: 87 + 44 = 131; odpočet ř.43 musí být také 131,
        // ne round(sloučený_základ 620 × 0,21) = 130 – jinak dano vyjde o 1 Kč špatně.
        var exporter = new EpoXmlExporter();
        var dph = exporter.ExportVatReturn(Subject(), new VatPeriod { Year = 2026, Month = 4 }, new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.ReverseCharge, EvidenceNumber = "IE-1", CounterpartyDic = "IE4143435AH",
                TaxableSupplyDate = new DateOnly(2026, 4, 30), TaxBaseCzk = 412.40m, VatCzk = 86.60m
            },
            new InvoiceLine
            {
                Kind = InvoiceKind.ReverseCharge, EvidenceNumber = "US-1",
                TaxableSupplyDate = new DateOnly(2026, 4, 30), TaxBaseCzk = 208.14m, VatCzk = 43.71m
            }
        });

        var veta1 = dph.Descendants("Veta1").Single();
        Assert.Equal("87", veta1.Attribute("dan_psl23_e")?.Value);
        Assert.Equal("44", veta1.Attribute("dan_psl23_z")?.Value);

        // ř.43: základ = 412 + 208, daň = 87 + 44 (součet výstupu), ne round(620 × 0,21) = 130.
        var veta4 = dph.Descendants("Veta4").Single();
        Assert.Equal("620", veta4.Attribute("nar_zdp23")?.Value);
        Assert.Equal("131", veta4.Attribute("od_zdp23")?.Value);
        Assert.Equal("131", veta4.Attribute("odp_sum_nar")?.Value);

        var veta6 = dph.Descendants("Veta6").Single();
        Assert.Equal("131", veta6.Attribute("dan_zocelk")?.Value);
        Assert.Equal("131", veta6.Attribute("odp_zocelk")?.Value);
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

    [Fact]
    public void Supplementary_Return_Reports_Only_Differences_And_Row_66()
    {
        var exporter = new EpoXmlExporter();
        var period = new VatPeriod { Year = 2026, Month = 5, SubmissionDate = new DateOnly(2026, 7, 3) };
        // Beze změny oproti poslednímu podání – v dodatečném přiznání se nesmí objevit.
        var unchangedReceived = new InvoiceLine
        {
            Kind = InvoiceKind.ReceivedDomesticWithVat,
            EvidenceNumber = "IN-1",
            CounterpartyDic = "CZ27082440",
            TaxableSupplyDate = new DateOnly(2026, 5, 12),
            TaxBaseCzk = 1_000m,
            VatCzk = 210m
        };
        var lastKnown = new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.IssuedDomestic,
                EvidenceNumber = "20260005",
                CounterpartyDic = "CZ61506133",
                TaxableSupplyDate = new DateOnly(2026, 5, 31),
                TaxBaseCzk = 100_000m,
                VatCzk = 21_000m
            },
            unchangedReceived
        };
        var current = new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.IssuedDomestic,
                EvidenceNumber = "20260005",
                CounterpartyDic = "CZ61506133",
                TaxableSupplyDate = new DateOnly(2026, 5, 31),
                TaxBaseCzk = 127_449m,
                VatCzk = 26_764.29m
            },
            unchangedReceived
        };

        // Poslední známá daň = hodnoty skutečně vykázané v naposledy podaném (řádném) přiznání.
        var lastKnownReturn = exporter.ExportVatReturn(Subject(), period, lastKnown);
        var dph = exporter.ExportVatReturn(Subject(), period, current, "D", [lastKnownReturn]);

        var vetaD = dph.Descendants("VetaD").Single();
        Assert.Equal("D", vetaD.Attribute("dapdph_forma")?.Value);
        Assert.Equal("03.07.2026", vetaD.Attribute("d_zjist")?.Value);

        // ř.1 jen rozdíl základu a daně; nezměněný odpočet (ř.40) se vůbec nevykazuje.
        var veta1 = dph.Descendants("Veta1").Single();
        Assert.Equal("27449", veta1.Attribute("obrat23")?.Value);
        Assert.Equal("5764", veta1.Attribute("dan23")?.Value);
        Assert.Empty(dph.Descendants("Veta4"));

        // ř.62/63 rozdíly, ř.66 = rozdíl oproti poslední známé dani; ř.64/65 se nevyplňují.
        var veta6 = dph.Descendants("Veta6").Single();
        Assert.Equal("5764", veta6.Attribute("dan_zocelk")?.Value);
        Assert.Equal("0", veta6.Attribute("odp_zocelk")?.Value);
        Assert.Equal("5764", veta6.Attribute("dano")?.Value);
        Assert.Null(veta6.Attribute("dano_da"));
        Assert.Null(veta6.Attribute("dano_no"));
    }

    [Fact]
    public void Supplementary_Return_For_Lower_Tax_Reports_Negative_Difference()
    {
        var exporter = new EpoXmlExporter();
        var period = new VatPeriod { Year = 2026, Month = 5, SubmissionDate = new DateOnly(2026, 7, 3) };
        var lastKnown = new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.IssuedDomestic,
                EvidenceNumber = "20260005",
                TaxableSupplyDate = new DateOnly(2026, 5, 31),
                TaxBaseCzk = 10_000m,
                VatCzk = 2_100m
            }
        };
        var current = new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.IssuedDomestic,
                EvidenceNumber = "20260005",
                TaxableSupplyDate = new DateOnly(2026, 5, 31),
                TaxBaseCzk = 8_000m,
                VatCzk = 1_680m
            }
        };

        var lastKnownReturn = exporter.ExportVatReturn(Subject(), period, lastKnown);
        var veta6 = exporter.ExportVatReturn(Subject(), period, current, "D", [lastKnownReturn])
            .Descendants("Veta6").Single();
        Assert.Equal("-420", veta6.Attribute("dan_zocelk")?.Value);
        Assert.Equal("-420", veta6.Attribute("dano")?.Value);
    }

    [Fact]
    public void Supplementary_Return_With_Unchanged_Invoices_Is_All_Zero()
    {
        // Reprodukce hlášeného problému: dodatečné přiznání beze změny plnění (vč. reverse charge)
        // musí vykázat samé nuly, ne rozdíl 130 = 87+43. Rozdíl se počítá proti hodnotám skutečně
        // vykázaným v podaném DP, ne proti rekonstrukci z aktuální logiky.
        var exporter = new EpoXmlExporter();
        var period = new VatPeriod { Year = 2026, Month = 5, SubmissionDate = new DateOnly(2026, 7, 3) };
        var invoices = new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.ReverseCharge, EvidenceNumber = "IE-1", CounterpartyDic = "IE4143435AH",
                TaxableSupplyDate = new DateOnly(2026, 5, 25), TaxBaseCzk = 412.40m, VatCzk = 86.60m
            },
            new InvoiceLine
            {
                Kind = InvoiceKind.ReverseCharge, EvidenceNumber = "US-1",
                TaxableSupplyDate = new DateOnly(2026, 5, 12), TaxBaseCzk = 207.14m, VatCzk = 43.50m
            },
            new InvoiceLine
            {
                Kind = InvoiceKind.IssuedDomestic, EvidenceNumber = "20260005", CounterpartyDic = "CZ61506133",
                TaxableSupplyDate = new DateOnly(2026, 5, 31), TaxBaseCzk = 127_449m, VatCzk = 26_764.29m
            }
        };

        var lastKnownReturn = exporter.ExportVatReturn(Subject(), period, invoices);
        var dph = exporter.ExportVatReturn(Subject(), period, invoices, "D", [lastKnownReturn]);

        // Žádný řádek plnění/odpočtu se nevykazuje – všechny rozdíly jsou nulové.
        Assert.Empty(dph.Descendants("Veta1"));
        Assert.Empty(dph.Descendants("Veta2"));
        Assert.Empty(dph.Descendants("Veta4"));
        var veta6 = dph.Descendants("Veta6").Single();
        Assert.Equal("0", veta6.Attribute("dan_zocelk")?.Value);
        Assert.Equal("0", veta6.Attribute("odp_zocelk")?.Value);
        Assert.Equal("0", veta6.Attribute("dano")?.Value);
    }

    [Fact]
    public void Supplementary_Return_Adds_Differences_From_Earlier_Supplementary()
    {
        // Poslední známá daň = řádné + již podané dodatečné. Druhé dodatečné počítá rozdíl proti
        // jejich součtu, takže dva po sobě jdoucí přírůstky +1000 skončí u správné hodnoty.
        var exporter = new EpoXmlExporter();
        var period = new VatPeriod { Year = 2026, Month = 5, SubmissionDate = new DateOnly(2026, 7, 3) };
        InvoiceLine Issued(decimal baseCzk) => new()
        {
            Kind = InvoiceKind.IssuedDomestic, EvidenceNumber = "F1", CounterpartyDic = "CZ61506133",
            TaxableSupplyDate = new DateOnly(2026, 5, 31), TaxBaseCzk = baseCzk, VatCzk = baseCzk * 0.21m
        };

        var regular = exporter.ExportVatReturn(Subject(), period, [Issued(10_000m)]);
        var firstSupplementary = exporter.ExportVatReturn(Subject(), period, [Issued(11_000m)], "D", [regular]);
        var secondSupplementary = exporter.ExportVatReturn(Subject(), period, [Issued(12_000m)], "D", [regular, firstSupplementary]);

        // První dodatečné: rozdíl +1000 základ / +210 daň.
        Assert.Equal("1000", firstSupplementary.Descendants("Veta1").Single().Attribute("obrat23")?.Value);
        // Druhé dodatečné: rozdíl proti 11 000 (řádné 10 000 + dodatečné +1 000), tedy zase jen +1000.
        Assert.Equal("1000", secondSupplementary.Descendants("Veta1").Single().Attribute("obrat23")?.Value);
        Assert.Equal("210", secondSupplementary.Descendants("Veta6").Single().Attribute("dano")?.Value);
    }

    [Fact]
    public void Supplementary_Return_Requires_Last_Known_State()
    {
        var exporter = new EpoXmlExporter();
        Assert.Throws<ArgumentException>(() =>
            exporter.ExportVatReturn(Subject(), new VatPeriod { Year = 2026, Month = 5 }, [], "D"));
    }

    [Fact]
    public void Follow_Up_Control_Statement_Has_Discovery_Date_And_Full_Data()
    {
        var exporter = new EpoXmlExporter();
        var period = new VatPeriod { Year = 2026, Month = 5, SubmissionDate = new DateOnly(2026, 7, 3) };
        var kh = exporter.ExportControlStatement(Subject(), period, new[]
        {
            new InvoiceLine
            {
                Kind = InvoiceKind.IssuedDomestic,
                EvidenceNumber = "20260005",
                CounterpartyDic = "CZ61506133",
                TaxableSupplyDate = new DateOnly(2026, 5, 31),
                TaxBaseCzk = 127_449m,
                VatCzk = 26_764.29m
            }
        }, "N");

        var vetaD = kh.Descendants("VetaD").Single();
        Assert.Equal("N", vetaD.Attribute("khdph_forma")?.Value);
        Assert.Equal("03.07.2026", vetaD.Attribute("d_zjist")?.Value);
        // Následné KH nese kompletní data, ne rozdíly.
        Assert.Equal("127449", kh.Descendants("VetaA4").Single().Attribute("zakl_dane1")?.Value);
    }

    [Fact]
    public void Regular_Forms_Do_Not_Carry_Discovery_Date()
    {
        var exporter = new EpoXmlExporter();
        var period = new VatPeriod { Year = 2026, Month = 5, SubmissionDate = new DateOnly(2026, 6, 20) };

        Assert.Null(exporter.ExportVatReturn(Subject(), period, []).Descendants("VetaD").Single().Attribute("d_zjist"));
        Assert.Null(exporter.ExportControlStatement(Subject(), period, [], "O").Descendants("VetaD").Single().Attribute("d_zjist"));
    }

    [Fact]
    public void Empty_Street_Is_Exported_As_Empty_Attribute()
    {
        // Obec bez uliční sítě (např. Jíkev 205) má v registraci u FÚ ulici prázdnou; prázdné
        // pole Ulice se exportuje jako prázdný atribut a EPO ho bere jako nevyplněné (kód 116
        // by naopak hlásilo odeslání názvu obce v poli ulice).
        var exporter = new EpoXmlExporter();
        var subject = Subject();
        subject.Street = "";
        subject.City = "Jíkev";

        var vetaP = exporter.ExportVatReturn(subject, new VatPeriod { Year = 2026, Month = 5 }, [])
            .Descendants("VetaP").Single();
        Assert.Equal("", vetaP.Attribute("ulice")?.Value);
        Assert.Equal("Jíkev", vetaP.Attribute("naz_obce")?.Value);
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
