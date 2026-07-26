using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Task 3 migrated <see cref="ColorSpaceResolver.PaintsNothing(PdfObject?, PdfDocument?)"/> and
/// <see cref="ColorSpaceResolver.PlatesForColorSpaceObject"/> onto <see cref="SpotColorSpace.TryParse"/>,
/// claiming the migration changes no behaviour. These tests exercise exactly the input classes where the
/// pre-migration positional code and the shared parser could have diverged — the code review that found
/// this gap pointed out that the original 28 tests in scope for Task 3 never actually hit one. Each case
/// asserts BOTH members' answers, because the whole point of the task is that they answer differently
/// (false vs. null) for the same malformed input.
/// </summary>
public class ColorSpaceResolverSpotMigrationEquivalenceTests
{
    private const string Tint = "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>";

    private static PdfArray Parse(string pdfArrayLiteral)
    {
        // Same idiom as ColorSpaceResolverPaintsNothingTests.cs:20-31: parse a literal colour-space
        // array through a throwaway one-page document rather than hand-building PdfArray/PdfName object
        // graphs.
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f");
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        PdfDictionary colorSpaces = page.GetResources()!.GetColorSpaces()!;
        return (PdfArray)colorSpaces[new PdfName("Cs0")]!;
    }

    [Fact]
    public void DeviceN_EmptyNamesArray_PaintsNothingFalse_PlatesNull()
    {
        // The guard the brief specifically called out: AllNamesResolved is vacuously true for an empty
        // list, so both members must check Names.Count == 0 separately or a [/DeviceN [] ...] array
        // would flip from "not suppressed"/"no plate mask" to the opposite.
        PdfArray cs = Parse("[/DeviceN [] /DeviceCMYK " + Tint + "]");

        Assert.False(ColorSpaceResolver.PaintsNothing(cs, null));
        Assert.Null(ColorSpaceResolver.PlatesForColorSpaceObject(cs, null));
    }

    [Fact]
    public void DeviceN_NonNameElement_PaintsNothingFalse_PlatesNull()
    {
        // The stated point of the task: a DeviceN name element that isn't a PdfName is recorded as
        // null by SpotColorSpace, which is NOT "/None" (PaintsNothing => false) and is NOT a
        // recognisable CMYK/spot colorant (PlatesForColorSpaceObject => null, "cannot know the plate
        // set"). The two members must disagree exactly like this — neither may return true/a mask.
        PdfArray cs = Parse("[/DeviceN [/Cyan 42] /DeviceCMYK " + Tint + "]");

        Assert.False(ColorSpaceResolver.PaintsNothing(cs, null));
        Assert.Null(ColorSpaceResolver.PlatesForColorSpaceObject(cs, null));
    }

    [Fact]
    public void Separation_NonNameColorant_PaintsNothingFalse_PlatesNull()
    {
        // Same asymmetry as above, for Separation's single colorant slot instead of a DeviceN array
        // entry.
        PdfArray cs = Parse("[/Separation 42 /DeviceCMYK " + Tint + "]");

        Assert.False(ColorSpaceResolver.PaintsNothing(cs, null));
        Assert.Null(ColorSpaceResolver.PlatesForColorSpaceObject(cs, null));
    }

    [Fact]
    public void Separation_TwoElementNoneArray_PaintsNothingTrue()
    {
        // Short-array leniency the migration newly introduces into these members: SpotColorSpace
        // accepts an array as short as two elements (no alternate, no tint transform), and a
        // [/Separation /None] array must still be recognised as painting nothing.
        PdfArray cs = Parse("[/Separation /None]");

        Assert.True(ColorSpaceResolver.PaintsNothing(cs, null));
        // /None marks no CMYK plate but IS a recognised colorant, so the plate mask is "no plates set"
        // rather than "unknown" — matching the pre-migration PlatesForColorSpaceObject answer for this
        // same short array.
        Assert.Equal((false, false, false, false), ColorSpaceResolver.PlatesForColorSpaceObject(cs, null)!.Value);
    }

    [Fact]
    public void IndexedOverIndexedOverSeparationNone_PaintsNothingTrue()
    {
        // Confirms the Indexed recursion still chains through two levels now that it lives outside
        // SpotColorSpace (which does not model Indexed at all).
        PdfArray cs = Parse(
            "[/Indexed [/Indexed [/Separation /None /DeviceRGB " + Tint + "] 1 <0055>] 1 <0066>]");

        Assert.True(ColorSpaceResolver.PaintsNothing(cs, null));
    }
}
