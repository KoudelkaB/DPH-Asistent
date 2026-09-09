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
    [InlineData("7nyn2d9", true)]
    [InlineData("7nyn2d", false)]
    [InlineData("7nyn2d99", false)]
    [InlineData("7nyn2d-", false)]
    [InlineData(null, false)]
    public void Validates_data_box_id_shape(string? id, bool expected)
        => Assert.Equal(expected, TaxOfficeDataBoxes.IsValidId(id));
}
