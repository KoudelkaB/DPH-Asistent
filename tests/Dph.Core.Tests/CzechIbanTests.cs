using Dph.Core.Invoicing;

namespace Dph.Core.Tests;

public sealed class CzechIbanTests
{
    [Theory]
    // Účet s předčíslím (ČSOB/Era), kontrolováno proti referenčnímu IBAN.
    [InlineData("19-2000145399/0800", "CZ6508000000192000145399")]
    // Účet bez předčíslí.
    [InlineData("2000145399/0800", "CZ7908000000002000145399")]
    public void Builds_Valid_Czech_Iban(string account, string expected)
    {
        var iban = CzechIban.TryFromAccount(account);
        Assert.Equal(expected, iban);
        Assert.True(IsMod97Valid(iban!));
    }

    [Fact]
    public void Passes_Through_Existing_Iban()
    {
        Assert.Equal("CZ6508000000192000145399", CzechIban.TryFromAccount("CZ65 0800 0000 1920 0014 5399"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bez lomítka")]
    [InlineData("123/")]
    public void Returns_Null_For_Invalid_Input(string? account)
    {
        Assert.Null(CzechIban.TryFromAccount(account));
    }

    private static bool IsMod97Valid(string iban)
    {
        var rearranged = iban[4..] + iban[..4];
        var numeric = string.Concat(rearranged.Select(c => char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString()));
        return System.Numerics.BigInteger.Parse(numeric) % 97 == 1;
    }
}
