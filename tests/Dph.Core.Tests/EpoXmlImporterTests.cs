using Dph.Core.Domain;
using Dph.Core.Epo;

namespace Dph.Core.Tests;

public sealed class EpoXmlImporterTests
{
    [Fact]
    public void Imports_Period_Counterparties_And_Invoice_Rows_From_Control_Statement()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="UTF-8"?>
            <Pisemnost>
              <DPHKH1 verzePis="03.01">
                <VetaD d_poddp="20.06.2026" dokument="KH1" k_uladis="DPH" khdph_forma="B" mesic="5" rok="2026"/>
                <VetaP dic="7503012671" jmeno="Bohdan" prijmeni="Koudelka" ulice="Jíkev" c_pop="205" naz_obce="Oskořínek" psc="28932"/>
                <VetaA4 c_evid_dd="20260005" c_radku="1" dan1="2100" dic_odb="27082440" dppd="31.05.2026" zakl_dane1="10000" />
                <VetaB2 c_evid_dd="4007482971" c_radku="1" dan1="754.79" dic_dod="27082440" dppd="12.05.2026" zakl_dane1="3594.21" />
                <VetaB3 dan1="210" zakl_dane1="1000"/>
              </DPHKH1>
            </Pisemnost>
            """);

        var imported = new ImportedEpoData();
        new EpoXmlImporter().ImportFile(path, imported);

        var period = Assert.Single(imported.Periods);
        Assert.Equal(2026, period.Period.Year);
        Assert.Equal(5, period.Period.Month);
        Assert.Equal(3, period.Invoices.Count);
        Assert.Contains(period.Invoices, x => x.Kind == InvoiceKind.IssuedDomestic && x.EvidenceNumber == "20260005");
        Assert.Contains(period.Invoices, x => x.Kind == InvoiceKind.ReceivedDomesticWithVat && x.EvidenceNumber == "4007482971");
        Assert.Contains(period.Invoices, x => x.EvidenceNumber == "B3");
        Assert.Equal("27082440", imported.Counterparties["CZ27082440"].Ico);
    }

    [Fact]
    public void Infers_Vat_Rate_From_Base_And_Vat_Amounts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="UTF-8"?>
            <Pisemnost>
              <DPHKH1 verzePis="03.01">
                <VetaD d_poddp="20.06.2026" dokument="KH1" k_uladis="DPH" khdph_forma="B" mesic="5" rok="2026"/>
                <VetaB2 c_evid_dd="STD" dan1="2100" dic_dod="27082440" dppd="12.05.2026" zakl_dane1="10000" />
                <VetaB2 c_evid_dd="RED" dan1="1200" dic_dod="27082440" dppd="12.05.2026" zakl_dane1="10000" />
              </DPHKH1>
            </Pisemnost>
            """);

        var imported = new ImportedEpoData();
        new EpoXmlImporter().ImportFile(path, imported);

        var invoices = Assert.Single(imported.Periods).Invoices;
        Assert.Equal(VatRateKind.Standard21, invoices.Single(x => x.EvidenceNumber == "STD").VatRate);
        Assert.Equal(VatRateKind.Reduced12, invoices.Single(x => x.EvidenceNumber == "RED").VatRate);
    }
}
