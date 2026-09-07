using Dph.Core.Domain;

namespace Dph.Core.Tests;

public sealed class InvoiceKindClassifierTests
{
    [Theory]
    [InlineData("IE4143435AH", true)]
    [InlineData(" ie4143435AH ", true)]
    [InlineData("CZ27082440", false)]
    [InlineData("US123", false)]
    [InlineData("XI123456789", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Detects_Eu_Supplier_From_Dic_Prefix(string? dic, bool expected)
        => Assert.Equal(expected, InvoiceKindClassifier.IsEuSupplier(dic));
}
