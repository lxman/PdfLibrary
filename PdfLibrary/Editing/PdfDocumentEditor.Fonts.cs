using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Editing;

public sealed partial class PdfDocumentEditor
{
    /// <summary>
    /// Writes <paramref name="program"/> into <paramref name="font"/>'s <c>/FontDescriptor</c> —
    /// the operation that actually embeds a font program (design §5.1, §5.2, §5.3).
    ///
    /// <para><paramref name="font"/> is the PROGRAM HOLDER, not necessarily the logical font (design
    /// §3.2): for a simple font they are the same dictionary; for a composite font the caller must
    /// pass the descendant CIDFont's id. F-2's planner never proposes a composite font, but this
    /// operation is written to handle one correctly regardless, because F-4 will call it for one.</para>
    ///
    /// <para>Five obligations, all load-bearing:</para>
    /// <list type="number">
    /// <item>Writes the program to the right stream key/subtype for <paramref name="format"/>:
    /// <c>/FontFile2</c> (TrueType, +<c>/Length1</c>), <c>/FontFile3</c> with <c>/Subtype</c>
    /// <c>/Type1C</c> | <c>/CIDFontType0C</c> | <c>/OpenType</c>, or <c>/FontFile</c> (Type1, +
    /// <c>/Length1</c>/<c>/Length2</c>/<c>/Length3</c>). A Type1 program whose segment lengths cannot
    /// be determined (a bare PFA with no embedded segment markers) throws
    /// <see cref="NotSupportedException"/> rather than writing wrong lengths — those would produce a
    /// font no consumer can load, and the planner should have declined such a program before this
    /// operation is ever reached.</item>
    /// <item>Creates <c>/FontDescriptor</c> when absent — <c>FontEmbeddingRule</c> fires both when the
    /// descriptor is entirely missing and when it merely lacks a font file, so both paths must work.</item>
    /// <item>Recomputes <c>/FontBBox</c>, <c>/ItalicAngle</c>, <c>/Ascent</c>, <c>/Descent</c>,
    /// <c>/CapHeight</c>, <c>/StemV</c>, <c>/MissingWidth</c> from the bytes actually embedded
    /// (<see cref="FontDescriptorMetrics.Compute"/>) — a descriptor describing something else is both
    /// wrong and a conformance violation once a real program sits behind it.</item>
    /// <item>Preserves the symbolic/nonsymbolic <c>/Flags</c> bits (bit 3 = Symbolic = 4, bit 6 =
    /// Nonsymbolic = 32) UNCHANGED when a descriptor already exists — they decide how the encoding is
    /// interpreted, so flipping one changes which glyph a character code selects, a rendering change
    /// smuggled in as a side effect. When creating a descriptor from nothing there is no prior value;
    /// this defaults to Nonsymbolic (32) — a symbolic font arriving with no descriptor at all is a
    /// case the planner should be declining, not one this operation can infer correctly.</item>
    /// <item><c>/Widths</c>: never touched when present — it is what preserves the document's existing
    /// layout, and the substitute program's own metrics are irrelevant to it (ISO 32000-2 §9.2.4 NOTE
    /// 2/3). Only derived (<c>/FirstChar</c>, <c>/LastChar</c>, <c>/Widths</c>, scaled to 1000
    /// units/em) when <c>/Widths</c> is absent.</item>
    /// </list>
    ///
    /// <para>Mutates the in-memory object graph only; does not call Save.</para>
    /// </summary>
    /// <exception cref="ArgumentException">No object at <paramref name="font"/>'s object number, or it
    /// is not a dictionary.</exception>
    /// <exception cref="NotSupportedException"><paramref name="format"/> is
    /// <see cref="FontProgramFormat.Type1"/> and the program's clear-text/encrypted/trailer segment
    /// lengths cannot be determined.</exception>
    public void EmbedProgram(FontId font, byte[] program, FontProgramFormat format)
    {
        ArgumentNullException.ThrowIfNull(program);

        PdfDictionary fontDict = ResolveFontDictionary(font);

        // Obligations 2 + 4: resolve or create the descriptor. An existing descriptor's /Flags is
        // never touched; a fresh one gets the Nonsymbolic default (see the helper's own comment).
        PdfDictionary descriptor = ResolveOrCreateDescriptor(fontDict);

        // Obligation 1: write the program stream to the format-appropriate key, clearing the other
        // two so a re-embed under a different format doesn't leave a stale entry behind.
        WriteProgramStream(descriptor, program, format);

        // Obligation 3: recompute the descriptor's metric entries from the bytes actually embedded.
        // Compute returns null when the program will not parse (e.g. a bare PFA Type1 program the
        // Type1 parser cannot read) — leave whatever metrics were already there rather than writing
        // zeroes, which would be worse than stale-but-plausible values.
        FontDescriptorValues? values = FontDescriptorMetrics.Compute(program, format);
        if (values is not null)
            ApplyMetrics(descriptor, values);

        // Obligation 5: /Widths is sacred when present; derived only when absent.
        if (fontDict.Get("Widths") is null)
            WriteDerivedWidths(fontDict, program, format, values);
    }

