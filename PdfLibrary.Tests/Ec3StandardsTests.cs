using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Editing.Annotations;
using PdfLibrary.Fonts;
using PdfLibrary.Parsing;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests;

/// <summary>
/// Unit tests verifying ISO 32000-2:2020 Errata Collection 3 (EC-3) bug fixes (Part 1, all versions)
/// and PDF 2.0-scoped conformance/feature updates (Part 2, v2.0 only).
/// </summary>
public class Ec3StandardsTests
{
    // ─── Part 1: Universal PDF 1.x + 2.0 Fixes ──────────────────────────────────

    [Fact]
    public void Issue215_ImageMask_IgnoresColorSpaceAndMask()
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(2),
            [new PdfName("Height")] = new PdfInteger(2),
            [new PdfName("ImageMask")] = PdfBoolean.True,
            [new PdfName("BitsPerComponent")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = new PdfName("DeviceRGB"),
            [new PdfName("Mask")] = new PdfArray(new PdfInteger(0), new PdfInteger(0))
        };
        var stream = new PdfStream(dict, [0x80, 0x00]);
        var img = new PdfImage(stream);

        Assert.True(img.IsImageMask);
        Assert.Equal("Unknown", img.ColorSpace);
        Assert.Null(img.ColorSpaceArray);
        Assert.False(img.HasAlpha);
        Assert.False(img.HasEffectiveSMask);
    }

    [Fact]
    public void Issue215_SMaskInDataNonZero_IgnoresSMaskDictionaryEntry()
    {
        var smaskDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = new PdfName("DeviceGray"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8)
        };
        var smaskStream = new PdfStream(smaskDict, [0x00]);

        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = new PdfName("DeviceRGB"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
            [new PdfName("SMaskInData")] = new PdfInteger(1),
            [new PdfName("SMask")] = smaskStream
        };
        var stream = new PdfStream(dict, [255, 0, 0]);
        var img = new PdfImage(stream);

        Assert.Equal(1, img.SMaskInData);
        Assert.False(img.HasEffectiveSMask);
        Assert.False(img.HasAlpha);
    }

    [Fact]
    public void Issue215_ImageSMask_OverridesGraphicsStateSoftMaskInRecordingRenderTarget()
    {
        var smaskDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = new PdfName("DeviceGray"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8)
        };
        var smaskStream = new PdfStream(smaskDict, [0xFF]);

        var imgDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = new PdfName("DeviceRGB"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
            [new PdfName("SMask")] = smaskStream
        };
        var imgStream = new PdfStream(imgDict, [255, 0, 0]);
        var img = new PdfImage(imgStream);
        Assert.True(img.HasEffectiveSMask);

        var target = new RecordingRenderTarget(document: null);
        target.BeginPage(1, 100, 100);
        var state = new PdfGraphicsState();

        target.RenderSoftMask("Luminosity", _ => { });
        target.DrawImage(img, state);
        target.ClearSoftMask();

        IReadOnlyList<DrawCommand> commands = target.TakeSnapshot().Commands;

        // Verify that SoftMaskPopCommand is emitted before ImageCommand and SoftMaskPushCommand is re-applied after
        int popBeforeImg = -1;
        int imgIdx = -1;
        int rePushAfterImg = -1;
        for (int i = 0; i < commands.Count; i++)
        {
            if (commands[i] is SoftMaskPopCommand && imgIdx == -1)
                popBeforeImg = i;
            else if (commands[i] is ImageCommand)
                imgIdx = i;
            else if (commands[i] is SoftMaskPushCommand && imgIdx != -1 && rePushAfterImg == -1)
                rePushAfterImg = i;
        }

        Assert.True(popBeforeImg >= 0, "Expected graphics-state SoftMask to be popped before ImageCommand");
        Assert.True(imgIdx > popBeforeImg, "Expected ImageCommand after SoftMaskPopCommand");
        Assert.True(rePushAfterImg > imgIdx, "Expected graphics-state SoftMask to be restored after ImageCommand");
    }

    [Fact]
    public void Issue287_AnnotationBorderWidth_BsOverridesBorderAndFallsBackToBorder()
    {
        var doc = new PdfDocument();
        var annotWithBoth = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Square"),
            [new PdfName("Rect")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(50), new PdfInteger(50)),
            [new PdfName("Border")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(9)),
            [new PdfName("BS")] = new PdfDictionary { [new PdfName("W")] = new PdfInteger(3) },
            [new PdfName("C")] = new PdfArray(new PdfInteger(1), new PdfInteger(0), new PdfInteger(0))
        };
        AnnotationAppearanceGenerator.Generate(doc, annotWithBoth);
        var apDict1 = (PdfDictionary)annotWithBoth[new PdfName("AP")];
        var nRef1 = (PdfIndirectReference)apDict1[new PdfName("N")];
        var nStream1 = (PdfStream)doc.GetObject(nRef1.ObjectNumber)!;
        string content1 = Encoding.ASCII.GetString(nStream1.GetDecodedData());
        Assert.Contains("3 w", content1);

        var annotBorderOnly = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Square"),
            [new PdfName("Rect")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(50), new PdfInteger(50)),
            [new PdfName("Border")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(7)),
            [new PdfName("C")] = new PdfArray(new PdfInteger(1), new PdfInteger(0), new PdfInteger(0))
        };
        AnnotationAppearanceGenerator.Generate(doc, annotBorderOnly);
        var apDict2 = (PdfDictionary)annotBorderOnly[new PdfName("AP")];
        var nRef2 = (PdfIndirectReference)apDict2[new PdfName("N")];
        var nStream2 = (PdfStream)doc.GetObject(nRef2.ObjectNumber)!;
        string content2 = Encoding.ASCII.GetString(nStream2.GetDecodedData());
        Assert.Contains("7 w", content2);
    }

    [Fact]
    public void Issue379_NegativeGenerationNumber_ThrowsPdfParseException()
    {
        byte[] input = Encoding.ASCII.GetBytes("1 -1 R");
        using var ms = new MemoryStream(input);
        var parser = new PdfParser(ms);
        Assert.Throws<PdfParseException>(() => parser.ReadObject());
    }

    // ─── Part 2: PDF 2.0 (v2.0)-Scoped Conformance & Feature Updates ────────────

    [Fact]
    public void Issue246_720_XRefStreamWithFKey_RejectedInPdf20_AllowedInPdf17()
    {
        // Build a minimal XRef stream object containing /F (external file specification)
        const string xrefStreamText =
            "1 0 obj\n" +
            "<< /Type /XRef /Size 2 /W [1 2 1] /F (external.bin) /Length 8 >>\n" +
            "stream\n" +
            "\x00\x00\x00\x00\x01\x00\x10\x00\n" +
            "endstream\n" +
            "endobj\n";
        byte[] bytes = Encoding.Latin1.GetBytes(xrefStreamText);

        // PDF 1.7 document: does not throw for /F in XRef stream
        var doc17 = new PdfDocument { Version = PdfVersion.Pdf17 };
        using (var ms17 = new MemoryStream(bytes))
        {
            var parser17 = new PdfXrefParser(ms17, doc17);
            PdfXrefParseResult res17 = parser17.Parse();
            Assert.True(res17.IsXRefStream);
        }

        // PDF 2.0 document: throws PdfParseException per EC-3 Issues #246 & #720
        var doc20 = new PdfDocument { Version = PdfVersion.Pdf20 };
        using (var ms20 = new MemoryStream(bytes))
        {
            var parser20 = new PdfXrefParser(ms20, doc20);
            var ex = Assert.Throws<PdfParseException>(() => parser20.Parse());
            Assert.Contains("/F", ex.Message);
        }
    }

    [Fact]
    public void Issue246_720_XRefStreamWithIndirectFilter_RejectedInPdf20()
    {
        const string xrefStreamText =
            "1 0 obj\n" +
            "<< /Type /XRef /Size 2 /W [1 2 1] /Filter 5 0 R /Length 8 >>\n" +
            "stream\n" +
            "\x00\x00\x00\x00\x01\x00\x10\x00\n" +
            "endstream\n" +
            "endobj\n";
        byte[] bytes = Encoding.Latin1.GetBytes(xrefStreamText);

        var doc20 = new PdfDocument { Version = PdfVersion.Pdf20 };
        using var ms20 = new MemoryStream(bytes);
        var parser20 = new PdfXrefParser(ms20, doc20);
        var ex = Assert.Throws<PdfParseException>(() => parser20.Parse());
        Assert.Contains("/Filter", ex.Message);
    }

    [Fact]
    public void Issue379_IndirectObjectLeadingZeros_FlaggedInPdf20_IgnoredInPdf17()
    {
        const string rawPdf =
            "%PDF-2.0\n" +
            "01 0 obj\n" +
            "<< /Type /Catalog >>\n" +
            "endobj\n";
        byte[] bytes = Encoding.ASCII.GetBytes(rawPdf);
        int objOffset = rawPdf.IndexOf("01 0 obj", StringComparison.Ordinal);

        var rule = new IndirectObjectSpacingRule();

        // PDF 1.7: leading zero on object number is not flagged by 6.1.9 spacing rule
        var doc17 = new PdfDocument { Version = PdfVersion.Pdf17 };
        doc17.XrefTable.Add(new PdfXrefEntry(1, objOffset, 0, true, PdfXrefEntryType.Uncompressed));
        var ctx17 = new ConformanceContext(doc17, ConformanceProfile.PdfA2b, bytes);
        Assert.Empty(rule.Check(ctx17));

        // PDF 2.0: leading zero on object number is flagged per ISO 32000-2 (EC-3 Issue #379)
        var doc20 = new PdfDocument { Version = PdfVersion.Pdf20 };
        doc20.XrefTable.Add(new PdfXrefEntry(1, objOffset, 0, true, PdfXrefEntryType.Uncompressed));
        var ctx20 = new ConformanceContext(doc20, ConformanceProfile.PdfA2b, bytes);
        List<Finding> findings = rule.Check(ctx20).ToList();
        Assert.Single(findings);
        Assert.Contains("leading zeros", findings[0].Message);
    }

    [Fact]
    public void Issue577_AnnotationFillOpacityCa_DefaultsToStrokeCaInPdf20()
    {
        // When /CA is 0.4 and /ca is omitted, /ca defaults to 0.4 in PDF 2.0
        var doc20 = new PdfDocument { Version = PdfVersion.Pdf20 };
        var annot = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Square"),
            [new PdfName("Rect")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(50), new PdfInteger(50)),
            [new PdfName("CA")] = new PdfReal(0.4),
            [new PdfName("C")] = new PdfArray(new PdfInteger(1), new PdfInteger(0), new PdfInteger(0)),
            [new PdfName("IC")] = new PdfArray(new PdfInteger(0), new PdfInteger(1), new PdfInteger(0))
        };
        AnnotationAppearanceGenerator.Generate(doc20, annot);
        var apDict = (PdfDictionary)annot[new PdfName("AP")];
        var nRef = (PdfIndirectReference)apDict[new PdfName("N")];
        var nStream = (PdfStream)doc20.GetObject(nRef.ObjectNumber)!;
        var res = (PdfDictionary)nStream.Dictionary[new PdfName("Resources")];
        var extGs = (PdfDictionary)res[new PdfName("ExtGState")];
        var gsCa = (PdfDictionary)extGs[new PdfName("GSca")];

        Assert.Equal(0.4, ((PdfReal)gsCa[new PdfName("CA")]).Value, 3);
        Assert.Equal(0.4, ((PdfReal)gsCa[new PdfName("ca")]).Value, 3);

        // When /ca is explicitly 0.8 in PDF 2.0, /ca is 0.8 while /CA is 0.4
        var annotExplicit = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Square"),
            [new PdfName("Rect")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(50), new PdfInteger(50)),
            [new PdfName("CA")] = new PdfReal(0.4),
            [new PdfName("ca")] = new PdfReal(0.8),
            [new PdfName("C")] = new PdfArray(new PdfInteger(1), new PdfInteger(0), new PdfInteger(0))
        };
        AnnotationAppearanceGenerator.Generate(doc20, annotExplicit);
        var apDict2 = (PdfDictionary)annotExplicit[new PdfName("AP")];
        var nRef2 = (PdfIndirectReference)apDict2[new PdfName("N")];
        var nStream2 = (PdfStream)doc20.GetObject(nRef2.ObjectNumber)!;
        var gsCa2 = (PdfDictionary)((PdfDictionary)((PdfDictionary)nStream2.Dictionary[new PdfName("Resources")])[new PdfName("ExtGState")])[new PdfName("GSca")];
        Assert.Equal(0.4, ((PdfReal)gsCa2[new PdfName("CA")]).Value, 3);
        Assert.Equal(0.8, ((PdfReal)gsCa2[new PdfName("ca")]).Value, 3);
    }

    [Fact]
    public void Issue444_PolygonAnnotation_PathOverridesVerticesInPdf20_UsesVerticesInPdf17()
    {
        PdfDictionary CreatePolygonAnnot() => new()
        {
            [new PdfName("Subtype")] = new PdfName("Polygon"),
            [new PdfName("Rect")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100)),
            [new PdfName("Vertices")] = new PdfArray(
                new PdfInteger(11), new PdfInteger(12),
                new PdfInteger(33), new PdfInteger(44),
                new PdfInteger(55), new PdfInteger(66)),
            [new PdfName("Path")] = new PdfArray(
                new PdfArray(new PdfInteger(77), new PdfInteger(88)),
                new PdfArray(new PdfInteger(90), new PdfInteger(95))),
            [new PdfName("C")] = new PdfArray(new PdfInteger(1), new PdfInteger(0), new PdfInteger(0))
        };

        // PDF 2.0: /Path overrides /Vertices
        var doc20 = new PdfDocument { Version = PdfVersion.Pdf20 };
        PdfDictionary annot20 = CreatePolygonAnnot();
        AnnotationAppearanceGenerator.Generate(doc20, annot20);
        var nRef20 = (PdfIndirectReference)((PdfDictionary)annot20[new PdfName("AP")])[new PdfName("N")];
        string content20 = Encoding.ASCII.GetString(((PdfStream)doc20.GetObject(nRef20.ObjectNumber)!).GetDecodedData());
        Assert.Contains("77 88 m", content20);
        Assert.DoesNotContain("11 12 m", content20);

        // PDF 1.7: /Vertices is used
        var doc17 = new PdfDocument { Version = PdfVersion.Pdf17 };
        PdfDictionary annot17 = CreatePolygonAnnot();
        AnnotationAppearanceGenerator.Generate(doc17, annot17);
        var nRef17 = (PdfIndirectReference)((PdfDictionary)annot17[new PdfName("AP")])[new PdfName("N")];
        string content17 = Encoding.ASCII.GetString(((PdfStream)doc17.GetObject(nRef17.ObjectNumber)!).GetDecodedData());
        Assert.Contains("11 12 m", content17);
        Assert.DoesNotContain("77 88 m", content17);
    }

    [Fact]
    public void Issue474_FontDescriptorFontWeight_Validates1To1000RangeInPdf20()
    {
        var doc20 = new PdfDocument { Version = PdfVersion.Pdf20 };
        var validDict = new PdfDictionary { [new PdfName("FontWeight")] = new PdfInteger(700) };
        Assert.Equal(700, new PdfFontDescriptor(validDict, doc20).FontWeight);

        var outOfRangeHigh = new PdfDictionary { [new PdfName("FontWeight")] = new PdfInteger(1200) };
        Assert.Equal(400, new PdfFontDescriptor(outOfRangeHigh, doc20).FontWeight);

        var outOfRangeZero = new PdfDictionary { [new PdfName("FontWeight")] = new PdfInteger(0) };
        Assert.Equal(400, new PdfFontDescriptor(outOfRangeZero, doc20).FontWeight);
    }
}
