using Dph.Core.Domain;

namespace Dph.Core.Tests;

public sealed class InvoiceKindClassifierTests
{
    [Theory]
    [InlineData("CZ27082440", "F1", InvoiceKind.ReceivedDomesticWithVat)]
    [InlineData("cz27082440", "F1", InvoiceKind.ReceivedDomesticWithVat)]
    [InlineData("27082440", "F1", InvoiceKind.ReceivedDomesticWithVat)]   // české DIČ bez prefixu
    [InlineData("7503012671", "F1", InvoiceKind.ReceivedDomesticWithVat)] // DIČ fyzické osoby (rodné číslo)
    [InlineData("IE4143435AH", "F1", InvoiceKind.ReverseCharge)]          // EU dodavatel → ř.5/6
    [InlineData("DE811128135", "F1", InvoiceKind.ReverseCharge)]
    [InlineData(null, "F1", InvoiceKind.ReverseCharge)]                   // třetí země bez VAT ID → ř.12/13
    [InlineData("", "F1", InvoiceKind.ReverseCharge)]
    [InlineData("  ", "F1", InvoiceKind.ReverseCharge)]
    [InlineData("EU372000042", "F1", InvoiceKind.ReverseCharge)]          // OSS registrace ≠ členský stát
    [InlineData(null, "B3", InvoiceKind.ReceivedDomesticWithVat)]         // souhrn KH je vždy tuzemský
    [InlineData(null, "b3", InvoiceKind.ReceivedDomesticWithVat)]
    public void Classifies_Received_By_Supplier_Dic(string? dic, string evidenceNumber, InvoiceKind expected)
        => Assert.Equal(expected, InvoiceKindClassifier.ClassifyReceived(dic, evidenceNumber));

    [Theory]
    [InlineData("IE4143435AH", true)]
    [InlineData(" ie4143435AH ", true)]
    [InlineData("CZ27082440", false)]
    [InlineData("US123", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Detects_Eu_Supplier_From_Dic_Prefix(string? dic, bool expected)
        => Assert.Equal(expected, InvoiceKindClassifier.IsEuSupplier(dic));
}
