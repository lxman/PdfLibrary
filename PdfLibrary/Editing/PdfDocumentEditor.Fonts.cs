using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Remediation;

namespace PdfLibrary.Editing;

public sealed partial class PdfDocumentEditor
{
    /// <summary>
    /// Writes <paramref name="program"/> into <paramref name="font"/>'s <c>/FontDescriptor</c> —
    /// the operation that actually embeds a font program (design §5.1, §5.2, §5.3).
    ///
    /// <para><paramref name="font"/> is the PROGRAM HOLDER, not necessarily the logical font (design
    /// §3.2): for a simple font they are the same dictionary; for a composite font the caller must
    /// pass the descendant CIDFont's id.</para>
    ///
    /// <para>COMPOSITE FONTS ARE NOT YET HANDLED. F-2's planner declines every composite font, so
    /// this is unreachable rather than merely untested — but obligation 5 below reads
    /// <c>fontDict.Get("Widths")</c>, which is ALWAYS null on a CIDFont (a CIDFont declares its
    /// widths with <c>/W</c> and <c>/DW</c>, ISO 32000-2 §9.7.4.3), so calling this with a descendant
    /// CIDFont's id today would write a bogus <c>/Widths</c>/<c>/FirstChar</c>/<c>/LastChar</c> into a
    /// dictionary those entries mean nothing in. F-4 must teach obligation 5 about <c>/W</c> — and
    /// obligation 6 about the CIDFont subtypes — before it calls this for a composite font.</para>
    ///
    /// <para>Six obligations, all load-bearing:</para>
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
    /// units/em) when <c>/Widths</c> is absent. Derived widths are measured through the font's own
    /// <c>/Encoding</c>, not by treating a character code as a Unicode code point.</item>
    /// <item>Reconciles a SIMPLE font dictionary's own <c>/Subtype</c> with the embedded program's
    /// format (<see cref="SimpleFontProgramSubtype"/>), because ISO 32000-2 §9.9.1 Table 124
    /// constrains the PAIR and not the stream key alone — see that type's own documentation. Refuses
    /// (<see cref="NotSupportedException"/>) the pairs Table 124 permits nowhere.</item>
    /// </list>
    ///
    /// <para>Mutates the in-memory object graph only; does not call Save.</para>
    /// </summary>
    /// <exception cref="ArgumentException">No object at <paramref name="font"/>'s object number, or it
    /// is not a dictionary.</exception>
    /// <exception cref="NotSupportedException"><paramref name="format"/> is
    /// <see cref="FontProgramFormat.Type1"/> and the program's clear-text/encrypted/trailer segment
    /// lengths cannot be determined, or no ISO 32000-2 Table 124 pair permits this program in a simple
    /// font dictionary (a CID-keyed program, in particular). The planner declines both shapes before
    /// they reach here; these throws are the backstop.</exception>
    public void EmbedProgram(FontId font, byte[] program, FontProgramFormat format)
    {
        ArgumentNullException.ThrowIfNull(program);

        PdfDictionary fontDict = ResolveFontDictionary(font);

        // Obligation 6, resolved FIRST so a refusal leaves the document entirely untouched rather
        // than half-embedded. /Subtype may itself be an indirect reference (syntactically legal), so
        // resolve before comparing — the same reason SetToUnicode below resolves it.
        var dictSubtype = (Resolve(fontDict.Get("Subtype")) as PdfName)?.Value;
        string? reconciledSubtype = IsCompositeSubtype(dictSubtype)
            ? null // composite: see the "NOT YET HANDLED" note above — left exactly as it was.
            : SimpleFontProgramSubtype.Resolve(format, program, dictSubtype);

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

        // Obligation 6, applied last: a simple font dictionary must declare the subtype Table 124
        // pairs with the stream key just written. Rewriting /Type1 to /TrueType (the common case: a
        // /BaseFont /Helvetica dictionary resolving to a system TrueType substitute) is safe because
        // a TrueType font dictionary carries the SAME entries a Type1 one does — /BaseFont,
        // /FirstChar, /LastChar, /Widths, /FontDescriptor, /Encoding (Table 109 / Table 111) — so
        // nothing else has to move with it. Without this, F-2 would close the font-embedded finding
        // by writing a /FontFile2 into a /Type1 descriptor: a combination Table 124 permits nowhere,
        // i.e. one conformance finding traded for another.
        if (reconciledSubtype is not null && reconciledSubtype != dictSubtype)
            fontDict.Set("Subtype", new PdfName(reconciledSubtype));
    }