    /// <summary>Resolves the font dictionary's existing <c>/FontDescriptor</c>, or registers and
    /// wires up a freshly created one.</summary>
    private PdfDictionary ResolveOrCreateDescriptor(PdfDictionary fontDict)
    {
        PdfObject? existing = Resolve(fontDict.Get("FontDescriptor"));
        if (existing is PdfDictionary existingDescriptor)
            return existingDescriptor;

        var descriptor = new PdfDictionary();
        descriptor.Set("Type", new PdfName("FontDescriptor"));
        PdfObject? baseFont = fontDict.Get("BaseFont");
        if (baseFont is not null)
            descriptor.Set("FontName", baseFont);
        // No prior /Flags to preserve — Nonsymbolic is the safer default of the two bits. A symbolic
        // font arriving with no descriptor at all is a shape the planner should be declining before
        // EmbedProgram is reached (Task 6), not one this operation can infer correctly.
        descriptor.Set("Flags", new PdfInteger(32));

        PdfIndirectReference descriptorRef = _document.RegisterObject(descriptor);
        fontDict.Set("FontDescriptor", descriptorRef);
        return descriptor;
    }

    /// <summary>Obligation 1: writes <paramref name="program"/> to the stream key ISO 32000-2 §9.9
    /// assigns <paramref name="format"/>, and clears whichever of the other two keys might be left
    /// over from a previous embedding under a different format.</summary>
    private void WriteProgramStream(PdfDictionary descriptor, byte[] program, FontProgramFormat format)
    {
        var fontFile = new PdfName("FontFile");
        var fontFile2 = new PdfName("FontFile2");
        var fontFile3 = new PdfName("FontFile3");

        switch (format)
        {
            case FontProgramFormat.TrueType:
            {
                var streamDict = new PdfDictionary();
                streamDict.Set("Length1", new PdfInteger(program.Length));
                PdfIndirectReference streamRef = _document.RegisterObject(new PdfStream(streamDict, program));
                descriptor.Set(fontFile2, streamRef);
                descriptor.Remove(fontFile3);
                descriptor.Remove(fontFile);
                break;
            }

            case FontProgramFormat.Type1C:
            case FontProgramFormat.CidFontType0C:
            case FontProgramFormat.OpenType:
            {
                string subtype = format switch
                {
                    FontProgramFormat.Type1C => "Type1C",
                    FontProgramFormat.CidFontType0C => "CIDFontType0C",
                    FontProgramFormat.OpenType => "OpenType",
                    _ => throw new InvalidOperationException("Unreachable."),
                };
                var streamDict = new PdfDictionary();
                streamDict.Set("Subtype", new PdfName(subtype));
                PdfIndirectReference streamRef = _document.RegisterObject(new PdfStream(streamDict, program));
                descriptor.Set(fontFile3, streamRef);
                descriptor.Remove(fontFile2);
                descriptor.Remove(fontFile);
                break;
            }

            case FontProgramFormat.Type1:
            {
                // A PDF /FontFile stream is the bare concatenation of the clear-text, encrypted and
                // trailer segments — WITHOUT the PFB format's 6-byte segment headers. Type1PfbSegments
                // recovers those lengths from a PFB program's own segment headers and throws
                // NotSupportedException for anything it cannot determine them from (a bare PFA, a
                // corrupt segment table, or a PFB with no binary segment) — shared with
                // FontRemediationPlanner (Task 6) so the planner's pre-embed decline and this
                // operation's own validation cannot diverge. The planner should decline such a
                // program before reaching here; this throw is the backstop if it does not.
                (byte[] data, int length1, int length2, int length3) = Type1PfbSegments.Split(program);

                var streamDict = new PdfDictionary();
                streamDict.Set("Length1", new PdfInteger(length1));
                streamDict.Set("Length2", new PdfInteger(length2));
                streamDict.Set("Length3", new PdfInteger(length3));
                PdfIndirectReference streamRef = _document.RegisterObject(new PdfStream(streamDict, data));
                descriptor.Set(fontFile, streamRef);
                descriptor.Remove(fontFile2);
                descriptor.Remove(fontFile3);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown font program format.");
        }
    }

    /// <summary>Obligation 3: writes the recomputed metric entries. Never called with a null
    /// <see cref="FontDescriptorValues"/> — the caller checks first.</summary>
    private static void ApplyMetrics(PdfDictionary descriptor, FontDescriptorValues values)
    {
        descriptor.Set("FontBBox", new PdfArray(
            new PdfInteger(values.FontBBox[0]),
            new PdfInteger(values.FontBBox[1]),
            new PdfInteger(values.FontBBox[2]),
            new PdfInteger(values.FontBBox[3])));
        descriptor.Set("ItalicAngle", new PdfReal(values.ItalicAngle));
        descriptor.Set("Ascent", new PdfInteger(values.Ascent));
        descriptor.Set("Descent", new PdfInteger(values.Descent));
        descriptor.Set("CapHeight", new PdfInteger(values.CapHeight));
        descriptor.Set("StemV", new PdfInteger(values.StemV));
        descriptor.Set("MissingWidth", new PdfInteger(values.MissingWidth));
    }

    /// <summary>Obligation 5, the absent-/Widths branch only — a present /Widths array is never
    /// reached here (the caller checks first). Derives /FirstChar, /LastChar and /Widths from the
    /// program's own advances over the codes a simple font's default (StandardEncoding-flavoured)
    /// text-space encoding addresses (32-255, printable ASCII plus the Latin-1 supplement), scaled to
    /// 1000 units per em. A code the program does not cover within that span falls back to the
    /// descriptor's own /MissingWidth rather than a bare zero, matching ISO 32000-2 §9.8.3's own
    /// fallback for an omitted /Widths entry.</summary>
    private static void WriteDerivedWidths(
        PdfDictionary fontDict, byte[] program, FontProgramFormat format, FontDescriptorValues? values)
    {
        EmbeddedFontMetrics metrics = format == FontProgramFormat.Type1
            ? new EmbeddedFontMetrics(program, length1: 0, length2: 0, length3: 0)
            : new EmbeddedFontMetrics(program);

        if (!metrics.IsValid || metrics.UnitsPerEm == 0)
            return; // Nothing sound to derive from; leave /Widths absent rather than guess.

        double scale = 1000.0 / metrics.UnitsPerEm;
        int missingWidth = values?.MissingWidth ?? 0;

        const int rangeStart = 32, rangeEnd = 255;
        var widths = new int[rangeEnd - rangeStart + 1];
        int firstCovered = -1, lastCovered = -1;

        for (int code = rangeStart; code <= rangeEnd; code++)
        {
            ushort advance = metrics.GetUnicodeAdvanceWidth(code);
            widths[code - rangeStart] = advance == 0 ? missingWidth : (int)Math.Round(advance * scale);
            if (advance == 0) continue;
            if (firstCovered < 0) firstCovered = code;
            lastCovered = code;
        }

        if (firstCovered < 0)
            return; // No coverage in this range at all — nothing sound to derive.

        var widthsArray = new PdfArray();
        for (int code = firstCovered; code <= lastCovered; code++)
            widthsArray.Add(new PdfInteger(widths[code - rangeStart]));

        fontDict.Set("FirstChar", new PdfInteger(firstCovered));
        fontDict.Set("LastChar", new PdfInteger(lastCovered));
        fontDict.Set("Widths", widthsArray);
    }
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
