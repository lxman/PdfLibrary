using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;

namespace PdfLibrary.Content;

/// <summary>
/// Categorises the font pathway for subsetting decisions.
/// </summary>
internal enum FontUsageKind
{
    /// <summary>Simple /Subtype /TrueType font with single-byte codes and /FontFile2.</summary>
    SimpleTrueType,
    /// <summary>/Subtype /Type0 with /Encoding /Identity-H or /Identity-V and CIDFontType2 descendant.</summary>
    IdentityCidType2,
}

/// <summary>
/// Records per-font-program glyph usage found while scanning content streams.
/// Keyed by the /FontFile2 PdfStream object (identity).
/// </summary>
internal sealed class FontUsage
{
    public required PdfFontDescriptor Descriptor { get; init; }
    /// <summary>Raw dictionary of the descendant CIDFont (Type0 only; null for simple TrueType).</summary>
    public PdfDictionary? DescendantCidFontDict { get; init; }
    public required FontUsageKind Kind { get; init; }
    public HashSet<ushort> Gids { get; } = new();
}

/// <summary>
/// Scans a single page's content stream and accumulates glyph IDs for every
/// embedded TrueType /FontFile2 program used on that page.
///
/// Modelled directly on <see cref="PdfTextExtractor"/>: mirror its ctor signature,
/// font-resolution pattern and <c>OnShowText</c> / <c>OnShowTextWithPositioning</c>
/// dispatch.  The private-protected overrides are in the same assembly so access is fine.
/// </summary>
internal sealed class GlyphUsageCollector : PdfContentProcessor
{
    private readonly PdfResources? _resources;
    private readonly PdfDocument? _document;

    // Accumulates usage keyed by the font-file PdfStream object (reference equality).
    private readonly Dictionary<PdfStream, FontUsage> _result = new(ReferenceEqualityComparer.Instance);

    public GlyphUsageCollector(PdfResources? resources, PdfDocument? document)
    {
        _resources = resources;
        _document = document;
    }

    /// <summary>
    /// Collected glyph usage per /FontFile2 stream.
    /// Call after processing all content streams for a page.
    /// </summary>
    public IReadOnlyDictionary<PdfStream, FontUsage> Result => _result;

    // -----------------------------------------------------------------
    // Text-show overrides
    // -----------------------------------------------------------------

    private protected override void OnShowText(PdfString text)
    {
        AccumulateGlyphs(text.Bytes);
    }

    private protected override void OnShowTextWithPositioning(PdfArray array)
    {
        foreach (PdfObject item in array)
        {
            if (item is PdfString str)
                AccumulateGlyphs(str.Bytes);
        }
    }

    // -----------------------------------------------------------------
    // Core accumulation logic
    // -----------------------------------------------------------------

    private void AccumulateGlyphs(byte[] bytes)
    {
        if (_resources is null || string.IsNullOrEmpty(CurrentState.FontName))
            return;

        PdfFont? font = _resources.GetFontObject(CurrentState.FontName);
        if (font is null)
            return;

        switch (font)
        {
            case Type0Font t0 when IsIdentityHOrV(t0):
                AccumulateType0Identity(t0, bytes);
                break;

            case TrueTypeFont tt:
                AccumulateSimpleTrueType(tt, bytes);
                break;
        }
    }

    // -----------------------------------------------------------------
    // Identity-H / Identity-V CIDFontType2
    // -----------------------------------------------------------------

    private static bool IsIdentityHOrV(Type0Font font)
    {
        string? enc = font.Encoding;
        return enc is "Identity-H" or "Identity-V";
    }

    private void AccumulateType0Identity(Type0Font font, byte[] bytes)
    {
        PdfFontDescriptor? descriptor = font.DescendantDescriptor;
        if (descriptor is null)
            return;

        PdfStream? fontFile2 = descriptor.GetFontFile2Stream();
        if (fontFile2 is null)
            return;

        // Check the descendant is actually CIDFontType2 (TrueType-based CID font).
        PdfDictionary? cidDict = font.DescendantCidFontDictionary;
        if (cidDict is not null)
        {
            if (!cidDict.TryGetValue(new PdfName("Subtype"), out PdfObject? stObj) ||
                stObj is not PdfName { Value: "CIDFontType2" })
                return; // Not the right subtype
        }

        FontUsage usage = GetOrAddUsage(fontFile2, new FontUsage
        {
            Descriptor = descriptor,
            DescendantCidFontDict = cidDict,
            Kind = FontUsageKind.IdentityCidType2,
        });

        // Identity-H: 2-byte big-endian code = CID = GID
        for (int i = 0; i + 1 < bytes.Length; i += 2)
        {
            ushort gid = (ushort)((bytes[i] << 8) | bytes[i + 1]);
            usage.Gids.Add(gid);
        }
        // If odd number of bytes, the trailing byte is not a complete 2-byte code —
        // skip it (shouldn't happen in valid PDFs, but avoid treating a lone byte as a GID).
    }

    // -----------------------------------------------------------------
    // Simple TrueType (single-byte codes)
    // -----------------------------------------------------------------

    private void AccumulateSimpleTrueType(TrueTypeFont font, byte[] bytes)
    {
        PdfFontDescriptor? descriptor = font.Descriptor;
        if (descriptor is null)
            return;

        PdfStream? fontFile2 = descriptor.GetFontFile2Stream();
        if (fontFile2 is null)
            return;

        EmbeddedFontMetrics? metrics = font.GetEmbeddedMetrics();

        FontUsage usage = GetOrAddUsage(fontFile2, new FontUsage
        {
            Descriptor = descriptor,
            DescendantCidFontDict = null,
            Kind = FontUsageKind.SimpleTrueType,
        });

        foreach (byte b in bytes)
        {
            ushort gid = metrics is { IsValid: true }
                ? metrics.GetGlyphId(b)
                : b; // fallback: treat byte as GID (common for simple subsets)
            usage.Gids.Add(gid);
        }
    }

    // -----------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------

    private FontUsage GetOrAddUsage(PdfStream fontFile2, FontUsage prototype)
    {
        if (_result.TryGetValue(fontFile2, out FontUsage? existing))
            return existing;
        _result[fontFile2] = prototype;
        return prototype;
    }
}
