using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PdfLibrary.Builder;
using PdfLibrary.Conformance;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// The resave-verification gate for the "fixed by saving with Pellucid" remediation class
/// (2026-08-10 remediation-spine, Task 2). Per candidate rule: build ORIGINAL BYTES that genuinely fail
/// rule R under PdfA2b (assert the finding is present — the fixture premise is proven), load the
/// document, run it through <see cref="PdfDocumentEditor"/>'s full-rewrite <c>Save</c>, then re-check the
/// saved bytes — asserting R is GONE and that no NEW RuleId appears versus the pre-save finding set
/// (<see cref="PdfDocumentSerializer"/> is a deterministic full rewrite: fresh header, offsets, xref and
/// trailer, no time/GUID in the write path — see PdfDocumentSerializer.cs).
///
/// Four of the five candidate rules PASS this gate (post-eof, file-header, indirect-object-spacing,
/// stream-object): each is a structural byproduct of always writing a fresh, correctly-framed file, so a
/// plain resave clears the violation with no extra work. file-id is the documented NEGATIVE: the
/// serializer only propagates an existing <c>Trailer.Id</c> (PdfDocumentSerializer.cs:70) — it never mints
/// one — so a document with no /ID keeps failing after a plain save; the finding only clears once the
/// caller sets <c>Trailer.Id</c> itself before saving. Both outcomes are equally valid deliverables of this
/// harness: it exists to tell the two classes of rule apart, not to force every rule to pass.
/// </summary>
public class ResaveVerificationTests
{
    private static PdfName N(string s) => new(s);
    private static PdfString Str(params byte[] bytes) => new(bytes);

    /// <summary>Runs the preflight over raw bytes for PDF/A-2b.</summary>
    private static PreflightResult CheckBytes(byte[] bytes) => Preflighter.Check(bytes, ConformanceProfile.PdfA2b);

