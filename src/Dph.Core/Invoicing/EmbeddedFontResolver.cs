using System.Reflection;
using PdfSharp.Fonts;

namespace Dph.Core.Invoicing;

// PDFsharp na Linuxu nemá GDI ani systémové fonty, takže font dodáme sami z embedded resource
// (DejaVu Sans pokrývá českou diakritiku). Registruje se jednou přes EnsureRegistered().
public sealed class EmbeddedFontResolver : IFontResolver
{
    public const string FamilyName = "DejaVu Sans";

    private const string RegularFace = "DejaVuSans";
    private const string BoldFace = "DejaVuSans-Bold";

    private static readonly object Gate = new();
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            GlobalFontSettings.FontResolver ??= new EmbeddedFontResolver();
            _registered = true;
        }
    }

    public byte[]? GetFont(string faceName)
        => LoadResource(faceName.Equals(BoldFace, StringComparison.OrdinalIgnoreCase)
            ? "DejaVuSans-Bold.ttf"
            : "DejaVuSans.ttf");

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        => new(isBold ? BoldFace : RegularFace);

    private static byte[] LoadResource(string fileName)
    {
        var assembly = typeof(EmbeddedFontResolver).Assembly;
        var name = Array.Find(assembly.GetManifestResourceNames(),
            x => x.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded font resource '{fileName}' nebyl nalezen.");
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded font resource '{name}' nelze otevřít.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
