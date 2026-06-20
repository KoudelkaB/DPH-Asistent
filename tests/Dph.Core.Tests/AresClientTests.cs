using System.Text.Json;
using Dph.Core.Services;

namespace Dph.Core.Tests;

public sealed class AresClientTests
{
    [Fact]
    public void Parses_Ares_Subject_Name_And_Dic()
    {
        const string json = """
            {
              "ico": "27082440",
              "obchodniJmeno": "Alza.cz a.s.",
              "datumAktualizace": "2026-06-20",
              "dic": "CZ27082440"
            }
            """;

        using var document = JsonDocument.Parse(json);

        var subject = AresClient.Parse(document.RootElement);

        Assert.NotNull(subject);
        Assert.Equal("27082440", subject.Ico);
        Assert.Equal("Alza.cz a.s.", subject.OfficialName);
        Assert.Equal("CZ27082440", subject.Dic);
        Assert.Equal(new DateOnly(2026, 6, 20), subject.UpdatedOn);
    }

    [Fact]
    public void Normalizes_Ico_To_Digits()
    {
        Assert.Equal("27082440", AresClient.NormalizeIco(" IČO: 270 824 40 "));
    }

    [Theory]
    [InlineData("CZ27082440", "27082440")]
    [InlineData("27082440", "27082440")]
    [InlineData("CZ 270 824 40", "27082440")]
    [InlineData("CZ7503012671", null)]
    public void Extracts_Ico_From_Czech_Legal_Entity_Dic(string dic, string? expectedIco)
    {
        Assert.Equal(expectedIco, AresClient.TryGetIcoFromDic(dic));
    }
}
