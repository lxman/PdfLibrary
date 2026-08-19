using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts;

/// <summary>
/// Represents a CIDFont (Character Identifier font)
/// Used as a descendant font of Type 0 fonts
/// </summary>
internal class CidFont : PdfFont
{
    private double _defaultWidth = 1000;
    private Dictionary<int, double>? _widths;
    private Dictionary<int, int>? _cidToGidMap;
    private bool _isIdentityMapping;
    private int _cidToGidMapCoveredCids;

    public CidFont(PdfDictionary dictionary, PdfDocument? document = null)
        : base(dictionary, document)
    {
        LoadWidths();
        LoadCidToGidMap();
    }

    /// <summary>
    /// Maps a CID (Character ID) to a GID (Glyph ID) for RENDERING — what to actually draw.
    ///
    /// <para>For a CID beyond a /CIDToGIDMap stream's covered range this returns the CID itself
    /// (identity), which is a DELIBERATE divergence from ISO 32000-2 §9.7.6.3, not an oversight.
    /// Use <see cref="MapCidToGidStrict"/> for the spec answer; the two differ only in that one
    /// case and the difference is intentional (tracker issue 42).</para>
    ///
    /// <para><b>Why identity here.</b> Measured against all three reference renderers on
    /// 2026-08-19 (fixture: a corpus document whose /CIDToGIDMap was rewritten to cover CID 0 only,
    /// so every drawn CID falls beyond coverage). poppler and mutool render pixel-identically, both
    /// drawing the identity glyph; Ghostscript draws the same glyphs and adds .notdef boxes only
    /// where the resulting GID exceeds the subset font's glyph count — a separate concern, not this
    /// mapping. No renderer in the field substitutes CID 0's glyph. Returning 0 here would blank
    /// text that every other viewer displays, which is a worse answer for a viewer than following
    /// a sentence the field ignores.</para>
    /// </summary>
    public int MapCidToGid(int cid) => MapCid(cid, strict: false);

    /// <summary>
    /// Maps a CID to a GID for CONFORMANCE — what a validator must consider referenced.
    ///
    /// <para>Identical to <see cref="MapCidToGid"/> except for a CID beyond a /CIDToGIDMap stream's
    /// covered range, where this returns 0 (.notdef) per ISO 32000-2 §9.7.6.3: "if a (character)
    /// code does not have a corresponding GID in the CIDtoGIDMap stream, the glyph for CID 0 shall
    /// be substituted."</para>
    ///
    /// <para><b>Why strict here.</b> That sentence is ABSENT from ISO 32000-1's §9.7.6.3 — it is a
    /// PDF 2.0 addition — but veraPDF 1.28.1 applies it regardless: on the fixture described above
    /// it raises 6.2.11.8, 6.2.11.5, 6.2.11.4.1 and 6.2.11.4.2 which the untruncated original does
    /// not. A conformance rule reading the lenient answer therefore UNDER-REPORTS against the
    /// reference, which is the half of issue 42 that was real.</para>
    ///
    /// <para>Callers: <c>FontProgramRule</c>, <c>ProgramWidthResolver</c> and
    /// <c>FontRemediationPlanner</c>'s CID-0 predicate. The renderer must NOT use this.</para>
    /// </summary>
    public int MapCidToGidStrict(int cid) => MapCid(cid, strict: true);

    private int MapCid(int cid, bool strict)
    {
        // Identity mapping: CID = GID (most common for subset fonts)
        if (_isIdentityMapping)
            return cid;

        if (_cidToGidMap is not null)
        {
            if (_cidToGidMap.TryGetValue(cid, out int gid))
                return gid;
            // Inside the stream's covered range but not stored: the stream explicitly spelled out
            // GID 0 — a legitimate ".notdef, no glyph" answer, not a lookup miss (issue 34).
            if (cid >= 0 && cid < _cidToGidMapCoveredCids)
                return 0;
            // Beyond the covered range: the ONLY place the two resolutions differ (issue 42).
            if (strict)
                return 0;
        }

        // Identity: beyond a stream's coverage on the render path, or no parseable map at all.
        // Note the second case is identity under BOTH resolutions — with no stream there is no
        // coverage to fall outside of, so §9.7.6.3's own precondition is not met.
        return cid;
    }

    private void LoadCidToGidMap()
    {
        if (!_dictionary.TryGetValue(new PdfName("CIDToGIDMap"), out PdfObject? mapObj))
        {
            // No mapping specified, assume identity
            _isIdentityMapping = true;
            return;
        }

        // Resolve indirect reference
        if (mapObj is PdfIndirectReference reference && _document is not null)
            mapObj = _document.ResolveReference(reference);

        switch (mapObj)
        {
            // Check for /Identity name
            case PdfName { Value: "Identity" }:
                _isIdentityMapping = true;
                return;
            // Parse stream containing the mapping
            case PdfStream stream:
            {
                byte[] data = stream.GetDecodedData(_document?.Decryptor);
                _cidToGidMap = new Dictionary<int, int>();
                _cidToGidMapCoveredCids = data.Length / 2;

                // Each entry is 2 bytes (big-endian GID), indexed by CID
                for (var cid = 0; cid < data.Length / 2; cid++)
                {
                    int gid = (data[cid * 2] << 8) | data[cid * 2 + 1];
                    if (gid != 0)  // Only store non-zero mappings (memory stays small); an absent
                                   // entry inside _cidToGidMapCoveredCids is read back as an
                                   // explicit 0 by MapCidToGid, not as "unmapped" (issue 34).
                        _cidToGidMap[cid] = gid;
                }

                break;
            }
            default:
                // Unknown format, assume identity
                _isIdentityMapping = true;
                break;
        }
    }

