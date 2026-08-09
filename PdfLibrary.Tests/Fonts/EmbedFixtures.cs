using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Document and font-program fixtures for <see cref="FontRemediationPlannerEmbedTests"/>. Mirrors
/// <c>FontRemediationPlannerTests</c>'s convention of hand-building documents directly with
/// <see cref="PdfDocument.AddObject"/> (no <c>TestFixtures.Path(...)</c> helper exists in this
/// project) and the repo-wide convention (<c>FontProgramClassifierTests</c>,
/// <c>FontDescriptorMetricsTests</c>, <c>PdfDocumentEditorEmbedProgramTests</c>) of resolving REAL
/// system fonts through <see cref="SystemFontLocator"/> rather than hand-assembling a synthetic sfnt
/// with a full OS/2/hmtx/glyf table set — <see cref="EmbeddedFontMetrics.IsValid"/> requires head +
/// hmtx + glyph outlines, which a bare-bones synthetic builder does not carry.
///
/// <para>Object numbers are fixed literals, matching every fixture below one-to-one — the same
/// convention <c>FontRemediationPlannerTests</c> uses (font dict always 30, content stream always
/// 11, page always 3, and so on).</para>
/// </summary>
internal static class EmbedFixtures
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    /// <summary>A single unembedded simple TrueType font, <c>/BaseFont /Arial</c> — resolvable
    /// through the real <see cref="SystemFontLocator"/> on a machine with Arial installed (every
    /// Windows dev/CI box this suite targets).</summary>
    public static PdfDocument UnembeddedArial() => UnembeddedNamed("Arial");

    /// <summary>A single unembedded simple TrueType font under an arbitrary <c>/BaseFont</c> — used
    /// where the fixture's own font name must NOT be the one actually resolved (the
    /// resolved-bytes-not-the-request tests), or where no system match is expected at all.</summary>
    public static PdfDocument UnembeddedNamed(string baseFont)
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N(baseFont),
            [N("Encoding")] = N("WinAnsiEncoding"),
            [N("FirstChar")] = new PdfInteger(65),
            [N("LastChar")] = new PdfInteger(65),
            [N("Widths")] = new PdfArray(new PdfInteger(722)),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf (A) Tj ET")));
        AddSinglePageCatalog(doc, font: 30);
        return doc;
    }

    /// <summary>An unembedded composite font: Type0 wrapper (object 20, indirect) over an INDIRECT
    /// CIDFontType2 descendant (object 22) — both indirect, so <c>IsAddressable</c> is true and the
    /// planner reaches the composite-kind decline rather than the unaddressable one.</summary>
    public static PdfDocument UnembeddedType0()
    {
        var doc = new PdfDocument();
        doc.AddObject(22, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("CompositeX"),
            [N("CIDSystemInfo")] = new PdfDictionary
            {
                [N("Registry")] = new PdfString(Encoding.ASCII.GetBytes("Adobe")),
                [N("Ordering")] = new PdfString(Encoding.ASCII.GetBytes("Identity")),
                [N("Supplement")] = new PdfInteger(0),
            },
            [N("FontDescriptor")] = new PdfDictionary
            {
                [N("Type")] = N("FontDescriptor"), [N("FontName")] = N("CompositeX"),
            },
        });
        doc.AddObject(20, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("CompositeX"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(22)),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf <0001> Tj ET")));
        AddSinglePageCatalog(doc, font: 20);
        return doc;
    }

    /// <summary>
    /// An UNADDRESSABLE font: an INDIRECT Type0 wrapper (object <see cref="UnaddressableObjectNumber"/>)
    /// over a DIRECT descendant CIDFont. Mirrors <c>FontRemediationPlannerTests</c>'s
    /// <c>BuildDirectDescendantType0Document</c>, and is the only shape that reaches
    /// <c>ProposeEmbed</c>'s <c>!entry.IsAddressable</c> branch through <c>Propose</c>: the finding
    /// can name the wrapper (indirect, so a <c>Finding.ObjectNumber</c> exists for it) while the
    /// PROGRAM HOLDER — the descendant — has no object number of its own to attach a program to.
    ///
    /// <para>A wholly direct simple font dictionary cannot do this job: it is never registered, so
    /// <c>FontInventory.Find</c> returns null for every object number a finding could name, the
    /// planner's <c>continue</c> fires, and <c>result.Fonts</c> comes back EMPTY — which is what made
    /// the previous version of this fixture's test pass vacuously over an empty collection.</para>
    /// </summary>
    public static PdfDocument UnaddressableType0()
    {
        var directDescendant = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("CIDFontX"),
            [N("CIDSystemInfo")] = new PdfDictionary
            {
                [N("Registry")] = new PdfString(Encoding.ASCII.GetBytes("Adobe")),
                [N("Ordering")] = new PdfString(Encoding.ASCII.GetBytes("Identity")),
                [N("Supplement")] = new PdfInteger(0),
            },
            [N("FontDescriptor")] = new PdfDictionary
            {
                [N("Type")] = N("FontDescriptor"), [N("FontName")] = N("CIDFontX"),
            },
        };
        Assert.False(directDescendant.IsIndirect); // guards the fixture's own premise

        var doc = new PdfDocument();
        doc.AddObject(UnaddressableObjectNumber, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("CIDFontX"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(directDescendant),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf <0001> Tj ET")));
        AddSinglePageCatalog(doc, font: UnaddressableObjectNumber);
        return doc;
    }

    /// <summary>The <see cref="UnaddressableType0"/> wrapper's own object number — the one a
    /// <c>font-embedded</c> finding on that document would carry.</summary>
    public const int UnaddressableObjectNumber = 20;

    /// <summary>Bytes that classify as <see cref="FontProgramFormat.OpenType"/> on their 'OTTO' magic
    /// alone but carry no readable sfnt table directory — so nothing can say which font dictionary
    /// ISO 32000-2 Table 124 would permit them in. Used to prove the planner DECLINES a program the
    /// editor's own reconciliation would refuse, rather than proposing one that throws at Save time.
    /// </summary>
    public static byte[] UnreadableOpenTypeProgram() =>
        [0x4F, 0x54, 0x54, 0x4F, 0xFF, 0xFF, 0xFF, 0xFF, 0x01, 0x02, 0x03, 0x04];

    /// <summary>The simple font's own object number — 30 in every fixture that has one.</summary>
    public static int FontObjectNumber(PdfDocument document) => 30;

    /// <summary>For a simple font the program holder IS the font dictionary — same object number.</summary>
    public static int ProgramHolderObjectNumber(PdfDocument document) => 30;

    /// <summary>The Type0 composite fixture's descendant CIDFont object number.</summary>
    public static int DescendantObjectNumber(PdfDocument document) => 22;

    /// <summary>The number of indirect objects currently registered — used to prove the planner adds
    /// none.</summary>
    public static int ObjectCount(PdfDocument document) => document.Objects.Count;

    /// <summary>True when ANY font in the document now carries an embedded program — used to prove
    /// the planner never writes one.</summary>
    public static bool HasFontFile(PdfDocument document) =>
        FontInventory.Read(document).Any(e => e.IsEmbedded);

    /// <summary>Real "Courier New" bytes, resolved from the machine's installed fonts — used as the
    /// "wrong but real" answer a fuzzy locator ladder can return for a request it did not actually
    /// satisfy.</summary>
    public static byte[] CourierBytes()
    {
        FontMatch? match = SystemFontLocator.Default.Resolve(new FontRequest("Courier New", Bold: false, Italic: false));
        if (match is null)
            throw new InvalidOperationException("No Courier New font found on this machine to build the fixture from.");
        return match.Data;
    }

    /// <summary>Real Arial bytes with the OS/2 table's <c>fsType</c> bit 1 (Restricted License
    /// Embedding) forced on — two bytes patched in place, exactly as the design brief specifies. Does
    /// not depend on a synthetic OS/2 builder: Arial's own OS/2 table is located via the sfnt table
    /// directory and patched at its own <c>fsType</c> offset (byte 8 of the table, per
    /// <see cref="Os2Table"/>).</summary>
    public static byte[] RestrictedEmbeddingFont()
    {
        FontMatch? match = SystemFontLocator.Default.Resolve(new FontRequest("Arial", Bold: false, Italic: false));
        if (match is null)
            throw new InvalidOperationException("No Arial font found on this machine to build the fixture from.");

        byte[] bytes = (byte[])match.Data.Clone();
        int numTables = (bytes[4] << 8) | bytes[5];
        for (var t = 0; t < numTables; t++)
        {
            int recordOffset = 12 + t * 16;
            string tag = Encoding.ASCII.GetString(bytes, recordOffset, 4);
            if (tag != "OS/2") continue;

            uint tableOffset = ((uint)bytes[recordOffset + 8] << 24) | ((uint)bytes[recordOffset + 9] << 16)
                | ((uint)bytes[recordOffset + 10] << 8) | bytes[recordOffset + 11];
            int fsTypeOffset = (int)tableOffset + 8;
            var fsType = (ushort)((bytes[fsTypeOffset] << 8) | bytes[fsTypeOffset + 1]);
            fsType |= 0x0002;
            bytes[fsTypeOffset] = (byte)(fsType >> 8);
            bytes[fsTypeOffset + 1] = (byte)(fsType & 0xFF);
            return bytes;
        }

        throw new InvalidOperationException("Resolved Arial program has no OS/2 table to patch.");
    }

    private static void AddSinglePageCatalog(PdfDocument doc, int font)
    {
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(font) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
    }
}

/// <summary>Returns fixed bytes (or null) from <see cref="Resolve"/> regardless of what the request
/// asks for — the deterministic stand-in for a fuzzy-matching real locator, used to prove the
/// planner reports what it actually got rather than what it asked for. Every other member throws
/// <see cref="NotSupportedException"/>: the planner calls only <see cref="Resolve"/>, so if any of
/// these ever fire, the planner is doing something this plan did not intend.</summary>
internal sealed class StubFontProvider(byte[]? bytes) : ISystemFontProvider
{
    public IReadOnlyCollection<string> GetAvailableFontFamilies() =>
        throw new NotSupportedException("The planner does not call GetAvailableFontFamilies.");

    public bool IsFontAvailable(string familyName) =>
        throw new NotSupportedException("The planner does not call IsFontAvailable.");

    public string? FindFirstAvailable(IEnumerable<string> candidates) =>
        throw new NotSupportedException("The planner does not call FindFirstAvailable.");

    public void RefreshCache() =>
        throw new NotSupportedException("The planner does not call RefreshCache.");

    public FontMatch? Resolve(FontRequest request) => bytes is null ? null : new FontMatch(bytes, 0);
}
