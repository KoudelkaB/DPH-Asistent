using System.Xml.Linq;
using Dph.Core.Epo;

namespace Dph.Core.Tests;

public sealed class ImportRegressionTests
{
    private static string Xml(string date, string form, decimal amount, string extra = "") => $"""
        <Pisemnost><DPHKH1><VetaD rok="2026" mesic="5" d_poddp="{date}" khdph_forma="{form}" />
        <VetaA5 zakl_dane1="{amount}" dan1="210" />{extra}</DPHKH1></Pisemnost>
        """;

    private static void Import(string xml, ImportedEpoData data)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dph-test-{Guid.NewGuid():N}.xml");
        try { File.WriteAllText(path, xml); new EpoXmlImporter().ImportFile(path, data); }
        finally { File.Delete(path); }
    }

    private static ImportedEpoData ImportFolder(params string[] files)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dph-folder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            for (var i = 0; i < files.Length; i++) File.WriteAllText(Path.Combine(directory, $"{i:D3}.xml"), files[i]);
            return new EpoXmlImporter().ImportDirectory(directory);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string WithSubject(string xml, string dic)
        => xml.Replace("<VetaA5", $"<VetaP dic=\"{dic}\" /><VetaA5");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unrelated_Form_Does_Not_Remove_Valid_Kh(bool reversed)
    {
        var valid = WithSubject(Xml("20.06.2026", "B", 1000), "12345678");
        var unrelated = valid.Replace("DPHKH1", "DPHSHV");
        var result = reversed ? ImportFolder(unrelated, valid) : ImportFolder(valid, unrelated);
        Assert.Single(Assert.Single(result.Periods).Invoices);
        Assert.Single(result.SkippedFiles);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Foreign_Subject_Does_Not_Remove_Selected_Subject_Period(bool reversed)
    {
        var first = WithSubject(Xml("20.06.2026", "B", 1000), "12345678");
        var second = WithSubject(Xml("20.06.2026", "B", 2000), "87654321");
        var result = reversed ? ImportFolder(second, first) : ImportFolder(first, second);
        Assert.Equal(reversed ? 2000m : 1000m, Assert.Single(Assert.Single(result.Periods).Invoices).TaxBaseCzk);
        Assert.Single(result.SkippedFiles);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Malformed_Kh_Of_Same_Subject_Blocks_Only_Its_Period_And_Explains_Why(bool reversed)
    {
        var valid = WithSubject(Xml("20.06.2026", "B", 1000), "12345678");
        var bad = WithSubject(Xml("02.07.2026", "N", 2000, "<VetaB3 dan1=\"bad\" />"), "CZ 12345678");
        var otherPeriod = valid.Replace("mesic=\"5\"", "mesic=\"6\"");
        var result = reversed ? ImportFolder(bad, valid, otherPeriod) : ImportFolder(valid, bad, otherPeriod);
        Assert.Equal(6, Assert.Single(result.Periods).Period.Month);
        Assert.Contains("2026-05", Assert.Single(result.Warnings));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Subsequent_Kh_Replaces_Original_Regardless_Of_File_Order(bool reverse)
    {
        var data = new ImportedEpoData();
        var files = new[] { Xml("20.06.2026", "B", 1000), Xml("02.07.2026", "N", 2000) };
        foreach (var file in reverse ? files.Reverse() : files) Import(file, data);
        var period = Assert.Single(data.Periods);
        Assert.Equal(2000m, Assert.Single(period.Invoices).TaxBaseCzk);
        Assert.Equal("N", period.Period.FormType);
    }

    [Fact]
    public void Reimport_Is_Idempotent()
    {
        var data = new ImportedEpoData();
        Import(Xml("20.06.2026", "B", 1000), data);
        Import(Xml("20.06.2026", "B", 1000), data);
        Assert.Single(Assert.Single(data.Periods).Invoices);
    }

    [Fact]
    public void Malformed_File_Does_Not_Partially_Append_Amounts()
    {
        var data = new ImportedEpoData();
        Assert.Throws<FormatException>(() => Import(Xml("20.06.2026", "B", 1000, "<VetaB3 zakl_dane1=\"bad\" />"), data));
        Assert.Empty(data.Periods);
    }

    [Theory]
    [InlineData("<VetaB1 zakl_dane1=\"1000\" />")]
    [InlineData("<VetaA5 zakl_dane3=\"1000\" dan3=\"100\" />")]
    [InlineData("<VetaA4 zdph_44=\"A\" />")]
    public void Unsupported_Regime_Is_Not_Silently_Dropped(string extra)
    {
        var data = new ImportedEpoData();
        Assert.Throws<FormatException>(() => Import(Xml("20.06.2026", "B", 1000, extra), data));
        Assert.Empty(data.Periods);
    }
}