    /// <summary>True for the subtypes <see cref="EmbedProgram"/> must NOT reconcile via
    /// <see cref="SimpleFontProgramSubtype"/> — the Type0 wrapper and either flavour of descendant
    /// CIDFont (Table 124's simple-font rows do not apply to these, and F-2 does not handle them,
    /// see <see cref="EmbedProgram"/>'s own note), plus /Type3. /Type3 is a "simple" font by Table
    /// 124's own classification, but its /Subtype is not a font-PROGRAM-format label at all — it is
    /// load-bearing together with /CharProcs and /FontMatrix (Table 112), so overwriting it based on
    /// an embedded program's format would silently destroy that meaning. The planner already
    /// declines /Type3 before a proposal reaches here; this is the same backstop reasoning as
    /// <see cref="EmbedProgram"/>'s own throws — <see cref="EmbedProgram"/> is public, so a direct
    /// caller must be stopped here too. A missing /Subtype falls through to "simple": an
    /// unidentifiable font cannot be assumed composite, and simple is both the far commoner case and
    /// the one this increment serves.</summary>
    private static bool IsCompositeSubtype(string? subtype) =>
        subtype is "Type0" or "CIDFontType0" or "CIDFontType2" or "Type3";

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

    /// <summary>Obligation 3: writes the recomputed metric entries, including <c>/MissingWidth</c>
    /// (meaningful on a simple font's descriptor, ISO 32000-2 §9.8.3). Never called with a null
    /// <see cref="FontDescriptorValues"/> — the caller checks first. Delegates to
    /// <see cref="WriteMetricEntries"/> so this and <see cref="ReplaceCompositeProgram"/> can never
    /// disagree on how the six shared entries are written — <c>/MissingWidth</c> is the one entry that
    /// differs between the two callers, not the primitives.</summary>
    private static void ApplyMetrics(PdfDictionary descriptor, FontDescriptorValues values) =>
        WriteMetricEntries(descriptor, values, includeMissingWidth: true);

