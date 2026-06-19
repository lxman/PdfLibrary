using FontParser.Tables.Cff.Type1;
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
    /// <summary>Simple /Subtype /Type1 font with single-byte codes and a Type1C /FontFile3 (CFF).</summary>
    SimpleType1C,
    /// <summary>/Subtype /Type0, Identity-H/V, descendant /CIDFontType0 with a /FontFile3 (CID-keyed CFF).</summary>
    IdentityCidType0,
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

    // Parsed CFF per /FontFile3 stream (cached; null = unparseable). Used to map glyph names -> GIDs.
    private readonly Dictionary<PdfStream, Type1Table?> _cffCache = new(ReferenceEqualityComparer.Instance);

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

    /// <summary>
    /// Recurse into Form XObjects so glyph usage inside forms is collected too. Without this, a font
    /// used only inside a form (e.g. an imposed page) would have its glyphs dropped on subsetting.
    /// Mirrors <see cref="PdfTextExtractor"/>'s form handling.
    /// </summary>
    protected override void OnInvokeXObject(string name)
    {
        PdfStream? xobject = _resources?.GetXObject(name);
        if (xobject is null || PdfImage.IsImageXObject(xobject) || !IsFormXObject(xobject))
            return;

        byte[] contentData = xobject.GetDecodedData(_document?.Decryptor);

        PdfResources? formResources = _resources;
        if (xobject.Dictionary.TryGetValue(new PdfName("Resources"), out PdfObject resObj))
        {
            if (resObj is PdfIndirectReference r && _document is not null)
                resObj = _document.ResolveReference(r);
            if (resObj is PdfDictionary resDict)
                formResources = new PdfResources(resDict, _document);
        }

        var nested = new GlyphUsageCollector(formResources, _document);
        nested.ProcessOperators(PdfContentParser.Parse(contentData));

        foreach ((PdfStream fs, FontUsage u) in nested.Result)
        {
            if (!_result.TryGetValue(fs, out FontUsage? existing))
                _result[fs] = u;
            else
                foreach (ushort gid in u.Gids)
                    existing.Gids.Add(gid);
        }
    }

    private static bool IsFormXObject(PdfStream stream) =>
        stream.Dictionary.TryGetValue(new PdfName("Subtype"), out PdfObject obj) && obj is PdfName { Value: "Form" };

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
                AccumulateType0Identity(t0, bytes);  // CIDFontType2 (TrueType /FontFile2)
                AccumulateType0CidType0(t0, bytes);  // CIDFontType0 (CFF /FontFile3)
                break;

            case TrueTypeFont tt:
                AccumulateSimpleTrueType(tt, bytes);
                break;

            case Type1Font t1:
                AccumulateSimpleType1C(t1, bytes);
                break;
        }
    }

    // -----------------------------------------------------------------
    // Simple Type1C (single-byte codes, /FontFile3 CFF)
    // -----------------------------------------------------------------

    private void AccumulateSimpleType1C(Type1Font font, byte[] bytes)
    {
        PdfFontDescriptor? descriptor = font.GetDescriptor();
        PdfStream? fontFile3 = descriptor?.GetFontFile3Stream();
        if (descriptor is null || fontFile3 is null)
            return;

        Type1Table? cff = GetCff(fontFile3);
        if (cff is null || cff.IsCid)
            return; // unparseable, or CID-keyed (handled via the Type0 path)

        FontUsage usage = GetOrAddUsage(fontFile3, new FontUsage
        {
            Descriptor = descriptor,
            DescendantCidFontDict = null,
            Kind = FontUsageKind.SimpleType1C,
        });

        // Single-byte code -> glyph name (PDF /Encoding) -> GID (CFF charset). GID 0 (.notdef) is
        // always retained by the subsetter, so codes that resolve to it are simply not added here.
        foreach (byte b in bytes)
        {
            string? name = font.Encoding?.GetGlyphName(b);
            if (string.IsNullOrEmpty(name))
                continue;
            int gid = cff.GetGlyphIndexByName(name);
            if (gid > 0)
                usage.Gids.Add((ushort)gid);
        }
    }

    private Type1Table? GetCff(PdfStream fontFile3)
    {
        if (_cffCache.TryGetValue(fontFile3, out Type1Table? cached))
            return cached;
        Type1Table? table;
        try { table = new Type1Table(fontFile3.GetDecodedData(_document?.Decryptor)); }
        catch { table = null; }
        _cffCache[fontFile3] = table;
        return table;
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
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            var gid = (ushort)((bytes[i] << 8) | bytes[i + 1]);
            usage.Gids.Add(gid);
        }
        // If odd number of bytes, the trailing byte is not a complete 2-byte code —
        // skip it (shouldn't happen in valid PDFs, but avoid treating a lone byte as a GID).
    }

    // -----------------------------------------------------------------
    // Identity-H / Identity-V CIDFontType0 (CID-keyed CFF, /FontFile3)
    // -----------------------------------------------------------------

    private void AccumulateType0CidType0(Type0Font font, byte[] bytes)
    {
        PdfDictionary? cidDict = font.DescendantCidFontDictionary;
        if (cidDict is null ||
            !cidDict.TryGetValue(new PdfName("Subtype"), out PdfObject? stObj) ||
            stObj is not PdfName { Value: "CIDFontType0" })
            return;

        PdfFontDescriptor? descriptor = font.DescendantDescriptor;
        PdfStream? fontFile3 = descriptor?.GetFontFile3Stream();
        if (descriptor is null || fontFile3 is null)
            return;

        Type1Table? cff = GetCff(fontFile3);
        if (cff is null || !cff.IsCid)
            return;

        FontUsage usage = GetOrAddUsage(fontFile3, new FontUsage
        {
            Descriptor = descriptor,
            DescendantCidFontDict = cidDict,
            Kind = FontUsageKind.IdentityCidType0,
        });

        // Identity-H/V: 2-byte big-endian code = CID -> GID via the CFF charset.
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            int cid = (bytes[i] << 8) | bytes[i + 1];
            int gid = cff.GetGlyphIndexByCid(cid);
            if (gid > 0)
                usage.Gids.Add((ushort)gid);
        }
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
