using FontParser.Tables.Cff.Type1;
using FontParser.Tables.Head;
using FontParser.Tables.Hhea;
using FontParser.Tables.Os2;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Fonts;

/// <summary>
/// The values a real <c>/FontDescriptor</c> needs, measured from the font program itself rather
/// than guessed. Field names are consumed by name (Task 5 writes them straight into a PDF
/// dictionary), so they are not cosmetic.
/// </summary>
public sealed record FontDescriptorValues(
    int[] FontBBox,        // exactly 4 entries, 1000-unit glyph space
    double ItalicAngle,
    int Ascent,
    int Descent,
    int CapHeight,
    int StemV,
    int MissingWidth,
    string StemVSource);   // "cff-stdvw" | "measured-I" | "weight-class"

/// <summary>
/// Design §5.2: once a program is embedded, a <c>/FontDescriptor</c> describing something else is
/// both wrong and a conformance violation in its own right. This computes the replacement from the
/// program's own tables — <c>head</c>, <c>hhea</c>, <c>OS/2</c>, <c>post</c>, the CFF Top/Private
/// DICTs, and, as a last resort, measured glyph outlines — instead of the guesses that used to be
/// written verbatim (<c>const int llx = -200</c>, a hardcoded <c>/StemV 80</c>, <c>CapHeight</c> as
/// <c>Ascender * 0.7</c>).
/// </summary>
public static class FontDescriptorMetrics
{
    /// <summary>
    /// Computes a <see cref="FontDescriptorValues"/> from <paramref name="program"/>, or null when
    /// the bytes will not parse as a loadable font program. Never returns a descriptor of zeroes —
    /// that would be worse than the wrong-but-plausible fake it replaces, because it would still get
    /// written into a real PDF.
    /// </summary>
    public static FontDescriptorValues? Compute(byte[] program, FontProgramFormat format)
    {
        EmbeddedFontMetrics metrics = format == FontProgramFormat.Type1
            ? new EmbeddedFontMetrics(program, length1: 0, length2: 0, length3: 0)
            : new EmbeddedFontMetrics(program);

        if (!metrics.IsValid || metrics.UnitsPerEm == 0)
            return null;

        double scale = 1000.0 / metrics.UnitsPerEm;

        int[] fontBBox = ComputeFontBBox(metrics, scale);
        double italicAngle = metrics.Post?.ItalicAngle ?? 0.0;
        (int ascent, int descent) = ComputeAscentDescent(metrics, scale, fontBBox);
        int capHeight = ComputeCapHeight(metrics, scale, ascent);
        (int stemV, string stemVSource) = ComputeStemV(metrics, scale);
        var missingWidth = (int)Math.Round(metrics.GetAdvanceWidth(0) * scale);

        return new FontDescriptorValues(
            fontBBox, italicAngle, ascent, descent, capHeight, stemV, missingWidth, stemVSource);
    }

    private static int RoundScale(double fontUnits, double scale) => (int)Math.Round(fontUnits * scale);

    /// <summary>/FontBBox: head.XMin/YMin/XMax/YMax, else the CFF Top DICT FontBBox, else the union
    /// of every glyph's own bounding box.</summary>
    private static int[] ComputeFontBBox(EmbeddedFontMetrics metrics, double scale)
    {
        HeadTable? head = metrics.Head;
        if (head is not null)
        {
            return
            [
                RoundScale(head.XMin, scale),
                RoundScale(head.YMin, scale),
                RoundScale(head.XMax, scale),
                RoundScale(head.YMax, scale)
            ];
        }

        IReadOnlyList<double>? cffBBox = metrics.Cff?.FontBBox;
        if (cffBBox is { Count: 4 } && (cffBBox[0] != 0 || cffBBox[1] != 0 || cffBBox[2] != 0 || cffBBox[3] != 0))
        {
            return
            [
                RoundScale(cffBBox[0], scale),
                RoundScale(cffBBox[1], scale),
                RoundScale(cffBBox[2], scale),
                RoundScale(cffBBox[3], scale)
            ];
        }

        return ComputeGlyphUnionBBox(metrics, scale);
    }

    /// <summary>Last-resort /FontBBox: walk every glyph outline and union their bounding boxes.
    /// Bounded to the first 2000 glyphs — a program with no head table and no CFF FontBBox is
    /// already an unusual shape, and this exists to answer something plausible, not to be exact.
    /// </summary>
    private static int[] ComputeGlyphUnionBBox(EmbeddedFontMetrics metrics, double scale)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var any = false;

        int limit = Math.Min((int)metrics.NumGlyphs, 2000);
        for (var gid = 0; gid < limit; gid++)
        {
            GlyphOutline? outline = metrics.GetGlyphOutline((ushort)gid);
            if (outline is null || outline.IsEmpty) continue;

            any = true;
            minX = Math.Min(minX, outline.Metrics.XMin);
            minY = Math.Min(minY, outline.Metrics.YMin);
            maxX = Math.Max(maxX, outline.Metrics.XMax);
            maxY = Math.Max(maxY, outline.Metrics.YMax);
        }