    internal override PdfFontType FontType => PdfFontType.Type0;

    /// <summary>
    /// Exposes the underlying dictionary for subsetting write-back (e.g. /CIDToGIDMap).
    /// </summary>
    internal PdfDictionary RawDictionary => _dictionary;

    /// <summary>
    /// True when this descendant's own <c>/Subtype</c> is <c>/CIDFontType0</c> — the CID-keyed CFF
    /// descendant, where the CFF charset genuinely IS the CID→GID authority
    /// (<c>CoreTextRenderer.ResolveGlyphId</c>'s <c>cidKeyedCff</c> discriminator, issue 36; mirrors
    /// <c>FontProgramRule.CheckType0</c>'s identically-named field, read there via
    /// <c>ConformanceContext.ResolveName</c>). False for CIDFontType2 — including a CIDFontType2
    /// descendant whose <c>/FontFile2</c> happens to carry CFF outlines wrapped in an OpenType/OTTO
    /// sfnt, where <c>/CIDToGIDMap</c> alone is the mapping authority and the OTTO's own charset is
    /// not CID-keyed at all. The renderer has no <c>ConformanceContext</c>, so this resolves the
    /// (possibly indirect — ISO 32000-1 7.3.10 permits any object indirect) <c>/Subtype</c> itself,
    /// via this font's own <c>_document</c>.
    /// </summary>
    internal bool IsCidFontType0 =>
        (Resolve(_dictionary.Get("Subtype")) as PdfName)?.Value == "CIDFontType0";

    public override double GetCharacterWidth(int charCode)
    {
        if (_widths is not null && _widths.TryGetValue(charCode, out double width))
            return width;

        return _defaultWidth;
    }

    private void LoadWidths()
    {
        // Get default width (DW)
        if (_dictionary.TryGetValue(new PdfName("DW"), out PdfObject dwObj))
        {
            _defaultWidth = dwObj.ToDouble();
        }

        // Get width array (W)
        if (_dictionary.TryGetValue(new PdfName("W"), out PdfObject? wObj))
        {
            if (wObj is PdfIndirectReference reference && _document is not null)
                wObj = _document.ResolveReference(reference);

            if (wObj is PdfArray widthArray)
            {
                _widths = ParseWidthArray(widthArray);
            }
        }

        // Try to get from descriptor
        if (_widths is not null && _widths.Count != 0) return;
        PdfFontDescriptor? descriptor = GetDescriptor();
        if (descriptor is { MissingWidth: > 0 })
            _defaultWidth = descriptor.MissingWidth;
    }

    /// <summary>
    /// Any element of /W may be an indirect reference (ISO 32000-1 7.3.10 — any object may be
    /// indirect), and a common Word/Acrobat output shape stores the inner width array that way.
    /// Elements are therefore resolved before pattern-matching; anything still unreadable after
    /// resolution breaks the parse and degrades to /DW — but not byte-identically to the pre-fix
    /// behaviour: a format-1 entry whose element resolves to nothing now leaves that CID unmapped
    /// (it used to be written as width 0), and an unresolvable format-2 width now abandons the rest
    /// of the array (it used to write 0 for that range and keep parsing). Both changes replace a
    /// silent wrong-width write with "fall through to /DW", which is the intended direction, but
    /// callers should not assume the old exact shape.
    /// </summary>
    private Dictionary<int, double> ParseWidthArray(PdfArray array)
    {
        var widths = new Dictionary<int, double>();
        var i = 0;

        while (i < array.Count)
        {
            if (Resolve(array[i]) is not PdfInteger startCid)
                break;

            int start = startCid.Value;
            i++;

            if (i >= array.Count)
                break;

            PdfObject? second = Resolve(array[i]);

            // Format 1: start_cid [ w1 w2 ... wn ]
            if (second is PdfArray widthList)
            {
                for (var j = 0; j < widthList.Count; j++)
                {
                    if (Resolve(widthList[j]) is not { } width)
                        continue;
                    widths[start + j] = width.ToDouble();
                }
                i++;
            }
            // Format 2: start_cid end_cid width
            else if (second is PdfInteger endCid && i + 1 < array.Count)
            {
                int end = endCid.Value;
                // CIDs are 16-bit (0..65535); clamp a malformed/absurd end so a corrupt array can't
                // force a multi-billion-entry loop.
                if (end > start + 65535)
                    end = start + 65535;
                if (Resolve(array[i + 1]) is not { } widthObj)
                    break;
                var width = widthObj.ToDouble();

                for (int cid = start; cid <= end; cid++)
                {
                    widths[cid] = width;
                }
                i += 2;
            }
            else
            {
                break;
            }
        }

        return widths;
    }

    /// <summary>Resolves an indirect reference through the owning document; null when it cannot.</summary>
    private PdfObject? Resolve(PdfObject? obj) =>
        obj is PdfIndirectReference reference
            ? _document is null ? null : _document.ResolveReference(reference)
            : obj;
}