    /// <summary>The six <c>/FontBBox</c>/<c>/ItalicAngle</c>/<c>/Ascent</c>/<c>/Descent</c>/
    /// <c>/CapHeight</c>/<c>/StemV</c> entries every <see cref="FontDescriptorValues"/> caller writes,
    /// plus <c>/MissingWidth</c> when <paramref name="includeMissingWidth"/> is true.
    /// <c>/MissingWidth</c> is meaningless on a CID font's descriptor — a CIDFont declares its
    /// fallback width with <c>/DW</c> on the descendant dictionary, not <c>/MissingWidth</c> on the
    /// descriptor (ISO 32000-2 §9.7.4.3) — so <see cref="ReplaceCompositeProgram"/> passes false.</summary>
    private static void WriteMetricEntries(
        PdfDictionary descriptor, FontDescriptorValues values, bool includeMissingWidth)
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
        if (includeMissingWidth)
            descriptor.Set("MissingWidth", new PdfInteger(values.MissingWidth));
    }

    /// <summary>Obligation 5, the absent-/Widths branch only — a present /Widths array is never
    /// reached here (the caller checks first). Derives /FirstChar, /LastChar and /Widths from the
    /// program's own advances over the codes a simple font's one-byte encoding addresses (32-255,
    /// printable ASCII plus the upper half), scaled to 1000 units per em. A code the program does not
    /// cover within that span falls back to the descriptor's own /MissingWidth rather than a bare
    /// zero, matching ISO 32000-2 §9.8.3's own fallback for an omitted /Widths entry.
    ///
    /// <para>Each code is resolved through the font's OWN /Encoding — code to glyph NAME, glyph name
    /// to Unicode, Unicode to advance — the same ladder <c>FontRemediationPlanner.ProvableUnicode</c>
    /// walks. Feeding the raw code to a Unicode lookup instead is wrong for every code above 127 the
    /// encodings this branch actually meets remap: WinAnsiEncoding sends 0x80 to U+20AC (euro) and
    /// 0x92 to U+2019 (right single quote), and StandardEncoding's whole upper half diverges from
    /// Latin-1. Since §5.1 makes the DERIVED case the one where written widths genuinely position
    /// text, a width measured from the wrong glyph moves text on the page.</para></summary>
    private void WriteDerivedWidths(
        PdfDictionary fontDict, byte[] program, FontProgramFormat format, FontDescriptorValues? values)
    {
        EmbeddedFontMetrics metrics = format == FontProgramFormat.Type1
            ? new EmbeddedFontMetrics(program, length1: 0, length2: 0, length3: 0)
            : new EmbeddedFontMetrics(program);

        if (!metrics.IsValid || metrics.UnitsPerEm == 0)
            return; // Nothing sound to derive from; leave /Widths absent rather than guess.

        PdfFontEncoding encoding = ResolveEncoding(fontDict);
        double scale = 1000.0 / metrics.UnitsPerEm;
        int missingWidth = values?.MissingWidth ?? 0;

        const int rangeStart = 32, rangeEnd = 255;
        var widths = new int[rangeEnd - rangeStart + 1];
        int firstCovered = -1, lastCovered = -1;

        for (int code = rangeStart; code <= rangeEnd; code++)
        {
            ushort advance = EncodedAdvance(metrics, encoding, code);
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

    /// <summary>The encoding <see cref="WriteDerivedWidths"/> measures through: the font's own
    /// <c>/Encoding</c>, whether written as a base-encoding name or as an encoding dictionary
    /// (<c>/BaseEncoding</c> plus <c>/Differences</c>). When there is no <c>/Encoding</c> at all, ISO
    /// 32000-2 §9.6.5.2 makes the font program's built-in encoding the default and StandardEncoding
    /// the fallback for the simple Latin subtypes this branch serves — which is what
    /// <see cref="PdfFontEncoding"/>'s own default is. Deliberately NOT Latin-1: assuming code ==
    /// Unicode is the very defect this ladder exists to fix.</summary>
    private PdfFontEncoding ResolveEncoding(PdfDictionary fontDict) =>
        Resolve(fontDict.Get("Encoding")) switch
        {
            PdfName name => PdfFontEncoding.GetStandardEncoding(name.Value),
            PdfDictionary dict => PdfFontEncoding.FromDictionary(dict),
            _ => PdfFontEncoding.GetStandardEncoding("StandardEncoding"),
        };

    /// <summary>One code's advance in the program's own font units, resolved code → glyph name →
    /// Unicode → advance. Falls back to a direct glyph-NAME lookup when the program is CFF/Type1
    /// flavoured (whose glyphs are named, and which may carry no Unicode cmap at all), and only for a
    /// code the encoding cannot name at all does it fall back to the raw code — where code and
    /// Unicode code point coincide for the ASCII range that case realistically covers. Returns 0 for
    /// a code this program does not cover, which the caller turns into /MissingWidth.</summary>
    private static ushort EncodedAdvance(EmbeddedFontMetrics metrics, PdfFontEncoding encoding, int code)
    {
        string? glyphName = encoding.GetGlyphName(code);
        if (string.IsNullOrEmpty(glyphName) || glyphName == ".notdef")
            return metrics.GetUnicodeAdvanceWidth(code);

        string? text = GlyphList.GetUnicode(glyphName);
        if (text is { Length: > 0 } && !text.Contains(FontUnicodeMapping.ReplacementChar))
        {
            // A ligature/multi-codepoint glyph name has no single code point to look up; the
            // by-name route below is the honest answer for one, not the first component's advance.
            var codePoints = 0;
            for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1) codePoints++;
            if (codePoints == 1)
            {
                ushort byUnicode = metrics.GetUnicodeAdvanceWidth(char.ConvertToUtf32(text, 0));
                if (byUnicode != 0) return byUnicode;
            }
        }

        ushort gid = metrics.GetGlyphIdByName(glyphName);
        return gid == 0 ? (ushort)0 : metrics.GetAdvanceWidth(gid);
    }
    /// <summary>
    /// Writes <paramref name="font"/>'s <c>/FontDescriptor</c> <c>/CIDSet</c> as a bitmap identifying
    /// exactly <paramref name="cids"/>: bit <c>i</c> (MSB-first within each byte) set ⇒ CID <c>i</c>
    /// present, the encoding <c>FontSubsetCoverageRule</c> decodes.
    ///
    /// <para><paramref name="font"/> is the PROGRAM HOLDER — the descendant CIDFont for a composite
    /// font, never the Type0 wrapper, because <c>/FontDescriptor</c> lives there.</para>
    ///
    /// <para>Writes exactly the set it is given, with no opinion about which CIDs belong: deciding
    /// that is the planner's job, and it decides by enumerating the embedded program. No-op when the
    /// font has no <c>/FontDescriptor</c> or no existing <c>/CIDSet</c> — this operation CORRECTS a
    /// declaration, it never introduces one, because a subset font with no <c>/CIDSet</c> produces no
    /// finding and giving it one would create an obligation the document never had.</para>
    /// </summary>
    public void SetCidSet(FontId font, IReadOnlySet<int> cids)
    {
        ArgumentNullException.ThrowIfNull(cids);

        PdfDictionary holder = ResolveFontDictionary(font);
        if (Resolve(holder.Get("FontDescriptor")) is not PdfDictionary descriptor) return;
        if (Resolve(descriptor.Get("CIDSet")) is not PdfStream) return;

        var max = -1;
        foreach (int cid in cids)
            if (cid > max) max = cid;

        var bytes = new byte[max < 0 ? 0 : max / 8 + 1];
        foreach (int cid in cids)
        {
            if (cid < 0) continue;
            bytes[cid / 8] |= (byte)(0x80 >> (cid % 8));
        }

        PdfIndirectReference streamRef = _document.RegisterObject(new PdfStream(new PdfDictionary(), bytes));
        descriptor.Set("CIDSet", streamRef);
    }

    /// <summary>
    /// Writes <paramref name="font"/>'s <c>/FontDescriptor</c> <c>/CharSet</c> as the run of PDF name
    /// tokens <c>FontSubsetCoverageRule</c> parses (<c>/a/b/c</c>).
    ///
    /// <para>Same contract as <see cref="SetCidSet"/>: corrects an existing declaration, never
    /// introduces one, and writes exactly the names it is given.</para>
    /// </summary>
    public void SetCharSet(FontId font, IReadOnlySet<string> glyphNames)
    {
        ArgumentNullException.ThrowIfNull(glyphNames);

        PdfDictionary holder = ResolveFontDictionary(font);
        if (Resolve(holder.Get("FontDescriptor")) is not PdfDictionary descriptor) return;
        if (Resolve(descriptor.Get("CharSet")) is not PdfString) return;

        var builder = new System.Text.StringBuilder();
        foreach (string name in glyphNames.OrderBy(n => n, StringComparer.Ordinal))
            builder.Append('/').Append(name);

        // PdfString's public constructor surface takes bytes, not a string (it stores
        // PDFDocEncoding/Latin-1 bytes internally) — the names this writes are PDF name tokens, i.e.
        // ASCII, so Latin-1 round-trips exactly through the .Value property the rule reads.
        descriptor.Set("CharSet", new PdfString(System.Text.Encoding.Latin1.GetBytes(builder.ToString())));
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
    /// Replaces <paramref name="font"/>'s EXISTING <c>/FontFile2</c> program bytes — the F-4a width
    /// patch's write. <paramref name="font"/> is the PROGRAM HOLDER (design §3.2). Deliberately does
    /// NOT touch the descriptor's metric entries, <c>/Widths</c>/<c>/W</c>, or <c>/Subtype</c>: the
    /// program differs from its predecessor only in advance widths, the declared widths are what
    /// preserves layout (§5.1), and rewriting anything else would smuggle a second change into an
    /// operation whose whole contract is "only the advances moved" — the contrast with
    /// <see cref="EmbedProgram"/>'s six obligations is intentional and load-bearing.
    /// </summary>
    /// <exception cref="ArgumentException">No dictionary at <paramref name="font"/>.</exception>
    /// <exception cref="InvalidOperationException">The font has no /FontDescriptor or no /FontFile2 —
    /// the planner only proposes patches for fonts that have both; this is the backstop.</exception>
    public void ReplaceProgramBytes(FontId font, byte[] program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.Length == 0)
            throw new ArgumentException("A font program cannot be empty.", nameof(program));

        PdfDictionary fontDict = ResolveFontDictionary(font);
        if (Resolve(fontDict.Get("FontDescriptor")) is not PdfDictionary descriptor)
            throw new InvalidOperationException(
                $"Font object {font.ObjectNumber} has no /FontDescriptor; ReplaceProgramBytes only "
                + "rewrites an existing embedded program.");
        if (Resolve(descriptor.Get("FontFile2")) is not PdfStream)
            throw new InvalidOperationException(
                $"Font object {font.ObjectNumber} has no /FontFile2; ReplaceProgramBytes only "
                + "rewrites an existing sfnt program.");

        var streamDict = new PdfDictionary();
        streamDict.Set("Length1", new PdfInteger(program.Length));
        PdfIndirectReference streamRef = _document.RegisterObject(new PdfStream(streamDict, program));
        descriptor.Set("FontFile2", streamRef);
    }

    /// <summary>Applies a <see cref="ReplaceProgramProposal"/> — the F-4b whole-face swap. Purely
    /// mechanical: every value written here was planner-resolved (spec §3); this method makes no
    /// inferences. <c>/W</c>, <c>/DW</c>, <c>/ToUnicode</c> and <c>/Encoding</c> are deliberately
    /// never touched — declared widths stay authoritative (the proposal's program is already patched
    /// to them) and the <c>/ToUnicode</c> claims are exactly what the replacement realised (spec §3
    /// steps 8-9).
    ///
    /// <para>ISO 32000-2 §9.7.4.2 expects CID 0 to map to <c>.notdef</c>, but
    /// <paramref name="proposal"/>'s <see cref="ReplaceProgramProposal.CidToGid"/> may map a dead CID 0
    /// to a REAL glyph when /ToUnicode resolves it (<c>CidReplacementMap.Build</c>, Task 4) — that is
    /// this program's approved policy, a deliberate call and not an oversight: a document that shows
    /// CID 0 and has a provable Unicode value for it is asking for that character to render, and
    /// restoring it beats preserving the .notdef convention for a code the document actually draws.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="proposal"/>'s program is empty or its
    /// <see cref="ReplaceProgramProposal.Format"/> is not <see cref="FontProgramFormat.TrueType"/>
    /// (<c>ParamName</c> <c>"proposal"</c> in both cases — this method only ever writes a TrueType
    /// sfnt to <c>/FontFile2</c>, so a hand-built <see cref="ReplaceProgramProposal"/> carrying a CFF
    /// or Type1 program would otherwise be written into <c>/FontFile2</c> under <c>/Subtype
    /// /CIDFontType2</c> as if it were valid sfnt bytes — silent corruption no consumer can load; the
    /// planner never proposes this shape, but the proposal's constructor is public), or its <c>Font</c>
    /// or its <c>CompositeFont</c> names no dictionary (<c>ParamName</c> <c>"font"</c> either way —
    /// the shared <see cref="ResolveFontDictionary"/> helper's own parameter name, not a proposal
    /// field name; do not rely on it to tell which of the two failed).</exception>
    /// <exception cref="InvalidOperationException"><paramref name="proposal"/>'s <c>Font</c> has no
    /// /FontDescriptor — the planner only proposes a replacement for an existing embedded composite
    /// font; this is the backstop.</exception>
    public void ReplaceCompositeProgram(ReplaceProgramProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposal.Program.Length == 0)
            throw new ArgumentException("A font program cannot be empty.", nameof(proposal));
        if (proposal.Format != FontProgramFormat.TrueType)
            throw new ArgumentException(
                $"ReplaceCompositeProgram only writes a TrueType sfnt to /FontFile2; proposal.Format "
                + $"was {proposal.Format}. The planner never proposes this shape — a hand-built "
                + "proposal must not reach this operation with a non-TrueType program.",
                nameof(proposal));

        PdfDictionary cidDict = ResolveFontDictionary(proposal.Font);
        PdfDictionary wrapperDict = ResolveFontDictionary(proposal.CompositeFont);
        if (Resolve(cidDict.Get("FontDescriptor")) is not PdfDictionary descriptor)
            throw new InvalidOperationException(
                $"Font object {proposal.Font.ObjectNumber} has no /FontDescriptor; "
                + "ReplaceCompositeProgram only rewrites an existing embedded composite font.");

        // 1. Program: /FontFile2 <- substitute (Flate; /Length1 = DECODED length, the FontSubsetter
        //    precedent: PdfLibrary/Optimization/FontSubsetter.cs:113-115 — a full Liberation face is
        //    ~300 KB, compression is not optional). The departing program's other carriers removed:
        //    the guard above already confirmed Format is TrueType, so /FontFile3 and /FontFile are
        //    stale leftovers of whatever the OLD program was, never something this operation itself
        //    writes.
        var programStreamDict = new PdfDictionary();
        var programStream = new PdfStream(programStreamDict, []);
        programStream.SetEncodedData(proposal.Program, "FlateDecode");
        programStreamDict.Set("Length1", new PdfInteger(proposal.Program.Length));
        PdfIndirectReference programRef = _document.RegisterObject(programStream);
        descriptor.Set("FontFile2", programRef);
        descriptor.Remove(new PdfName("FontFile3"));
        descriptor.Remove(new PdfName("FontFile"));

        // 2. Descendant identity: CIDFontType2 + explicit CIDToGIDMap + Adobe-Identity-0 (required
        //    once the GID map is custom; compatible with the untouched Identity-H CMap on the
        //    wrapper — that CMap addresses CODES, not GIDs, so it needs no change here).
        cidDict.Set("Subtype", new PdfName("CIDFontType2"));

        var cidToGidStreamDict = new PdfDictionary();
        var cidToGidStream = new PdfStream(cidToGidStreamDict, []);
        cidToGidStream.SetEncodedData(
            CidReplacementMap.ToStreamBytes(proposal.CidToGid, proposal.MaxCid), "FlateDecode");
        PdfIndirectReference cidToGidRef = _document.RegisterObject(cidToGidStream);
        cidDict.Set("CIDToGIDMap", cidToGidRef);

        var cidSystemInfo = new PdfDictionary();
        cidSystemInfo.Set("Registry", new PdfString(Encoding.ASCII.GetBytes("Adobe")));
        cidSystemInfo.Set("Ordering", new PdfString(Encoding.ASCII.GetBytes("Identity")));
        cidSystemInfo.Set("Supplement", new PdfInteger(0));
        cidDict.Set("CIDSystemInfo", cidSystemInfo);

        // 3. Names, both levels — stale metrics describing the departed program would be a quiet lie.
        wrapperDict.Set("BaseFont", new PdfName(proposal.NewBaseFont));
        cidDict.Set("BaseFont", new PdfName(proposal.NewBaseFont));
        descriptor.Set("FontName", new PdfName(proposal.NewBaseFont));

        // 4. Descriptor rebuilt from the substitute's own tables; stale /CIDSet dropped (a full embed
        //    is not a subset — spec §3 step 7). No /MissingWidth: meaningless on a CID descriptor,
        //    which declares its fallback width with /DW on the descendant, not /MissingWidth on the
        //    descriptor (ISO 32000-2 §9.7.4.3) — see WriteMetricEntries.
        descriptor.Set("Flags", new PdfInteger(proposal.DescriptorFlags));
        WriteMetricEntries(descriptor, proposal.Descriptor, includeMissingWidth: false);
        descriptor.Remove(new PdfName("CIDSet"));
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
