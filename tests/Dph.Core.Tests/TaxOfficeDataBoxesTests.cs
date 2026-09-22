using System.Linq;
using Dph.Core.Epo;
using Dph.Core.Isds;

namespace Dph.Core.Tests;

public sealed class TaxOfficeDataBoxesTests
{
    [Fact]
    public void Every_office_in_the_directory_has_a_data_box()
    {
        foreach (var office in TaxOfficeDirectory.Offices)
        {
            Assert.True(
                TaxOfficeDataBoxes.For(office.Code) is not null,
                $"Chybí ID datové schránky pro {office.Code} – {office.Name}.");
        }
    }

    [Fact]
    public void All_ids_have_the_isds_shape()
    {
        Assert.All(TaxOfficeDataBoxes.All.Values, id => Assert.True(TaxOfficeDataBoxes.IsValidId(id), id));
        // ID schránek se nesmí opakovat – jinak by podání šlo jinému úřadu.
        Assert.Equal(TaxOfficeDataBoxes.All.Count, TaxOfficeDataBoxes.All.Values.Distinct().Count());
    }

    [Theory]
    [InlineData("451", "7nyn2d9")] // Finanční úřad pro hlavní město Prahu
    [InlineData("461", "qdhny4c")] // Finanční úřad pro Jihomoravský kraj
    [InlineData("13", "7cs8cge")]  // Specializovaný finanční úřad
    public void Maps_offices_from_the_official_list(string officeCode, string expected)
        => Assert.Equal(expected, TaxOfficeDataBoxes.For(officeCode));

    [Fact]
    public void Trims_and_rejects_unknown_codes()
    {
        Assert.Equal("7nyn2d9", TaxOfficeDataBoxes.For(" 451 "));
        Assert.Null(TaxOfficeDataBoxes.For("999"));
        Assert.Null(TaxOfficeDataBoxes.For(""));
        Assert.Null(TaxOfficeDataBoxes.For(null));
    }

    [Theory]
    [InlineData("2001", "2p2n5ad")] // ÚzP pro Prahu 1
    [InlineData("2110", "w56n5ku")] // ÚzP v Kladně
    [InlineData("3312", "4jhn65c")] // ÚzP ve Vsetíně
    public void Maps_workplaces_from_the_official_list(string workplaceCode, string expected)
        => Assert.Equal(expected, TaxOfficeDataBoxes.ForWorkplace(workplaceCode));

    [Fact]
    public void Only_workplaces_with_their_own_data_box_are_routed_to_it()
    {
        Assert.Equal("2p2n5ad", TaxOfficeDataBoxes.ForWorkplace("2001", "451"));
        // Pracoviště bez vlastní schránky (optimalizované) i prázdná volba nemají kam – podání pak
        // jde do schránky finančního úřadu.
        Assert.Null(TaxOfficeDataBoxes.ForWorkplace("", "451"));
        Assert.Null(TaxOfficeDataBoxes.ForWorkplace("2108", "452")); // ÚzP v Dobříši
        Assert.Null(TaxOfficeDataBoxes.ForWorkplace("9999", ""));
    }

    [Fact]
    public void Workplace_of_another_office_never_routes_the_filing_there()
    {
        // Uložený kód pracoviště přežije změnu úřadu (ARES úřad přepíše, pracoviště nechá být).
        // ÚzP pro Prahu 1 patří pod 451; se Středočeským krajem (452) se nesmí použít.
        Assert.Null(TaxOfficeDataBoxes.ForWorkplace("2001", "452"));
        Assert.Equal("2p2n5ad", TaxOfficeDataBoxes.ForWorkplace("2001", "451"));

        Assert.False(TaxOfficeDataBoxes.BelongsToOffice("2001", "452"));
        Assert.True(TaxOfficeDataBoxes.BelongsToOffice("2001", "451"));
        Assert.False(TaxOfficeDataBoxes.BelongsToOffice("9999", "451"));
        Assert.False(TaxOfficeDataBoxes.BelongsToOffice("2001", ""));

        // Bez kódu úřadu zůstává dohledání podle pracoviště nezměněné.
        Assert.Equal("2p2n5ad", TaxOfficeDataBoxes.ForWorkplace("2001"));
    }

    [Fact]
    public void Every_workplace_with_a_data_box_belongs_to_its_office_in_the_directory()
    {
        // Tabulka schránek a číselník musí sedět, jinak by kontrola příslušnosti tiše vyřadila
        // pracoviště, která vlastní schránku mají.
        foreach (var (code, _) in TaxOfficeDataBoxes.AllWorkplaces)
        {
            var workplace = TaxOfficeDirectory.Workplaces.Single(x => x.Code == code);
            Assert.True(
                TaxOfficeDataBoxes.BelongsToOffice(code, workplace.OfficeCode),
                $"Pracoviště {code} není v číselníku vedené pod úřadem {workplace.OfficeCode}.");
        }
    }

    [Fact]
    public void Workplace_ids_have_the_isds_shape_and_belong_to_known_workplaces()
    {
        Assert.All(TaxOfficeDataBoxes.AllWorkplaces.Values, id => Assert.True(TaxOfficeDataBoxes.IsValidId(id), id));

        var known = TaxOfficeDirectory.Workplaces.Select(x => x.Code).ToHashSet();
        Assert.All(TaxOfficeDataBoxes.AllWorkplaces.Keys, code => Assert.Contains(code, known));

        // Schránka územního pracoviště nesmí kolidovat se schránkou finančního úřadu.
        Assert.Empty(TaxOfficeDataBoxes.AllWorkplaces.Values.Intersect(TaxOfficeDataBoxes.All.Values));
    }

    [Theory]
    [InlineData("7nyn2d9", true)]
    [InlineData("7nyn2d", false)]
    [InlineData("7nyn2d99", false)]
    [InlineData("7nyn2d-", false)]
    [InlineData(null, false)]
    public void Validates_data_box_id_shape(string? id, bool expected)
        => Assert.Equal(expected, TaxOfficeDataBoxes.IsValidId(id));
}
