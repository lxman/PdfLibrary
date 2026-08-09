using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;

namespace PdfLibrary.Editing;

public sealed partial class PdfDocumentEditor
{
    /// <summary>
    /// Writes (or removes) a font's <c>/ToUnicode</c> CMap.
    ///
    /// <para>Target the LOGICAL font — a Type0 wrapper, not its descendant CIDFont. Viewers read
    /// /ToUnicode from the font a content stream selects; an entry on the descendant is valid
    /// syntax that nothing consults, so the fix would report success and the finding would
    /// persist.</para>
    ///
    /// <para>An empty map removes the entry, matching the PdfMetadata convention that null clears.
    /// It does not write an empty CMap, which would be a mapping to nothing rather than the absence
    /// of a mapping.</para>
    /// </summary>
    /// <exception cref="ArgumentException">No object with that number, or it is not a dictionary.</exception>
    public void SetToUnicode(FontId font, IReadOnlyDictionary<int, string> codeToText)
    {
        ArgumentNullException.ThrowIfNull(codeToText);

        PdfDictionary dictionary = ResolveFontDictionary(font);
        var key = new PdfName("ToUnicode");

        if (codeToText.Count == 0)
        {
            dictionary.Remove(key);
            return;
        }

        // The codespace must match the font's own encoding (ISO 32000-1 / 32000-2 §9.10.3, both
        // normative): one byte for a simple font, two for a composite one. SetToUnicode targets the
        // LOGICAL font, so /Subtype on this very dictionary is the right thing to read — a Type0
        // wrapper says "Type0", and everything else here is simple. /Subtype may itself be an
        // indirect reference (syntactically legal), so resolve before comparing — reading the
        // unresolved PdfIndirectReference would silently misclassify a genuine Type0 font as simple
        // and emit a one-byte-codespace CMap for a font whose content-stream codes are two bytes
        // wide: the fix would report success and the mapping would be unusable, the same failure
        // shape as writing /ToUnicode onto the descendant. A missing /Subtype falls through to
        // OneByte: an unidentifiable font cannot be assumed composite, and a simple font is the more
        // common case this default protects.
        ToUnicodeCodespace codespace =
            (Resolve(dictionary.Get("Subtype")) as PdfName)?.Value == "Type0"
                ? ToUnicodeCodespace.TwoByte
                : ToUnicodeCodespace.OneByte;

        PdfIndirectReference streamRef = _document.RegisterObject(
            new PdfStream(new PdfDictionary(), ToUnicodeCMapWriter.Write(codeToText, codespace)));
        dictionary.Set(key, streamRef);
    }

    /// <summary>
    /// True when <paramref name="font"/> names an object that exists and is a dictionary. Lets
    /// callers (Task 11's save stage) distinguish a font an earlier Organize stage already deleted
    /// (skip and report) from a malformed proposal (a bug that must surface) — both would otherwise
    /// arrive from <see cref="SetToUnicode"/> as an <see cref="ArgumentException"/>. Implemented as
    /// the SAME lookup <see cref="SetToUnicode"/> performs, factored into <see cref="ResolveFontDictionary"/>
    /// so the two can never disagree.
    /// </summary>
    public bool HasFont(FontId font) => TryResolveFontDictionary(font, out _);

    private PdfDictionary ResolveFontDictionary(FontId font)
    {
        if (!TryResolveFontDictionary(font, out PdfDictionary? dictionary))
        {
            throw new ArgumentException(
                $"No font dictionary at object {font.ObjectNumber}.", nameof(font));
        }
        return dictionary!;
    }

    private bool TryResolveFontDictionary(FontId font, out PdfDictionary? dictionary)
    {
        dictionary = _document.GetObject(font.ObjectNumber) as PdfDictionary;
        return dictionary is not null;
    }

    private PdfObject? Resolve(PdfObject? obj) =>
        obj is PdfIndirectReference reference ? _document.GetObject(reference.ObjectNumber) : obj;
}
