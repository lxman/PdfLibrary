using System.Reflection;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public sealed class PdfRendererEmptyDecodeTests
{
    [Fact]
    public void ShowText_DebugCharacterWithEmptyDecode_DoesNotAbortPage()
    {
        var target = new MockRenderTarget();
        var resources = new PdfResources(new PdfDictionary());
        FontCache(resources)["F1"] = new EmptyDecodeFont();
        var renderer = new PdfRenderer(target, resources);

        renderer.ProcessOperators(
        [
            new BeginTextOperator(),
            new SetTextFontOperator(new PdfName("F1"), 12),
            new ShowTextOperator(new PdfString([0x03])),
            new EndTextOperator(),
        ]);
    }

    private static Dictionary<string, PdfFont?> FontCache(PdfResources resources)
    {
        FieldInfo field = typeof(PdfResources).GetField("_fontCache",
                              BindingFlags.Instance | BindingFlags.NonPublic)
                          ?? throw new InvalidOperationException("PdfResources font cache was not found.");
        return (Dictionary<string, PdfFont?>)field.GetValue(resources)!;
    }

    private sealed class EmptyDecodeFont() : PdfFont(new PdfDictionary())
    {
        internal override PdfFontType FontType => PdfFontType.Type1;

        public override string DecodeCharacter(int charCode) => string.Empty;

        public override double GetCharacterWidth(int charCode) => 500;
    }
}
