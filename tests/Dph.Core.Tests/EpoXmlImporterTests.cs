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

    [Fact]
    public void Imports_Second_Rate_Columns_As_Separate_Reduced_Rate_Lines()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="UTF-8"?>
            <Pisemnost>
              <DPHKH1 verzePis="03.01">
                <VetaD d_poddp="20.06.2026" dokument="KH1" k_uladis="DPH" khdph_forma="B" mesic="5" rok="2026"/>
                <VetaB2 c_evid_dd="MIX" dan1="2100" dan2="600" dic_dod="27082440" dppd="12.05.2026" pomer="A" zakl_dane1="10000" zakl_dane2="5000" />
              </DPHKH1>
            </Pisemnost>
            """);

        var imported = new ImportedEpoData();
        new EpoXmlImporter().ImportFile(path, imported);

        var invoices = Assert.Single(imported.Periods).Invoices;
        Assert.Equal(2, invoices.Count);
        var standard = invoices.Single(x => x.VatRate == VatRateKind.Standard21);
        Assert.Equal(10_000m, standard.TaxBaseCzk);
        Assert.Equal(2_100m, standard.VatCzk);
        Assert.True(standard.PartialDeduction);
        var reduced = invoices.Single(x => x.VatRate == VatRateKind.Reduced12);
        Assert.Equal(5_000m, reduced.TaxBaseCzk);
        Assert.Equal(600m, reduced.VatCzk);
    }

    [Fact]
    public void Imports_Section_A2_As_Reverse_Charge_Lines()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="UTF-8"?>
            <Pisemnost>
              <DPHKH1 verzePis="03.01">
                <VetaD d_poddp="20.06.2026" dokument="KH1" k_uladis="DPH" khdph_forma="B" mesic="5" rok="2026"/>
                <VetaA2 c_evid_dd="OPENAI-IE" c_radku="1" dan1="87" dppd="25.05.2026" k_stat="IE" vatid_dod="4143435AH" zakl_dane1="412.4"/>
                <VetaA2 c_evid_dd="ANTHROPIC" c_radku="2" dan1="95" dppd="11.05.2026" zakl_dane1="450"/>
              </DPHKH1>
            </Pisemnost>
            """);

        var imported = new ImportedEpoData();
        new EpoXmlImporter().ImportFile(path, imported);

        var invoices = Assert.Single(imported.Periods).Invoices;
        Assert.Equal(2, invoices.Count);
        Assert.All(invoices, x => Assert.Equal(InvoiceKind.ReverseCharge, x.Kind));
        var eu = invoices.Single(x => x.EvidenceNumber == "OPENAI-IE");
        Assert.Equal("IE4143435AH", eu.CounterpartyDic);
        Assert.Equal(412.4m, eu.TaxBaseCzk);
        var thirdCountry = invoices.Single(x => x.EvidenceNumber == "ANTHROPIC");
        Assert.Null(thirdCountry.CounterpartyDic);

        // EU dodavatel se založí do adresáře; třetí země bez VAT ID ne.
        Assert.Equal("IE", imported.Counterparties["IE4143435AH"].CountryCode);
        Assert.Single(imported.Counterparties);
    }
}
