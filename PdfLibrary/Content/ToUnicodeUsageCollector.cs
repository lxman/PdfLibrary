using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;

namespace PdfLibrary.Content;

/// <summary>
/// Scans a content stream (page content + Form XObjects, recursively) and records, per font actually used
/// for text showing, the set of character codes drawn. Modelled on <see cref="GlyphUsageCollector"/> and
/// <see cref="PdfTextExtractor"/>: it mirrors their content-processor state tracking and text-show dispatch,
/// but keeps CHARACTER CODES (not glyph IDs) so a conformance rule can ask whether each used code maps to
/// Unicode (PDF/A-2u, ISO 19005-2 6.2.11.7.2). Codes are split per font: one byte for simple fonts, two
/// big-endian bytes for Type0 (matching the Identity-H/V assumption used across the engine's text path).
/// </summary>
internal sealed class ToUnicodeUsageCollector : PdfContentProcessor
{
    private readonly PdfResources? _resources;
    private readonly PdfDocument? _document;

    // Used codes accumulated per font instance (reference identity; the same font seen in several scopes
    // may appear more than once, which a consuming rule de-duplicates by font name — correctness is unaffected).
    private readonly Dictionary<PdfFont, HashSet<int>> _result = new(ReferenceEqualityComparer.Instance);

    // Mirror of _result restricted to codes shown while NOT in text rendering mode 3 (invisible). Backs the
    // RM3 exemption veraPDF applies to glyph-present (6.2.11.4.1 t2) and widths (6.2.11.5) — but not to
    // .notdef (6.2.11.8, "regardless of text rendering mode") or ToUnicode, which stay on the full set.
    private readonly Dictionary<PdfFont, HashSet<int>> _visibleResult = new(ReferenceEqualityComparer.Instance);

    public ToUnicodeUsageCollector(PdfResources? resources, PdfDocument? document)
    {
        _resources = resources;
        _document = document;
    }

    /// <summary>Used character codes per font, after processing the content stream(s).</summary>
    public IReadOnlyDictionary<PdfFont, HashSet<int>> Result => _result;

    /// <summary>The subset of <see cref="Result"/> shown outside text rendering mode 3 (invisible).</summary>
    public IReadOnlyDictionary<PdfFont, HashSet<int>> VisibleResult => _visibleResult;

    private protected override void OnShowText(PdfString text) => Accumulate(text.Bytes);

    private protected override void OnShowTextWithPositioning(PdfArray array)
    {
        foreach (PdfObject item in array)
            if (item is PdfString str)
                Accumulate(str.Bytes);
    }

    protected override void OnInvokeXObject(string name)
    {
        PdfStream? xobject = _resources?.GetXObject(name);
        if (xobject is null || PdfImage.IsImageXObject(xobject) || !IsFormXObject(xobject))
            return;

        byte[] contentData = xobject.GetDecodedData(_document?.Decryptor);

        PdfResources? formResources = _resources;
        if (xobject.Dictionary.TryGetValue(new PdfName("Resources"), out PdfObject? resObj))
        {
            if (resObj is PdfIndirectReference r && _document is not null)
                resObj = _document.ResolveReference(r);
            if (resObj is PdfDictionary resDict)
                formResources = new PdfResources(resDict, _document);
        }

        var nested = new ToUnicodeUsageCollector(formResources, _document);
        nested.ProcessOperators(PdfContentParser.Parse(contentData));

        foreach ((PdfFont font, HashSet<int> codes) in nested.Result)
        {
            if (!_result.TryGetValue(font, out HashSet<int>? existing))
                _result[font] = codes;
            else
                existing.UnionWith(codes);
        }

        foreach ((PdfFont font, HashSet<int> codes) in nested.VisibleResult)
        {
            if (!_visibleResult.TryGetValue(font, out HashSet<int>? existing))
                _visibleResult[font] = codes;
            else
                existing.UnionWith(codes);
        }
    }

    private static bool IsFormXObject(PdfStream stream) =>
        stream.Dictionary.TryGetValue(new PdfName("Subtype"), out PdfObject obj) && obj is PdfName { Value: "Form" };

    private void Accumulate(byte[] bytes)
    {
        if (_resources is null || string.IsNullOrEmpty(CurrentState.FontName))
            return;

        PdfFont? font = _resources.GetFontObject(CurrentState.FontName);
        if (font is null)
            return;

        if (!_result.TryGetValue(font, out HashSet<int>? codes))
            _result[font] = codes = [];

        // Visible == not text rendering mode 3 (invisible). Only touch _visibleResult when this text is
        // actually visible, so a font never seen outside RM3 correctly has no entry (empty visible set).
        HashSet<int>? visibleCodes = null;
        if (CurrentState.RenderingMode != 3 && !_visibleResult.TryGetValue(font, out visibleCodes))
            _visibleResult[font] = visibleCodes = [];

        if (font is Type0Font)
        {
            // Two-byte big-endian codes (Identity-H/V and the common CID case); a trailing odd byte is
            // not a complete code and is skipped rather than mistaken for a one-byte code.
            for (int i = 0; i + 1 < bytes.Length; i += 2)
            {
                int code = (bytes[i] << 8) | bytes[i + 1];
                codes.Add(code);
                visibleCodes?.Add(code);
            }
        }
        else
        {
            foreach (byte b in bytes)
            {
                codes.Add(b);
                visibleCodes?.Add(b);
            }
        }
    }
}
