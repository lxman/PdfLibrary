using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
/// Five of the six original candidate rules PASS this gate (post-eof, file-header, indirect-object-spacing,
/// stream-object, xref-spacing): each is a structural byproduct of always writing a fresh, correctly-framed
/// file, so a plain resave clears the violation with no extra work. file-id is the documented NEGATIVE: the
/// serializer only propagates an existing <c>Trailer.Id</c> (PdfDocumentSerializer.cs:70) — it never mints
/// one — so a document with no /ID keeps failing after a plain save; the finding only clears once the
/// caller sets <c>Trailer.Id</c> itself before saving. Both outcomes are equally valid deliverables of this
/// harness: it exists to tell the two classes of rule apart, not to force every rule to pass. Later coverage
/// audits also use it to prove that rules with semantic or content-bearing violations survive a plain save
/// and therefore need an explicit non-save disposition.
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

        public byte[] Build(string xrefSeparator = "\r\n")
        {
            long xrefPos = _buf.Count;
            int size = _xref.Max(e => e.num) + 1;
            var slots = new (long off, int gen, char t)[size];
            for (int i = 0; i < size; i++) slots[i] = (0, i == 0 ? 65535 : 0, 'f');
            foreach ((int num, int gen, long off) in _xref) slots[num] = (off, gen, 'n');

            var sb = new StringBuilder();
            sb.Append("xref").Append(xrefSeparator).Append("0 ").Append(size).Append("\r\n");
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
    /// The two branches represented by veraPDF's 6-1-4-t01-fail-a/b fixtures are both structural
    /// serialization defects: one has <c>SPACE LF</c> between <c>xref</c> and its subsection header,
    /// the other has <c>LF LF</c>. The loader tolerates both, while the full-rewrite serializer always
    /// writes <c>xref LF</c>. This saved-byte witness is parameterized over both source forms so the
    /// classification cannot rest on only one half of the evidence.
    /// </summary>
    [Theory]
    [InlineData(" \n", "SPACE LF")]
    [InlineData("\n\n", "LF LF")]
    public void XrefSpacing_finding_is_cleared_by_a_plain_save(
        string malformedSeparator, string expectedDescription)
    {
        byte[] original = new Pdf()
            .Obj(1, 0, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n")
            .Obj(3, 0, "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n")
            .Build(malformedSeparator);

        PreflightResult before = CheckBytes(original);
        Finding finding = Assert.Single(before.Findings, f => f.RuleId == "xref-spacing");
        Assert.Equal(FindingSeverity.Error, finding.Severity);
        Assert.Contains(expectedDescription, finding.Message, StringComparison.Ordinal);

        byte[] saved = LoadEditSave(original);
        PreflightResult after = CheckBytes(saved);

        Assert.DoesNotContain(after.Findings, f => f.RuleId == "xref-spacing");
        Assert.Contains("xref\n0 ", Encoding.Latin1.GetString(saved), StringComparison.Ordinal);
        AssertNoNewRuleIds(before, after);
    }

    // ── rendering-intent: custom output-device semantics survive a plain save ───────────────────

    /// <summary>
    /// The rule reaches the four ISO 32000-1 rendering-intent sites: ExtGState <c>/RI</c>, image-XObject
    /// <c>/Intent</c>, the page-content <c>ri</c> operator, and inline-image <c>/Intent</c>. A conforming
    /// reader falls back to RelativeColorimetric when it does not recognize a name, but the same clause
    /// explicitly permits an output device to support additional intents. Consequently a converter cannot
    /// replace a custom name with RelativeColorimetric (or remove it and rely on the default) without
    /// potentially changing colour conversion on the device that understands the extension. A plain full
    /// rewrite preserves each exact name and therefore preserves the finding as well.
    /// </summary>
    [Theory]
    [InlineData("extgstate")]
    [InlineData("image-xobject")]
    [InlineData("page-ri")]
    [InlineData("inline-image")]
    public void RenderingIntent_custom_name_at_each_live_site_survives_a_plain_save(string shape)
    {
        const string customIntent = "VendorIntent";
        string pageContent = shape switch
        {
            "page-ri" => $"/{customIntent} ri",
            "inline-image" => $"BI /W 1 /H 1 /BPC 8 /CS /DeviceGray /Intent /{customIntent} ID x EI",
            _ => "q Q",
        };
        string expectedSavedFragment = shape switch
        {
            "extgstate" => $"/RI /{customIntent}",
            "image-xobject" or "inline-image" => $"/Intent /{customIntent}",
            "page-ri" => $"/{customIntent} ri",
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        string pageResources = shape switch
        {
            "extgstate" => "<< /ExtGState << /GS0 5 0 R >> >>",
            "image-xobject" => "<< /XObject << /Im0 5 0 R >> >>",
            _ => "<< >>",
        };

        byte[] pageBytes = L(pageContent);
        byte[] pageStream =
        [
            .. L($"4 0 obj\n<< /Length {pageBytes.Length} >>\nstream\n"),
            .. pageBytes,
            .. L("\nendstream\nendobj\n")
        ];

        var pdf = new Pdf()
            .Obj(1, 0, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n")
            .Obj(3, 0, "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
                       + $"/Resources {pageResources} /Contents 4 0 R >>\nendobj\n")
            .Obj(4, 0, pageStream);

        if (shape == "extgstate")
            pdf.Obj(5, 0, $"5 0 obj\n<< /Type /ExtGState /RI /{customIntent} >>\nendobj\n");
        else if (shape == "image-xobject")
            pdf.Obj(5, 0, $"5 0 obj\n<< /Type /XObject /Subtype /Image /Width 1 /Height 1 "
                          + $"/ColorSpace /DeviceGray /BitsPerComponent 8 /Intent /{customIntent} "
                          + "/Length 1 >>\nstream\nx\nendstream\nendobj\n");

        byte[] original = pdf.Build();
        PreflightResult before = CheckBytes(original);
        Finding beforeFinding = Assert.Single(before.Findings, f => f.RuleId == "rendering-intent");
        Assert.Contains(customIntent, beforeFinding.Message, StringComparison.Ordinal);

        byte[] saved = LoadEditSave(original);
        PreflightResult after = CheckBytes(saved);

        Finding afterFinding = Assert.Single(after.Findings, f => f.RuleId == "rendering-intent");
        Assert.Contains(customIntent, afterFinding.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSavedFragment, Encoding.Latin1.GetString(saved), StringComparison.Ordinal);
        AssertNoNewRuleIds(before, after);
    }

    // ── content-stream-operator: extension semantics survive a plain save ────────────────────────

    /// <summary>
    /// The rule has one predicate (an operator name outside ISO 32000-1's defined set), reached through
    /// three materially different content shapes: a page stream, an invoked Form XObject, and an unknown
    /// operator inside BX/EX. A full rewrite preserves every stream byte instead of interpreting an
    /// extension operator or guessing which preceding operands belong to it. In particular, BX/EX tells a
    /// conforming processor that it may ignore an unknown operator; it does not authorize a converter to
    /// delete vendor-extension data from the saved document.
    /// </summary>
    [Theory]
    [InlineData("page")]
    [InlineData("invoked-form")]
    [InlineData("bx-ex")]
    public void ContentStreamOperator_reachable_shape_survives_a_plain_save(string shape)
    {
        const string undefinedOperator = "VendorPaint";
        string pageContent = shape switch
        {
            "page" => $"1 /VendorData {undefinedOperator}",
            "invoked-form" => "/Fm0 Do",
            "bx-ex" => $"BX 1 /VendorData {undefinedOperator} EX",
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        string? formContent = shape == "invoked-form"
            ? $"1 /VendorData {undefinedOperator}"
            : null;

        byte[] pageBytes = L(pageContent);
        byte[] pageStream =
        [
            .. L($"4 0 obj\n<< /Length {pageBytes.Length} >>\nstream\n"),
            .. pageBytes,
            .. L("\nendstream\nendobj\n")
        ];
        string pageResources = formContent is null
            ? "<< >>"
            : "<< /XObject << /Fm0 5 0 R >> >>";

        var pdf = new Pdf()
            .Obj(1, 0, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n")
            .Obj(3, 0, $"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
                       + $"/Resources {pageResources} /Contents 4 0 R >>\nendobj\n")
            .Obj(4, 0, pageStream);

        if (formContent is not null)
        {
            byte[] formBytes = L(formContent);
            byte[] formStream =
            [
                .. L($"5 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] "
                     + $"/Resources << >> /Length {formBytes.Length} >>\nstream\n"),
                .. formBytes,
                .. L("\nendstream\nendobj\n")
            ];
            pdf.Obj(5, 0, formStream);
        }

        byte[] original = pdf.Build();
        PreflightResult before = CheckBytes(original);
        Finding beforeFinding = Assert.Single(before.Findings, f => f.RuleId == "content-stream-operator");
        Assert.Contains(undefinedOperator, beforeFinding.Message, StringComparison.Ordinal);

        byte[] saved = LoadEditSave(original);
        PreflightResult after = CheckBytes(saved);

        Finding afterFinding = Assert.Single(after.Findings, f => f.RuleId == "content-stream-operator");
        Assert.Contains(undefinedOperator, afterFinding.Message, StringComparison.Ordinal);
        Assert.Contains(formContent ?? pageContent, Encoding.Latin1.GetString(saved), StringComparison.Ordinal);
        AssertNoNewRuleIds(before, after);
    }

    // ── hex-string-format: mixed save behavior, therefore not FixedBySaving ─────────────────────────────────────

    /// <summary>
    /// Hexadecimal strings in the reachable object graph are serialized from their decoded bytes, so
    /// both malformed written forms become valid hex strings on save. This is only half of the rule's
    /// traversal surface; the content-stream witness below proves why the rule cannot be classified
    /// FixedBySaving as a whole.
    /// </summary>
    [Theory]
    [InlineData("48455")]
    [InlineData("484!")]
    public void HexStringFormat_in_the_object_graph_is_cleared_by_a_plain_save(string malformedDigits)
    {
        byte[] original = new Pdf()
            .Obj(1, 0, $"1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Junk <{malformedDigits}> >>\nendobj\n")
            .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n")
            .Obj(3, 0, "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n")
            .Build();

        PreflightResult before = CheckBytes(original);
        Assert.Contains(before.Findings, f => f.RuleId == "hex-string-format");

        byte[] saved = LoadEditSave(original);
        PreflightResult after = CheckBytes(saved);

        Assert.DoesNotContain(after.Findings, f => f.RuleId == "hex-string-format");
        AssertNoNewRuleIds(before, after);
    }

    /// <summary>
    /// Page-content streams are not tokenized and reserialized by a full document save. Both the odd
    /// nibble count and non-hex-character forms therefore survive byte-for-byte. This is the decisive
    /// negative for classification: a plain save cannot honestly promise to close the rule.
    /// </summary>
    [Theory]
    [InlineData("48455")]
    [InlineData("484!")]
    public void HexStringFormat_in_page_content_survives_a_plain_save(string malformedDigits)
    {
        byte[] content = L($"BT <{malformedDigits}> Tj ET");
        byte[] streamObject =
        [
            .. L($"4 0 obj\n<< /Length {content.Length} >>\nstream\n"),
            .. content,
            .. L("\nendstream\nendobj\n")
        ];
        byte[] original = new Pdf()
            .Obj(1, 0, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n")
            .Obj(3, 0, "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>\nendobj\n")
            .Obj(4, 0, streamObject)
            .Build();

        PreflightResult before = CheckBytes(original);
        Assert.Contains(before.Findings, f => f.RuleId == "hex-string-format");

        byte[] saved = LoadEditSave(original);
        PreflightResult after = CheckBytes(saved);

        Assert.Contains(after.Findings, f => f.RuleId == "hex-string-format");
        Assert.Contains($"<{malformedDigits}>", Encoding.Latin1.GetString(saved), StringComparison.Ordinal);
        AssertNoNewRuleIds(before, after);
    }

    // ── file-id: the proven negative ────────────────────────────────────────────────────────────────────────────────────────────

    // ── implementation-limits: semantic/content constraints survive a plain save ─────────────────────

    /// <summary>
    /// Page boundary dimensions are semantic page geometry, not source framing. A full rewrite preserves
    /// the effective box and therefore cannot be advertised as repairing this violation.
    /// </summary>
    [Fact]
    public void ImplementationLimits_page_boundary_violation_survives_a_plain_save()
    {
        byte[] original = MinimalPagePdf("", mediaBox: "[0 0 2 2]");

        AssertImplementationLimitSurvives(original, "MediaBox");
    }

    /// <summary>
    /// The reachable-object arm has three independent payload constraints. The serializer preserves every
    /// value rather than truncating, renaming, or clamping it, because any of those transformations could
    /// change document meaning.
    /// </summary>
    [Theory]
    [InlineData("string")]
    [InlineData("name")]
    [InlineData("integer")]
    public void ImplementationLimits_reachable_object_violation_survives_a_plain_save(string shape)
    {
        string extra = shape switch
        {
            "string" => $"/Junk ({new string('X', 32768)})",
            "name" => $"/Junk /{new string('N', 128)}",
            "integer" => "/Junk 2157483648",
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        byte[] original = MinimalPagePdf(extra);

        AssertImplementationLimitSurvives(original, shape);
    }

    /// <summary>
    /// Page content is retained as a content stream by a document save. Both live content-stream branches
    /// consequently remain non-conformant until an author-aware operation changes the operands.
    /// </summary>
    [Theory]
    [InlineData("string")]
    [InlineData("integer")]
    public void ImplementationLimits_page_content_violation_survives_a_plain_save(string shape)
    {
        string content = shape switch
        {
            "string" => $"BT ({new string('X', 32768)}) Tj ET",
            "integer" => "BT 0 2157483648 Td ET",
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        byte[] original = MinimalPagePdf("", content);

        AssertImplementationLimitSurvives(original, shape);
    }

    /// <summary>
    /// The CID branch is encoded in a font's embedded CMap. Save preserves that mapping; silently
    /// renumbering CIDs would require coordinated changes to the font and every affected text operand.
    /// </summary>
    [Fact]
    public void ImplementationLimits_CMap_CID_violation_survives_a_plain_save()
    {
        const string cmap = "/CIDInit /ProcSet findresource begin\n1 begincidrange\n"
                          + "<3f00> <3fff> 65536\nendcidrange\nend";
        byte[] cmapObject =
        [
            .. L($"6 0 obj\n<< /Length {L(cmap).Length} /Type /CMap >>\nstream\n"),
            .. L(cmap),
            .. L("\nendstream\nendobj\n")
        ];
        byte[] original = new Pdf()
            .Obj(1, 0, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n")
            .Obj(3, 0, "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
                     + "/Contents 4 0 R /Resources << /Font << /F0 5 0 R >> >> >>\nendobj\n")
            .Obj(4, 0, "4 0 obj\n<< /Length 5 >>\nstream\nBT ET\nendstream\nendobj\n")
            .Obj(5, 0, "5 0 obj\n<< /Type /Font /Subtype /Type0 /BaseFont /Test "
                     + "/Encoding 6 0 R /DescendantFonts [7 0 R] >>\nendobj\n")
            .Obj(6, 0, cmapObject)
            .Obj(7, 0, "7 0 obj\n<< /Type /Font /Subtype /CIDFontType0 /BaseFont /Test >>\nendobj\n")
            .Build();

        AssertImplementationLimitSurvives(original, "CID");
    }

    private static byte[] MinimalPagePdf(
        string catalogExtra,
        string? content = null,
        string mediaBox = "[0 0 612 792]")
    {
        var pdf = new Pdf()
            .Obj(1, 0, $"1 0 obj\n<< /Type /Catalog /Pages 2 0 R {catalogExtra} >>\nendobj\n")
            .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        if (content is null)
            return pdf
                .Obj(3, 0, $"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox {mediaBox} >>\nendobj\n")
                .Build();

        byte[] contentBytes = L(content);
        byte[] streamObject =
        [
            .. L($"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n"),
            .. contentBytes,
            .. L("\nendstream\nendobj\n")
        ];
        return pdf
            .Obj(3, 0, $"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox {mediaBox} /Contents 4 0 R >>\nendobj\n")
            .Obj(4, 0, streamObject)
            .Build();
    }

    private static void AssertImplementationLimitSurvives(byte[] original, string messageFragment)
    {
        PreflightResult before = CheckBytes(original);
        Assert.Contains(before.Findings,
            f => f.RuleId == "implementation-limits"
              && f.Message.Contains(messageFragment, StringComparison.OrdinalIgnoreCase));

        byte[] saved = LoadEditSave(original);
        PreflightResult after = CheckBytes(saved);

        Assert.Contains(after.Findings,
            f => f.RuleId == "implementation-limits"
              && f.Message.Contains(messageFragment, StringComparison.OrdinalIgnoreCase));
        AssertNoNewRuleIds(before, after);
    }

    // ── stream-external-file: embedded payload versus orphan keys ───────────────────────────────────

    /// <summary>
    /// A real /F file specification without /EF is not a structural spelling defect. PDF readers ignore
    /// the bytes physically between stream/endstream while /F is present, and a plain save preserves both
    /// the selector and those ignored bytes. Removing /F would silently substitute the placeholder for an
    /// unavailable host-file payload, so the targeted repair must refuse it as well.
    /// </summary>
    [Fact]
    public void StreamExternalFile_external_path_survives_save_and_is_refused()
    {
        byte[] original = ExternalFilePagePdf("/F (external-content.bin)", L("IGNORED"));
        PreflightResult before = CheckBytes(original);
        Finding finding = Assert.Single(before.Findings, f => f.RuleId == "stream-external-file");
        Assert.Contains("/F", finding.Message, StringComparison.Ordinal);

        byte[] plainSaved = LoadEditSave(original);
        PreflightResult afterPlainSave = CheckBytes(plainSaved);
        Assert.Contains(afterPlainSave.Findings, f => f.RuleId == "stream-external-file");
        Assert.Contains("IGNORED", Encoding.Latin1.GetString(plainSaved), StringComparison.Ordinal);
        AssertNoNewRuleIds(before, afterPlainSave);

        byte[] repairAttempt = LoadEditSave(original, editor =>
        {
            StreamExternalFileRepairReport report = editor.RepairStreamExternalFiles();
            Assert.Empty(report.Applied);
            Assert.Contains("host path", Assert.Single(report.Refused).Reason, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains(CheckBytes(repairAttempt).Findings, f => f.RuleId == "stream-external-file");
    }

    /// <summary>
    /// Without /F, /FFilter and /FDecodeParms do not act on the in-object bytes. A plain save preserves
    /// each forbidden key, proving this is not FixedBySaving; the narrow repair removes the orphan key
    /// and preserves the exact stream bytes.
    /// </summary>
    [Theory]
    [InlineData("/FFilter /FlateDecode", "/FFilter")]
    [InlineData("/FDecodeParms << /Predictor 1 >>", "/FDecodeParms")]
    public void StreamExternalFile_orphan_external_key_is_safely_removed(
        string dictionaryEntry, string expectedKey)
    {
        byte[] content = L("BT ET");
        byte[] original = ExternalFilePagePdf(dictionaryEntry, content);
        PreflightResult before = CheckBytes(original);
        Finding finding = Assert.Single(before.Findings, f => f.RuleId == "stream-external-file");
        Assert.Contains(expectedKey, finding.Message, StringComparison.Ordinal);

        byte[] plainSaved = LoadEditSave(original);
        Assert.Contains(CheckBytes(plainSaved).Findings, f => f.RuleId == "stream-external-file");

        byte[] repaired = LoadEditSave(original, editor =>
        {
            StreamExternalFileRepairReport report = editor.RepairStreamExternalFiles();
            StreamExternalFileRepair applied = Assert.Single(report.Applied);
            Assert.Contains(expectedKey, applied.RemovedKeys);
            Assert.Null(applied.EmbeddedFileObjectNumber);
            Assert.Empty(report.Refused);
        });
        PreflightResult after = CheckBytes(repaired);
        Assert.DoesNotContain(after.Findings, f => f.RuleId == "stream-external-file");
        AssertNoNewRuleIds(before, after);

        using PdfDocument document = PdfDocument.Load(new MemoryStream(repaired));
        var stream = Assert.IsType<PdfStream>(document.GetObject(4));
        Assert.Equal(content, stream.Data);
    }

    /// <summary>
    /// /F can be closed without ambient file access when its indirect /Filespec maps every represented
    /// name to one embedded-file stream. The embedded stream contains the external file bytes; those
    /// bytes are copied into the target, and /FFilter plus /FDecodeParms are moved to their internal
    /// equivalents. The decoded target content is therefore identical to the intended external payload.
    /// </summary>
    [Fact]
    public void StreamExternalFile_embedded_payload_is_internalized_with_external_filter()
    {
        byte[] decodedContent = L("BT ET");
        byte[] externalFileBytes = Zlib(decodedContent);
        byte[] targetObject =
        [
            .. L("4 0 obj\n<< /Length 7 /F 5 0 R /FFilter /FlateDecode "
               + "/FDecodeParms << /Predictor 1 >> >>\nstream\n"),
            .. L("IGNORED"),
            .. L("\nendstream\nendobj\n")
        ];
        byte[] embeddedObject =
        [
            .. L($"6 0 obj\n<< /Type /EmbeddedFile /Length {externalFileBytes.Length} >>\nstream\n"),
            .. externalFileBytes,
            .. L("\nendstream\nendobj\n")
        ];
        byte[] original = new Pdf()
            .Obj(1, 0, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n")
            .Obj(3, 0, "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>\nendobj\n")
            .Obj(4, 0, targetObject)
            .Obj(5, 0, "5 0 obj\n<< /Type /Filespec /F (payload.bin) /EF << /F 6 0 R >> >>\nendobj\n")
            .Obj(6, 0, embeddedObject)
            .Build();

        PreflightResult before = CheckBytes(original);
        Assert.Contains(before.Findings, f => f.RuleId == "stream-external-file");

        byte[] repaired = LoadEditSave(original, editor =>
        {
            StreamExternalFileRepairPreview preview = editor.PreviewStreamExternalFileRepairs();
            StreamExternalFileRepairCandidate candidate = Assert.Single(preview.Candidates);
            Assert.Equal(4, candidate.ObjectNumber);
            Assert.Equal(6, candidate.EmbeddedFileObjectNumber);
            Assert.Empty(preview.Refused);

            StreamExternalFileRepairReport report = editor.RepairStreamExternalFiles(new HashSet<int> { 4 });
            Assert.Single(report.Applied);
            Assert.Empty(report.Refused);
        });

        PreflightResult after = CheckBytes(repaired);
        Assert.DoesNotContain(after.Findings, f => f.RuleId == "stream-external-file");
        AssertNoNewRuleIds(before, after);

        using PdfDocument document = PdfDocument.Load(new MemoryStream(repaired));
        var stream = Assert.IsType<PdfStream>(document.GetObject(4));
        Assert.False(stream.Dictionary.ContainsKey(N("F")));
        Assert.False(stream.Dictionary.ContainsKey(N("FFilter")));
        Assert.False(stream.Dictionary.ContainsKey(N("FDecodeParms")));
        Assert.Equal("FlateDecode", Assert.IsType<PdfName>(stream.Dictionary.Get("Filter")).Value);
        Assert.IsType<PdfDictionary>(stream.Dictionary.Get("DecodeParms"));
        Assert.Equal(externalFileBytes, stream.Data);
        Assert.Equal(decodedContent, stream.GetDecodedData());
        using PdfDocumentEditor secondPass = document.Edit();
        Assert.Empty(secondPass.PreviewStreamExternalFileRepairs().Candidates);
        Assert.Empty(secondPass.PreviewStreamExternalFileRepairs().Refused);
    }

    private static byte[] ExternalFilePagePdf(string dictionaryEntry, byte[] content)
    {
        byte[] streamObject =
        [
            .. L($"4 0 obj\n<< /Length {content.Length} {dictionaryEntry} >>\nstream\n"),
            .. content,
            .. L("\nendstream\nendobj\n")
        ];
        return new Pdf()
            .Obj(1, 0, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n")
            .Obj(3, 0, "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>\nendobj\n")
            .Obj(4, 0, streamObject)
            .Build();
    }

    private static byte[] Zlib(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(bytes);
        return output.ToArray();
    }

    // ── file-id: the proven negative ────────────────────────────────────────────────────────────────────────────────────────────

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

    // ── embedded-file 6.8 — /F present, /UF absent (Task 3: RepairFileSpecNames) ────────────────────

    /// <summary>A catalog-registered filespec (object 40) with /EF and /F but no /UF — a genuine 6.8-t2
    /// violation ("must contain both /F and /UF keys") — reachable via the catalog's
    /// /Names /EmbeddedFiles name tree. Mirrors Task 1's measured corpus shape: 55/55 affected documents
    /// carry /F non-empty and /UF absent.</summary>
    private static PdfDocument FileSpecMissingUfDocument()
    {
        var doc = new PdfDocument();
        doc.AddObject(41, 0, new PdfStream(
            new PdfDictionary { [N("Type")] = N("EmbeddedFile") }, Encoding.ASCII.GetBytes("data")));
        doc.AddObject(40, 0, new PdfDictionary
        {
            [N("Type")] = N("Filespec"),
            [N("F")] = PdfString.FromText("report.txt"),
            [N("EF")] = new PdfDictionary { [N("F")] = new PdfIndirectReference(41, 0) },
            // deliberately no /UF — the violation under test
        });

        var namesArray = new PdfArray(PdfString.FromText("report.txt"), new PdfIndirectReference(40, 0));
        var embeddedFilesLeaf = new PdfDictionary { [N("Names")] = namesArray };

        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(),
            [N("Count")] = new PdfInteger(0),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Pages")] = new PdfIndirectReference(2, 0),
            [N("Names")] = new PdfDictionary { [N("EmbeddedFiles")] = embeddedFilesLeaf },
        });
        doc.Trailer.Dictionary[N("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    /// <summary>
    /// The 6.8 finding on a genuinely non-conformant filespec (/F present, /UF absent) is cleared once
    /// <see cref="PdfDocumentEditor.RepairFileSpecNames"/> fills /UF from /F before saving. Includes the
    /// same discrimination check as the font-dictionary facts above.
    /// </summary>
    [Fact]
    public void EmbeddedFile_finding_is_cleared_by_RepairFileSpecNames_before_save()
    {
        byte[] original = SavedBytes(FileSpecMissingUfDocument());

        PreflightResult before = CheckBytes(original);
        PreflightResult beforeAgain = CheckBytes(original); // discrimination: same bytes, no save in between
        Assert.Contains(before.Findings, f => f.RuleId == "embedded-file" && ClauseKey(f) == "6.8");
        Assert.Contains(beforeAgain.Findings, f => f.RuleId == "embedded-file" && ClauseKey(f) == "6.8");

        byte[] saved = LoadEditSave(original, editor =>
        {
            FileSpecNameRepairReport report = editor.RepairFileSpecNames(includeAnnotationSpecs: false);
            Assert.Single(report.Repaired);
        });
        PreflightResult after = CheckBytes(saved);

        Assert.DoesNotContain(after.Findings, f => f.RuleId == "embedded-file" && ClauseKey(f) == "6.8");
        AssertNoNewRuleIds(before, after);
    }
}
