using Dph.Core.Calculations;
using Dph.Core.Domain;
using Dph.Core.Epo;
using System.Xml.Linq;

namespace Dph.Core.Tests;

public sealed class TaxRegressionTests
{
    private readonly EpoXmlExporter _exporter = new();

    [Fact]
    public void Control_States_Use_Instance_Limit_And_Whole_Document()
    {
        var first = Line(InvoiceKind.ReceivedDomesticWithVat, 7000m, 1000m);
        var second = Line(InvoiceKind.ReceivedDomesticWithVat, 7000m, 1000m);
        second.VatRate = VatRateKind.Reduced12;
        var custom = new EpoXmlExporter(new() { ControlStatementDetailLimitCzk = 20000m });
        Assert.All(_exporter.ReceivedControlStatementStates([first, second]).Values, state =>
        {
            Assert.Equal(16000m, state.DocumentGrossCzk);
            Assert.True(state.IsDetail);
        });
        Assert.All(custom.ReceivedControlStatementStates([first, second]).Values, state => Assert.False(state.IsDetail));
        Assert.Single(custom.ExportControlStatement(new(), Period(), [first, second]).Descendants("VetaB3"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Exact_Limit_With_Explicit_Original_Document_Above_Limit_Is_B2(int sign)
    {
        var line = Line(InvoiceKind.ReceivedDomesticWithVat, sign * 9000m, sign * 1000m);
        line.PartialDeduction = true;
        line.DocumentAboveControlLimit = true;
        Assert.True(_exporter.ReceivedControlStatementStates([line])[line].IsDetail);
        Assert.Single(_exporter.ExportControlStatement(new(), Period(), [line]).Descendants("VetaB2"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Proportional_Claim_Below_Limit_Can_Belong_To_Large_Original_Document(int sign)
    {
        var line = Line(InvoiceKind.ReceivedDomesticWithVat, sign * 5000m, sign * 1050m);
        line.PartialDeduction = true;
        var small = _exporter.ExportControlStatement(new(), Period(), [line]);
        Assert.Single(small.Descendants("VetaB3"));
        line.PartialDeduction = false;
        Assert.True(XNode.DeepEquals(small, _exporter.ExportControlStatement(new(), Period(), [line])));
        line.PartialDeduction = true;
        var originalReturn = _exporter.ExportVatReturn(new(), Period(), [line]);

        line.DocumentAboveControlLimit = true;
        EpoXmlExporter.ValidateSupportedLines(Period(), [line]);
        var detail = Assert.Single(_exporter.ExportControlStatement(new(), Period(), [line]).Descendants("VetaB2"));
        Assert.Equal("A", detail.Attribute("pomer")?.Value);
        Assert.Equal((sign * 5000).ToString(), detail.Attribute("zakl_dane1")?.Value);
        Assert.Equal((sign * 1050).ToString(), detail.Attribute("dan1")?.Value);
        Assert.True(XNode.DeepEquals(originalReturn, _exporter.ExportVatReturn(new(), Period(), [line])));
        line.CounterpartyDic = null;
        Assert.Throws<InvalidOperationException>(() => EpoXmlExporter.ValidateSupportedLines(Period(), [line]));
    }

    [Fact]
    public void Original_Document_Limit_Applies_To_All_Rates()
    {
        var first = Line(InvoiceKind.ReceivedDomesticWithVat, 1000m, 210m);
        var second = Line(InvoiceKind.ReceivedDomesticWithVat, 1000m, 120m);
        second.VatRate = VatRateKind.Reduced12;
        first.DocumentAboveControlLimit = true;
        first.PartialDeduction = second.PartialDeduction = true;
        var kh = _exporter.ExportControlStatement(new(), Period(), [first, second]);
        var detail = Assert.Single(kh.Descendants("VetaB2"));
        Assert.Equal("1000", detail.Attribute("zakl_dane2")?.Value);
        Assert.Empty(kh.Descendants("VetaB3"));
        Assert.Equal(2, _exporter.ReceivedControlStatementStates([first, second]).Count);
    }
    private static VatPeriod Period() => new() { Year = 2026, Month = 5, SubmissionDate = new(2026, 7, 10) };
    private static InvoiceLine Line(InvoiceKind kind, decimal basis, decimal vat, string? dic = "CZ12345678", string number = "F1")
        => new() { Kind = kind, TaxBaseCzk = basis, VatCzk = vat, CounterpartyDic = dic,
            EvidenceNumber = number, TaxableSupplyDate = new(2026, 5, 20) };

    [Theory]
    [InlineData("CZ 12345678")]
    [InlineData("cz\u00a012345678")]
    [InlineData(" CZ\t1234 5678 ")]
    public void Whitespace_In_Czech_Dic_Preserves_Kh_Detail_And_Exports_Canonical_Id(string dic)
    {
        Assert.True(InvoiceKindClassifier.IsCzechDic(dic));
        var issued = Line(InvoiceKind.IssuedDomestic, 50000m, 10500m, dic);
        var received = Line(InvoiceKind.ReceivedDomesticWithVat, 50000m, 10500m, dic);
        var kh = _exporter.ExportControlStatement(new(), Period(), [issued, received]);
        Assert.Equal("12345678", kh.Descendants("VetaA4").Single().Attribute("dic_odb")?.Value);
        Assert.Equal("12345678", kh.Descendants("VetaB2").Single().Attribute("dic_dod")?.Value);
        Assert.Empty(kh.Descendants("VetaA5"));
    }

    [Fact]
    public void Preflight_Default_And_Custom_Limits_Match_Export()
    {
        var limit = EpoTaxFormDefinition.Current.ControlStatementDetailLimitCzk;
        var line = Line(InvoiceKind.ReceivedDomesticWithVat, limit, 0m, null);
        EpoXmlExporter.ValidateSupportedLines(Period(), [line]);
        EpoXmlExporter.ValidateSupportedLines(Period(), [line], null);
        Assert.Single(_exporter.ExportControlStatement(new(), Period(), [line]).Descendants("VetaB3"));

        line.TaxBaseCzk += 1m;
        Assert.Throws<InvalidOperationException>(() => EpoXmlExporter.ValidateSupportedLines(Period(), [line]));
        Assert.Throws<InvalidOperationException>(() => EpoXmlExporter.ValidateSupportedLines(Period(), [line], null));
        Assert.Throws<InvalidOperationException>(() => _exporter.ExportControlStatement(new(), Period(), [line]));

        var definition = new EpoTaxFormDefinition { ControlStatementDetailLimitCzk = limit + 1000m };
        EpoXmlExporter.ValidateSupportedLines(Period(), [line], definition.ControlStatementDetailLimitCzk);
        Assert.Single(new EpoXmlExporter(definition).ExportControlStatement(new(), Period(), [line]).Descendants("VetaB3"));
    }

    [Fact]
    public void Preflight_Uses_Whole_Received_Document_Limit()
    {
        var first = Line(InvoiceKind.ReceivedDomesticWithVat, 5000m, 1050m, null);
        var second = Line(InvoiceKind.ReceivedDomesticWithVat, 5000m, 600m, null);
        second.VatRate = VatRateKind.Reduced12;
        Assert.Throws<InvalidOperationException>(() => EpoXmlExporter.ValidateSupportedLines(Period(), [first, second]));
        Assert.Throws<InvalidOperationException>(() => _exporter.ExportVatReturn(new(), Period(), [first, second]));
        Assert.Throws<InvalidOperationException>(() => _exporter.ExportControlStatement(new(), Period(), [first, second]));
        first.EvidenceNumber = second.EvidenceNumber = "B3";
        EpoXmlExporter.ValidateSupportedLines(Period(), [first, second]);
    }

    [Theory]
    [InlineData(InvoiceKind.IssuedDomestic, "VetaA4", "VetaA5")]
    [InlineData(InvoiceKind.ReceivedDomesticWithVat, "VetaB2", "VetaB3")]
    public void Credit_Note_Uses_Absolute_Limit(InvoiceKind kind, string detail, string summary)
    {
        var kh = _exporter.ExportControlStatement(new(), Period(), [Line(kind, -10000m, -2100m)]);
        Assert.Equal("-10000", kh.Descendants(detail).Single().Attribute("zakl_dane1")?.Value);
        Assert.Empty(kh.Descendants(summary));
    }

    [Theory]
    [InlineData(10000, "VetaA5")]
    [InlineData(-10000, "VetaA5")]
    [InlineData(10000.01, "VetaA4")]
    [InlineData(-10000.01, "VetaA4")]
    public void Detail_Threshold_Is_Strict(decimal total, string section)
    {
        var kh = _exporter.ExportControlStatement(new(), Period(), [Line(InvoiceKind.IssuedDomestic, total - 100m, 100m)]);
        Assert.Single(kh.Descendants(section));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("DE123456789")]
    public void Issued_Document_Without_Czech_Dic_Belongs_To_A5(string? dic)
    {
        var kh = _exporter.ExportControlStatement(new(), Period(), [Line(InvoiceKind.IssuedDomestic, 20000m, 4200m, dic)]);
        Assert.Single(kh.Descendants("VetaA5"));
        Assert.Empty(kh.Descendants("VetaA4"));
    }

    [Fact]
    public void Northern_Ireland_Service_Is_Third_Country_And_Remains_Neutral()
    {
        var lines = new[] { Line(InvoiceKind.ReverseCharge, 207.14m, 43.50m, "XI123456789") };
        var dp = _exporter.ExportVatReturn(new(), Period(), lines);
        Assert.Equal("44", dp.Descendants("Veta1").Single().Attribute("dan_psl23_z")?.Value);
        Assert.Null(dp.Descendants("Veta1").Single().Attribute("p_sl23_e"));
        Assert.Equal("0", dp.Descendants("Veta6").Single().Attribute("dano_da")?.Value);
        Assert.Equal("", _exporter.ExportControlStatement(new(), Period(), lines).Descendants("VetaA2").Single().Attribute("k_stat")?.Value);
    }

    [Fact]
    public void Different_Supply_Dates_And_Case_Sensitive_Numbers_Are_Not_Merged()
    {
        var first = Line(InvoiceKind.ReverseCharge, 100m, 21m, null, "Ab1");
        var second = Line(InvoiceKind.ReverseCharge, 100m, 21m, null, "ab1");
        var third = Line(InvoiceKind.ReverseCharge, 100m, 21m, null, "Ab1");
        third.TaxableSupplyDate = third.TaxableSupplyDate.AddDays(1);
        Assert.Equal(3, _exporter.ExportControlStatement(new(), Period(), [first, second, third]).Descendants("VetaA2").Count());
    }

    [Fact]
    public void Unidentified_Reverse_Charge_Documents_Cannot_Be_Exported()
    {
        var lines = new[] { Line(InvoiceKind.ReverseCharge, 100m, 21m, null, ""), Line(InvoiceKind.ReverseCharge, 100m, 21m, null, "") };
        Assert.Throws<InvalidOperationException>(() => _exporter.ExportControlStatement(new(), Period(), lines));
    }

    [Fact]
    public void Czech_Dic_With_And_Without_Prefix_Groups_Multiple_Rates()
    {
        var first = Line(InvoiceKind.IssuedDomestic, 6000m, 1260m, " CZ12345678 ");
        var second = Line(InvoiceKind.IssuedDomestic, 3000m, 360m, "12345678");
        second.VatRate = VatRateKind.Reduced12;
        var kh = _exporter.ExportControlStatement(new(), Period(), [first, second]);
        Assert.Equal("12345678", kh.Descendants("VetaA4").Single().Attribute("dic_odb")?.Value);
    }

    [Theory]
    [InlineData(InvoiceKind.IssuedDomestic)]
    [InlineData(InvoiceKind.ReceivedDomesticWithVat)]
    [InlineData(InvoiceKind.ReverseCharge)]
    public void Unspecified_Exemption_Does_Not_Leak_Into_Standard_Rate(InvoiceKind kind)
    {
        var line = Line(kind, 1000m, 0m);
        line.VatRate = VatRateKind.Zero0;
        Assert.Throws<InvalidOperationException>(() => _exporter.ExportVatReturn(new(), Period(), [line]));
        Assert.Throws<InvalidOperationException>(() => _exporter.ExportControlStatement(new(), Period(), [line]));
    }

    [Fact]
    public void Discovery_Date_Is_Independent_Of_Xml_Creation_Date()
    {
        var period = Period();
        period.DiscoveryDate = new(2026, 7, 2);
        var original = _exporter.ExportVatReturn(new(), period, []);
        var dp = _exporter.ExportVatReturn(new(), period, [], "D", [original]);
        var kh = _exporter.ExportControlStatement(new(), period, [], "N");
        foreach (var doc in new[] { dp, kh })
        {
            Assert.Equal("02.07.2026", doc.Descendants("VetaD").Single().Attribute("d_zjist")?.Value);
            Assert.Equal("10.07.2026", doc.Descendants("VetaD").Single().Attribute("d_poddp")?.Value);
        }
    }

    [Fact]
    public void Correction_Requires_Discovery_Date()
        => Assert.Throws<InvalidOperationException>(() => _exporter.ExportControlStatement(new(), Period(), [], "N"));

    [Fact]
    public void Multiple_Dates_Keep_Whole_Document_Limit()
    {
        var first = Line(InvoiceKind.IssuedDomestic, 5000m, 1050m);
        var second = Line(InvoiceKind.IssuedDomestic, 5000m, 1050m);
        second.TaxableSupplyDate = second.TaxableSupplyDate.AddDays(1);
        var kh = _exporter.ExportControlStatement(new(), Period(), [first, second]);
        Assert.Equal(2, kh.Descendants("VetaA4").Count());
        Assert.Empty(kh.Descendants("VetaA5"));
    }

    [Fact]
    public void Unsupported_Previous_Return_Is_Not_Automatically_Zeroed()
    {
        var period = Period();
        period.DiscoveryDate = new(2026, 7, 2);
        var previous = _exporter.ExportVatReturn(new(), period, []);
        previous.Root!.Element("DPHDP3")!.Add(new XElement("Veta2", new XAttribute("pln_vyvoz", 10000)));
        Assert.Throws<InvalidOperationException>(() => _exporter.ExportVatReturn(new(), period, [], "D", [previous]));
    }

    [Theory]
    [InlineData("1 234,56", 1234.56)]
    [InlineData("1\u00a0234,56", 1234.56)]
    [InlineData("1\u202f234.56", 1234.56)]
    [InlineData("-0,125", -0.125)]
    public void Parses_Czech_Amounts(string text, decimal expected) => Assert.Equal(expected, DecimalInput.Parse(text));

    [Theory]
    [InlineData("abc")]
    [InlineData("1.234,56")]
    [InlineData("1,2,3")]
    [InlineData("")]
    public void Invalid_Numbers_Do_Not_Become_Zero(string text) => Assert.Throws<FormatException>(() => DecimalInput.Parse(text));
}
