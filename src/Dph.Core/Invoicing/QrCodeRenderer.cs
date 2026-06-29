using QRCoder;

namespace Dph.Core.Invoicing;

// Vykreslí libovolný textový obsah (typicky SPAYD) do QR kódu.
// Výstup je 24bit BMP – ten umí PDFsharp dekódovat (jeho PNG dekodér QRCoder PNG odmítá),
// a nevyžaduje System.Drawing, takže funguje cross-platform.
public static class QrCodeRenderer
{
    public static byte[] RenderBmp(string content, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var qr = new BitmapByteQRCode(data);
        return qr.GetGraphic(pixelsPerModule);
    }
}