        return any
            ? [RoundScale(minX, scale), RoundScale(minY, scale), RoundScale(maxX, scale), RoundScale(maxY, scale)]
            : [0, 0, 0, 0];
    }

    /// <summary>/Ascent and /Descent: OS/2 sTypo{Ascender,Descender} when non-zero, else hhea's.
    /// Descent is forced negative — a positive descender is itself a conformance violation, and both
    /// sources are negative in a well-formed font.
    /// <para>Extension beyond the literal source table: a bare CFF program (Type1C/CIDFontType0C,
    /// no sfnt wrapper) has neither an OS/2 nor an hhea table at all — the ladder's assumption that
    /// hhea is always present holds only for sfnt-wrapped programs. When both are entirely absent,
    /// this falls back to the already-computed /FontBBox top/bottom rather than silently reporting
    /// zero for both fields.</para></summary>
    private static (int Ascent, int Descent) ComputeAscentDescent(
        EmbeddedFontMetrics metrics, double scale, int[] fontBBox)
    {
        Os2Table? os2 = metrics.Os2;
        HheaTable? hhea = metrics.Hhea;

        if (os2 is null && hhea is null)
        {
            // Neither table exists (bare CFF, no sfnt wrapper) — derive from the measured FontBBox.
            int bboxAscent = fontBBox[3];
            int bboxDescent = fontBBox[1] < 0 ? fontBBox[1] : -fontBBox[1];
            return (bboxAscent, bboxDescent);
        }

        int ascentUnits = os2 is not null && os2.STypoAscender != 0 ? os2.STypoAscender : hhea?.Ascender ?? 0;
        int descentUnits = os2 is not null && os2.STypoDescender != 0 ? os2.STypoDescender : hhea?.Descender ?? 0;

        int ascent = RoundScale(ascentUnits, scale);
        int descent = RoundScale(descentUnits, scale);
        if (descent > 0) descent = -descent;

        return (ascent, descent);
    }

    /// <summary>/CapHeight: OS/2 sCapHeight (version >= 2, non-zero), else the measured top of the
    /// 'H' glyph's bounding box, else 0.7 * Ascent.</summary>
    private static int ComputeCapHeight(EmbeddedFontMetrics metrics, double scale, int ascent)
    {
        Os2Table? os2 = metrics.Os2;
        if (os2 is { Version: >= 2, SCapHeight: not 0 })
            return RoundScale(os2.SCapHeight, scale);

        GlyphOutline? hOutline = GetGlyphOutlineFor(metrics, 'H', "H");
        if (hOutline is not null && !hOutline.IsEmpty)
            return RoundScale(hOutline.Metrics.YMax, scale);

        return (int)Math.Round(ascent * 0.7);
    }

    /// <summary>/StemV: the CFF Private DICT StdVW when the program is CFF-flavoured, else the
    /// measured width of the 'I' glyph's bounding box, else the weight-class formula. The returned
    /// source tag exists so a caller can tell which one actually answered — silently falling to the
    /// weight-class guess on a font that carries a real StdVW would be exactly the quiet degradation
    /// this field is for.</summary>
    private static (int StemV, string Source) ComputeStemV(EmbeddedFontMetrics metrics, double scale)
    {
        if (metrics.IsCffFont)
        {
            int? stdVw = metrics.Cff?.StdVW;
            if (stdVw is > 0)
                return (RoundScale(stdVw.Value, scale), "cff-stdvw");
        }

        GlyphOutline? iOutline = GetGlyphOutlineFor(metrics, 'I', "I");
        if (iOutline is not null && !iOutline.IsEmpty)
        {
            int width = iOutline.Metrics.XMax - iOutline.Metrics.XMin;
            if (width > 0)
                return (RoundScale(width, scale), "measured-I");
        }

        double weightClass = metrics.Os2?.UsWeightClass is > 0 ? metrics.Os2.UsWeightClass : 400;
        double raw = 50 + Math.Pow(weightClass / 100.0, 2) * 3;
        var clamped = (int)Math.Round(Math.Clamp(raw, 50, 220));
        return (clamped, "weight-class");
    }

    /// <summary>Resolves and loads the outline for a named Latin glyph across every program shape
    /// this library reads: a Type1 program (name lookup through the Type1 parser), a CFF program
    /// with a name-resolving charset (non-CID; CID-keyed CFF has no glyph names, so this yields
    /// null for it), and a TrueType/OpenType program (cmap lookup by Unicode code point).</summary>
    private static GlyphOutline? GetGlyphOutlineFor(EmbeddedFontMetrics metrics, char ch, string glyphName)
    {
        if (metrics.IsType1Font)
            return metrics.GetGlyphOutlineByName(glyphName);

        if (metrics.IsCffFont)
        {
            ushort gid = metrics.GetGlyphIdByName(glyphName);
            return gid == 0 ? null : metrics.GetGlyphOutline(gid);
        }

        ushort codeGid = metrics.GetGlyphId(ch);
        return codeGid == 0 ? null : metrics.GetGlyphOutline(codeGid);
    }
}
