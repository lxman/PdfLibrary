using PdfLibrary.Builder;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// <see cref="RecordingRenderTarget.Record(PdfPage, double)"/> is the entry point Pellucid actually
/// calls (via <c>PdfPageSkiaExtensions.RenderTo</c>/<c>RenderToImage</c> and the Avalonia page
/// control) — it, not <see cref="PdfPage.Render(IRenderTarget, int, double)"/> directly, is where the
/// font-provider seam needs to live so a consuming app can inject one. Today it always renders through
/// the null-provider path (falls back to <see cref="SystemFontLocator.Default"/> inside
/// <c>PdfRenderer</c>), with no way for a caller to supply their own <see cref="ISystemFontProvider"/>.
/// </summary>
public class RenderFontProviderSeamTests
{
    /// <summary>Records every /BaseFont it is asked to resolve and always declines (returns null),
    /// so "was the provider consulted" is observable without needing real substitute font bytes.</summary>
    private sealed class RecordingProvider : ISystemFontProvider
    {
        public readonly List<string> Asked = [];
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => [];
        public bool IsFontAvailable(string familyName) => false;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
        public FontMatch? Resolve(FontRequest request) { Asked.Add(request.BaseFont); return null; }
    }

    /// <summary>A one-page document drawing "Hi" in "Helvetica" with no /FontFile embedded — the
    /// substitute-resolution path (and therefore the injected provider) is only reached when the
    /// font is NOT embedded.</summary>
    private static PdfDocument BuildNonEmbeddedFontDoc(out MemoryStream stream)
    {
        byte[] pdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hi", 100, 700, "Helvetica", 24))
            .ToByteArray();
        stream = new MemoryStream(pdf);
        return PdfDocument.Load(stream);
    }

    /// <summary>A one-page document with an embedded font (PublicPixel) — never needs substitute
    /// resolution, so its render is deterministic and independent of the host's installed fonts.</summary>
    private static PdfDocument BuildEmbeddedFontDoc(out MemoryStream stream)
    {
        byte[] fontBytes = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));
        byte[] pdf = PdfDocumentBuilder.Create()
            .LoadFont(fontBytes, "Pixel")
            .AddPage(p => p.AddText("AB", 100, 700, "Pixel", 24))
            .ToByteArray();
        stream = new MemoryStream(pdf);
        return PdfDocument.Load(stream);
    }

    [Fact]
    public void Record_WithInjectedProvider_ConsultsIt()
    {
        using PdfDocument doc = BuildNonEmbeddedFontDoc(out MemoryStream stream);
        using (stream)
        {
            PdfPage page = doc.GetPage(0)!;
            var provider = new RecordingProvider();

            RecordingRenderTarget.Record(page, 1.0, provider);

            Assert.NotEmpty(provider.Asked);
        }
    }

    // Record_WithoutProvider_BehavesIdenticallyToTodaysEntryPoint was removed (review finding, L-2
    // whole-branch review): it compared Record(page, 1.0) with Record(page, 1.0, null), but the
    // former delegates directly to the latter (see RecordingRenderTarget.Record(PdfPage, double)),
    // and every render entry point in this codebase funnels to the same internal
    // PdfPage.Render(IRenderTarget, ISystemFontProvider?, int, double) — there is no second,
    // independent code path for a test to compare against, so the assertion could never fail.
    // The identity is now documented as structural on the two-arg overload's doc comment instead.
}