    /// <summary>Loads <paramref name="bytes"/>, opens an editor, lets the caller mutate the in-memory
    /// document, then performs a plain full-rewrite Save and returns the resulting bytes.</summary>
    private static byte[] LoadEditSave(byte[] bytes, Action<PdfDocumentEditor>? mutate = null)
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(bytes, writable: false));
        using PdfDocumentEditor editor = doc.Edit();
        mutate?.Invoke(editor);
        using var ms = new MemoryStream();
        editor.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Asserts that <paramref name="after"/> introduces no RuleId absent from <paramref name="before"/>,
    /// other than those explicitly allowed via <paramref name="permittedNew"/> (rules the save legitimately
    /// fixes or that are expected to appear/disappear as a documented side effect).
    /// </summary>
    private static void AssertNoNewRuleIds(PreflightResult before, PreflightResult after, params string[] permittedNew)
    {
        var beforeIds = before.Findings.Select(f => f.RuleId).ToHashSet();
        var afterIds = after.Findings.Select(f => f.RuleId).ToHashSet();
        var unexpected = afterIds.Except(beforeIds).Except(permittedNew).ToList();
        Assert.True(unexpected.Count == 0,
            $"Save introduced RuleId(s) not present before save and not in the permitted set "
            + $"[{string.Join(", ", permittedNew)}]: {string.Join(", ", unexpected)}. "
            + $"Before: [{string.Join(", ", beforeIds)}]. After: [{string.Join(", ", afterIds)}].");
    }

    // ── shared minimal byte-exact PDF builder (adapted from IndirectObjectSpacingRuleTests /
    // StreamObjectRuleTests: a valid classic xref table whose offsets point at each object number, so
    // byte-level rules see real spacing/framing) ─────────────────────────────────────────────────────────

    private sealed class Pdf
    {
        private readonly List<byte> _buf = [];
        private readonly List<(int num, int gen, long off)> _xref = [];

        public Pdf()
        {
            Ascii("%PDF-1.7\n");
            _buf.Add((byte)'%');
            _buf.AddRange([0xE2, 0xE3, 0xCF, 0xD3]); // binary marker line
            _buf.Add((byte)'\n');
        }

        public Pdf Obj(int num, int gen, byte[] rawText)
        {
            _xref.Add((num, gen, _buf.Count));
            _buf.AddRange(rawText);
            return this;
        }

        public Pdf Obj(int num, int gen, string rawText) => Obj(num, gen, L(rawText));

        public byte[] Build()
        {
            long xrefPos = _buf.Count;
            int size = _xref.Max(e => e.num) + 1;
            var slots = new (long off, int gen, char t)[size];
            for (int i = 0; i < size; i++) slots[i] = (0, i == 0 ? 65535 : 0, 'f');
            foreach ((int num, int gen, long off) in _xref) slots[num] = (off, gen, 'n');

            var sb = new StringBuilder();
            sb.Append("xref\r\n0 ").Append(size).Append("\r\n");
            for (int i = 0; i < size; i++)
                sb.Append(slots[i].off.ToString("D10")).Append(' ')
                  .Append(slots[i].gen.ToString("D5")).Append(' ').Append(slots[i].t).Append("\r\n");
            sb.Append("trailer\r\n<< /Size ").Append(size).Append(" /Root 1 0 R >>\r\nstartxref\r\n")
              .Append(xrefPos).Append("\r\n%%EOF");
            Ascii(sb.ToString());
            return [.. _buf];
        }

        private void Ascii(string s) { foreach (char c in s) _buf.Add((byte)c); }
    }

    private static byte[] L(string s) => Encoding.Latin1.GetBytes(s);

    // ── post-eof ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A clean <see cref="PdfDocumentBuilder"/> document, plus trailing garbage bytes after the last
    /// <c>%%EOF</c>: PostEofDataRule fires on the original bytes; a full-rewrite Save writes fresh bytes
    /// that end cleanly at the serializer's own <c>%%EOF</c>, with nothing appended, so the finding cannot
    /// survive.
    /// </summary>
    [Fact]
    public void PostEof_finding_is_cleared_by_a_plain_save()
    {
        byte[] clean = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hello", 100, 700))
            .ToByteArray();
        byte[] original = [.. clean, .. Encoding.ASCII.GetBytes("\ngarbage after eof")];

        PreflightResult before = CheckBytes(original);
        Assert.Contains(before.Findings, f => f.RuleId == "post-eof" && f.Severity == FindingSeverity.Error);

        byte[] saved = LoadEditSave(original);
        PreflightResult after = CheckBytes(saved);

        Assert.DoesNotContain(after.Findings, f => f.RuleId == "post-eof");
        AssertNoNewRuleIds(before, after);
    }

    /// <summary>
    /// Discrimination check (Task 2, step "discrimination check"): checking the ORIGINAL bytes twice —
    /// never routing through Save — must NOT make the finding disappear on its own. This proves the
    /// gone-assertion above is actually exercising the save path rather than trivially passing.
    /// </summary>
    [Fact]
    public void PostEof_discrimination_the_finding_does_not_vanish_without_a_save()
    {
        byte[] clean = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hello", 100, 700))
            .ToByteArray();
        byte[] original = [.. clean, .. Encoding.ASCII.GetBytes("\ngarbage after eof")];

        PreflightResult first = CheckBytes(original);
        PreflightResult second = CheckBytes(original); // same bytes, no save in between

        Assert.Contains(first.Findings, f => f.RuleId == "post-eof");
        Assert.Contains(second.Findings, f => f.RuleId == "post-eof"); // still present — no save happened
    }

    // ── file-header ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="PdfDocumentBuilder"/>'s own header is already non-conformant: PdfDocumentWriter writes
    /// the binary-marker comment line via <c>StreamWriter(stream, Encoding.ASCII)</c> (PdfDocumentWriter.cs
    /// ~line 133), so the non-ASCII marker bytes (â ã Ï Ó) fall back to <c>?</c> (0x3F) under ASCII
    /// encoding — every one of the first four comment bytes ends up ≤ 127, which is exactly the violation
    /// FileHeaderRule flags. No mutation is needed to produce a genuinely-failing, genuinely-loadable
    /// fixture; the builder's real output already fails file-header. PdfDocumentSerializer's header, by
    /// contrast, writes the binary marker as raw bytes (0xE2 0xE3 0xCF 0xD3, all &gt; 127) — never through a
    /// text encoder — so a full-rewrite Save always produces a conformant header.
    /// </summary>
    [Fact]
    public void FileHeader_finding_is_cleared_by_a_plain_save()
    {
        byte[] original = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hello", 100, 700))
            .ToByteArray();

        PreflightResult before = CheckBytes(original);
        Assert.Contains(before.Findings, f => f.RuleId == "file-header" && f.Severity == FindingSeverity.Error);

        byte[] saved = LoadEditSave(original);
        PreflightResult after = CheckBytes(saved);

        Assert.DoesNotContain(after.Findings, f => f.RuleId == "file-header");
        AssertNoNewRuleIds(before, after);
    }

    // ── indirect-object-spacing ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A three-object document (catalog → pages → page, all reachable so Save's default orphan-GC does not
    /// simply delete the offending object out from under the rule) whose page object has two spaces
    /// between its object and generation numbers — a genuine 6.1.9 violation, adapted from
    /// IndirectObjectSpacingRuleTests. PdfDocumentSerializer.SerializeIndirectObject always writes
    /// <c>"{num} {gen} obj\n"</c> (single space, LF-framed), so a resave normalizes the spacing away
    /// regardless of how the source was framed.
    /// </summary>
    [Fact]
    public void IndirectObjectSpacing_finding_is_cleared_by_a_plain_save()
    {
        byte[] original = new Pdf()
            .Obj(1, 0, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n")
            .Obj(3, 0, "3  0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n") // 2 spaces
            .Build();

        PreflightResult before = CheckBytes(original);
        Assert.Contains(before.Findings,
            f => f.RuleId == "indirect-object-spacing" && f.Severity == FindingSeverity.Error && f.ObjectNumber == 3);

        byte[] saved = LoadEditSave(original);
        PreflightResult after = CheckBytes(saved);

        Assert.DoesNotContain(after.Findings, f => f.RuleId == "indirect-object-spacing");
        AssertNoNewRuleIds(before, after);
    }

    // ── stream-object ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A four-object document (catalog → pages → page → content stream, the stream reachable via the
    /// page's /Contents so Save's orphan-GC keeps it) whose stream declares <c>/Length 3</c> for six bytes
    /// of actual data — a genuine 6.1.7.1 test-1 violation, adapted from StreamObjectRuleTests.
    /// <see cref="PdfStream"/> keeps its /Length entry synced to the real data length on every
    /// construction/mutation (PdfStream.cs:22/46), and the loader recovers the true stream bytes from
    /// <c>endstream</c> rather than trusting a bogus declared length, so by the time
    /// PdfDocumentSerializer re-serializes the stream, its /Length is already correct and its framing is
    /// always <c>stream\n…\nendstream</c> (PdfStream.ToBytes()) — a resave clears the violation.
    /// </summary>
    [Fact]
    public void StreamObject_finding_is_cleared_by_a_plain_save()
    {
        byte[] streamObj = L("4 0 obj\n<< /Length 3 >>\nstream\r\nhello!\r\nendstream\nendobj\n"); // 6 bytes, /Length 3
        byte[] original = new Pdf()
            .Obj(1, 0, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n")
            .Obj(3, 0, "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>\nendobj\n")
            .Obj(4, 0, streamObj)
            .Build();

        PreflightResult before = CheckBytes(original);
        Assert.Contains(before.Findings,
            f => f.RuleId == "stream-object" && f.Severity == FindingSeverity.Error && f.ObjectNumber == 4);

        byte[] saved = LoadEditSave(original);
        PreflightResult after = CheckBytes(saved);

        Assert.DoesNotContain(after.Findings, f => f.RuleId == "stream-object");
        AssertNoNewRuleIds(before, after);
    }

    // ── file-id: the proven negative ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a genuinely-failing, loadable fixture with no /ID: <see cref="PdfDocumentBuilder"/> actually
    /// writes an /ID (PdfDocumentWriter.cs ~line 711, "required for encryption, recommended otherwise"),
    /// so this strips it by loading the builder's output, removing the trailer's /ID key, and doing one
    /// legitimate editor Save — the resulting bytes are the fixture, not yet part of the fact under test.
    /// </summary>
    private static byte[] BuilderBytesWithoutId()
    {
        byte[] raw = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hello", 100, 700))
            .ToByteArray();
        return LoadEditSave(raw, editor => editor.Document.Trailer.Dictionary.Remove(N("ID")));
    }

    /// <summary>
    /// Half 1 — the negative: file-id fires on the /ID-less original, and a PLAIN resave does not clear it.
    /// PdfDocumentSerializer only ever propagates an existing <c>document.Trailer.Id</c>
    /// (PdfDocumentSerializer.cs:70, <c>if (document.Trailer.Id is { } id) ...</c>) — it never mints a new
    /// one — so a document that had no /ID before saving still has none after.
    /// </summary>
    [Fact]
    public void FileId_finding_survives_a_plain_save()
    {
        byte[] original = BuilderBytesWithoutId();

        PreflightResult before = CheckBytes(original);
        Assert.Contains(before.Findings, f => f.RuleId == "file-id" && f.Severity == FindingSeverity.Error);

        byte[] saved = LoadEditSave(original); // plain save — no /ID set
        PreflightResult after = CheckBytes(saved);

        Assert.Contains(after.Findings, f => f.RuleId == "file-id" && f.Severity == FindingSeverity.Error);
    }

    /// <summary>
    /// Half 2 — the fix that stands on the negative: setting <c>doc.Trailer.Id</c> to a two-non-empty-byte-
    /// string array before saving clears file-id. This is the evidence Task 3's file-id remediation (set
    /// /ID when absent, then save) is built on.
    /// </summary>
    [Fact]
    public void FileId_finding_is_cleared_when_the_caller_sets_trailer_id_before_saving()
    {
        byte[] original = BuilderBytesWithoutId();

        PreflightResult before = CheckBytes(original);
        Assert.Contains(before.Findings, f => f.RuleId == "file-id" && f.Severity == FindingSeverity.Error);

        byte[] saved = LoadEditSave(original, editor =>
            editor.Document.Trailer.Id = new PdfArray(Str(0x01, 0x02, 0x03, 0x04), Str(0x05, 0x06, 0x07, 0x08)));
        PreflightResult after = CheckBytes(saved);

        Assert.DoesNotContain(after.Findings, f => f.RuleId == "file-id");
        AssertNoNewRuleIds(before, after);
    }

    // ── font-dictionary 6.2.11.3.2 — CIDToGIDMap (Task 2: SetCidToGidMapIdentity) ───────────────────────

    private static string? ClauseKey(Finding f) => ParitySnapshot.ClauseKey(f.Clause);

    /// <summary>Serializes a hand-built in-memory <see cref="PdfDocument"/> to real file bytes via a
    /// plain (unmutated) editor Save — the "original" bytes a font-dictionary fixture needs, since no
    /// <see cref="PdfDocumentBuilder"/> support exists for constructing a composite font.</summary>
    private static byte[] SavedBytes(PdfDocument document)
    {
        using PdfDocumentEditor editor = document.Edit();
        using var ms = new MemoryStream();
        editor.Save(ms);
        return ms.ToArray();
    }

    /// <summary>A Type0 font (object 20) over a CIDFontType2 descendant (object 21) with no
    /// /CIDToGIDMap — a genuine 6.2.11.3.2 violation — reachable via a page's /Resources /Font so
    /// <c>ConformanceContext.ReferencedFonts</c> (and hence <see cref="FontInventory"/>) sees it.
    /// Mirrors <c>PdfDocumentEditorFontsTests.BuildType0Document</c>.</summary>
    private static PdfDocument CidFontType2WithoutMapDocument()
    {
        var doc = new PdfDocument();
        doc.AddObject(22, 0, new PdfStream(new PdfDictionary { [N("Length1")] = new PdfInteger(0) }, []));
        doc.AddObject(21, 0, new PdfDictionary
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
                [N("Type")] = N("FontDescriptor"),
                [N("FontName")] = N("CIDFontX"),
                [N("FontFile2")] = new PdfIndirectReference(22, 0),
            },
            // deliberately no /CIDToGIDMap — the violation under test
        });
        doc.AddObject(20, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("CIDFontX"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(new PdfIndirectReference(21, 0)),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf <0001> Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = new PdfIndirectReference(2, 0),
            [N("MediaBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792)),
            [N("Contents")] = new PdfIndirectReference(11, 0),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = new PdfDictionary { [N("F0")] = new PdfIndirectReference(20, 0) },
            },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(new PdfIndirectReference(3, 0)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = new PdfIndirectReference(2, 0) });
        doc.Trailer.Dictionary[N("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    /// <summary>
    /// The 6.2.11.3.2 finding on a genuinely non-conformant CIDFontType2 (no /CIDToGIDMap) is cleared
    /// once <see cref="PdfDocumentEditor.SetCidToGidMapIdentity"/> writes <c>/CIDToGIDMap /Identity</c>
    /// onto the descendant before saving. Includes the discrimination check (re-checking the ORIGINAL,
    /// unsaved bytes twice) proving the "before" assertion is not a fluke of a single run.
    /// </summary>
    [Fact]
    public void CidToGidMap_finding_is_cleared_by_SetCidToGidMapIdentity_before_save()
    {
        byte[] original = SavedBytes(CidFontType2WithoutMapDocument());

        PreflightResult before = CheckBytes(original);
        PreflightResult beforeAgain = CheckBytes(original); // discrimination: same bytes, no save in between
        Assert.Contains(before.Findings, f => f.RuleId == "font-dictionary" && ClauseKey(f) == "6.2.11.3.2");
        Assert.Contains(beforeAgain.Findings, f => f.RuleId == "font-dictionary" && ClauseKey(f) == "6.2.11.3.2");

        byte[] saved = LoadEditSave(original, editor =>
        {
            FontInventoryEntry entry = Assert.Single(
                FontInventory.Read(editor.Document), e => e.Kind == FontKind.Type0CidType2);
            Assert.True(editor.SetCidToGidMapIdentity(entry.ProgramHolderId!.Value));
        });
        PreflightResult after = CheckBytes(saved);

        Assert.DoesNotContain(after.Findings, f => f.RuleId == "font-dictionary" && ClauseKey(f) == "6.2.11.3.2");
        AssertNoNewRuleIds(before, after);
    }

    // ── font-dictionary 6.2.11.6 — symbolic TrueType /Encoding (Task 2b: RemoveSymbolicEncoding) ───────

    /// <summary>A symbolic TrueType font (object 31, /FontDescriptor /Flags 4) that also carries an
    /// /Encoding — a genuine 6.2.11.6 violation — reachable via a page's /Resources /Font.</summary>
    private static PdfDocument SymbolicTrueTypeWithEncodingDocument()
    {
        var doc = new PdfDocument();
        doc.AddObject(31, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("ABCDEF+TestFont"),
            [N("Encoding")] = N("WinAnsiEncoding"), // the violation under test — a symbolic font must not carry this
            [N("FirstChar")] = new PdfInteger(65),
            [N("LastChar")] = new PdfInteger(65),
            [N("Widths")] = new PdfArray(new PdfInteger(722)),
            [N("FontDescriptor")] = new PdfDictionary
            {
                [N("Type")] = N("FontDescriptor"),
                [N("FontName")] = N("ABCDEF+TestFont"),
                [N("Flags")] = new PdfInteger(4), // Symbolic (bit 3)
            },
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf (A) Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = new PdfIndirectReference(2, 0),
            [N("MediaBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792)),
            [N("Contents")] = new PdfIndirectReference(11, 0),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = new PdfDictionary { [N("F0")] = new PdfIndirectReference(31, 0) },
            },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(new PdfIndirectReference(3, 0)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = new PdfIndirectReference(2, 0) });
        doc.Trailer.Dictionary[N("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    /// <summary>
    /// The 6.2.11.6 finding on a genuinely non-conformant symbolic TrueType font (carries /Encoding) is
    /// cleared once <see cref="PdfDocumentEditor.RemoveSymbolicEncoding"/> removes it before saving.
    /// Includes the same discrimination check as the 6.2.11.3.2 fact above.
    /// </summary>
    [Fact]
    public void SymbolicEncoding_finding_is_cleared_by_RemoveSymbolicEncoding_before_save()
    {
        byte[] original = SavedBytes(SymbolicTrueTypeWithEncodingDocument());

        PreflightResult before = CheckBytes(original);
        PreflightResult beforeAgain = CheckBytes(original); // discrimination: same bytes, no save in between
        Assert.Contains(before.Findings, f => f.RuleId == "font-dictionary" && ClauseKey(f) == "6.2.11.6");
        Assert.Contains(beforeAgain.Findings, f => f.RuleId == "font-dictionary" && ClauseKey(f) == "6.2.11.6");

        byte[] saved = LoadEditSave(original, editor =>
        {
            FontInventoryEntry entry = Assert.Single(
                FontInventory.Read(editor.Document), e => e.Kind == FontKind.TrueType);
            Assert.True(editor.RemoveSymbolicEncoding(entry.Id));
        });
        PreflightResult after = CheckBytes(saved);

        Assert.DoesNotContain(after.Findings, f => f.RuleId == "font-dictionary" && ClauseKey(f) == "6.2.11.6");
        AssertNoNewRuleIds(before, after);
    }
}
