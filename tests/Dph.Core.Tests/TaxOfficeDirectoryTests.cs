using System.Linq;
using Dph.Core.Epo;

namespace Dph.Core.Tests;

public sealed class TaxOfficeDirectoryTests
{
    [Fact]
    public void Every_workplace_belongs_to_a_known_office()
    {
        var officeCodes = TaxOfficeDirectory.Offices.Select(x => x.Code).ToHashSet();
        Assert.All(TaxOfficeDirectory.Workplaces, w => Assert.Contains(w.OfficeCode, officeCodes));
    }

    [Fact]
    public void Contains_all_regional_offices_and_specialized()
    {
        var codes = TaxOfficeDirectory.Offices.Select(x => x.Code).ToList();
        for (var code = 451; code <= 464; code++)
        {
            Assert.Contains(code.ToString(), codes);
        }

        Assert.Contains("13", codes); // Specializovaný finanční úřad
    }

    [Fact]
    public void Maps_known_prague_workplace()
    {
        var praha1 = TaxOfficeDirectory.Workplaces.SingleOrDefault(x => x.Code == "2001");
        Assert.NotNull(praha1);
        Assert.Equal("451", praha1!.OfficeCode);
    }
}
