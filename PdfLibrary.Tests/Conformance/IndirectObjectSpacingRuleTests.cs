using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// PDF/A-2/3 clause 6.1.9 (<see cref="IndirectObjectSpacingRule"/>): the whitespace around every indirect
/// object definition is constrained — object number and generation number separated by a single
/// white-space; generation number and <c>obj</c> separated by a single white-space; the object number and
/// <c>endobj</c> each immediately preceded by an EOL marker; <c>obj</c> and <c>endobj</c> each immediately
/// followed by an EOL marker. Calibrated against veraPDF's PDFA-2 rule (object <c>CosIndirect</c>, test 1:
/// <c>spacingCompliesPDFA</c>). A byte-level rule: it re-reads the source bytes at each object's xref
/// offset, so the tests load real PDF bytes (with a correct classic cross-reference table) rather than an
/// in-memory document.
/// </summary>
public class IndirectObjectSpacingRuleTests
{
    private static Finding[] Run(byte[] pdf)
    {
        using var doc = PdfDocument.Load(new MemoryStream(pdf, writable: false), string.Empty);
        var ctx = new ConformanceContext(doc, ConformanceProfile.PdfA2b, pdf);
        return [.. new IndirectObjectSpacingRule().Check(ctx)];
    }

    /// <summary>
    /// Builds a byte-exact PDF with a valid classic xref table whose offsets point at each object number,
    /// so a byte-level rule sees the real spacing. Object bodies are supplied as raw text that must begin at
    /// the object-number digit and end at the final byte of <c>endobj</c> (plus any trailing EOL).
    /// </summary>
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

        /// <summary>Appends arbitrary inter-object bytes (glue) without recording an object.</summary>
        public Pdf Glue(string ascii) { Ascii(ascii); return this; }

        /// <summary>Records an object at the current offset (the offset points at <paramref name="rawText"/>'s
        /// first byte, which must be the object-number digit) and appends its raw text.</summary>
        public Pdf Obj(int num, int gen, string rawText)
        {
            _xref.Add((num, gen, _buf.Count));
            Ascii(rawText);
            return this;
        }

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

    // A conforming three-object document: catalog, pages, and a trailing dummy. Every object number is
    // preceded by the previous object's endobj EOL, single-spaced, and each keyword is EOL-bounded.
    private static Pdf Conforming(string obj3) => new Pdf()
        .Obj(1, 0, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
        .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n")
        .Obj(3, 0, obj3);

    private const string GoodObj3 = "3 0 obj\n<< /Type /Foo >>\nendobj\n";

    [Fact]
    public void A_conforming_document_has_no_findings()
    {
        Assert.Empty(Run(Conforming(GoodObj3).Build()));
    }

    [Fact]
    public void Crlf_line_endings_conform()
    {
        byte[] pdf = new Pdf()
            .Obj(1, 0, "1 0 obj\r\n<< /Type /Catalog /Pages 2 0 R >>\r\nendobj\r\n")
            .Obj(2, 0, "2 0 obj\r\n<< /Type /Pages /Kids [] /Count 0 >>\r\nendobj\r\n")
            .Obj(3, 0, "3 0 obj\r\n<< /Type /Foo >>\r\nendobj\r\n")
            .Build();
        Assert.Empty(Run(pdf));
    }

    [Fact]
    public void A_multidigit_object_number_with_nonzero_generation_conforms()
    {
        // Objects 3..12 are free; object 13 exercises multi-digit parsing and a non-zero generation.
        byte[] pdf = new Pdf()
            .Obj(1, 0, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n")
            .Obj(13, 4, "13 4 obj\n<< /Type /Foo >>\nendobj\n")
            .Build();
        Assert.Empty(Run(pdf));
    }

    private static void AssertFlagsObject3(byte[] pdf)
    {
        Finding f = Assert.Single(Run(pdf));
        Assert.Equal("indirect-object-spacing", f.RuleId);
        Assert.Equal(ConformanceClauses.For(ConformanceProfile.PdfA2b, "6.1.9"), f.Clause);
        Assert.Equal(FindingSeverity.Error, f.Severity);
        Assert.Equal(3, f.ObjectNumber);
    }

    [Fact]
    public void Two_spaces_between_object_and_generation_number_is_flagged()
    {
        AssertFlagsObject3(Conforming("3  0 obj\n<< /Type /Foo >>\nendobj\n").Build());
    }

    [Fact]
    public void Extra_space_between_generation_number_and_obj_keyword_is_flagged()
    {
        AssertFlagsObject3(Conforming("3 0   obj\n<< /Type /Foo >>\nendobj\n").Build());
    }

    [Fact]
    public void An_object_number_not_immediately_preceded_by_an_eol_is_flagged()
    {
        // Object 2 ends with endobj+EOL; three spaces of glue then push object 3's number off the EOL.
        byte[] pdf = new Pdf()
            .Obj(1, 0, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n")
            .Glue("   ")
            .Obj(3, 0, GoodObj3)
            .Build();
        AssertFlagsObject3(pdf);
    }

    [Fact]
    public void An_obj_keyword_not_immediately_followed_by_an_eol_is_flagged()
    {
        AssertFlagsObject3(Conforming("3 0 obj << /Type /Foo >>\nendobj\n").Build());
    }

    [Fact]
    public void An_endobj_keyword_not_immediately_preceded_by_an_eol_is_flagged()
    {
        AssertFlagsObject3(Conforming("3 0 obj\n<< /Type /Foo >> endobj\n").Build());
    }

    [Fact]
    public void An_endobj_keyword_not_immediately_followed_by_an_eol_is_flagged()
    {
        AssertFlagsObject3(Conforming("3 0 obj\n<< /Type /Foo >>\nendobj  \n").Build());
    }

    [Fact]
    public void An_inter_object_comment_containing_endobj_does_not_false_positive()
    {
        // Object 1 is conformant. A legal comment before object 2 also contains the bytes 'endobj', framed
        // by spaces. A naive "last endobj in the region" search would pick the comment's and flag object 1.
        byte[] pdf = new Pdf()
            .Obj(1, 0, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Glue("% trailing note endobj here\n")
            .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n")
            .Obj(3, 0, GoodObj3)
            .Build();
        Assert.Empty(Run(pdf));
    }

    [Fact]
    public void A_superseded_object_copy_in_the_region_does_not_false_positive()
    {
        // Object 2 is conformant. A superseded copy left in the gap by an "incremental update" (not in the
        // xref, so absent from XrefTable.Entries) carries a tightly-framed '>>endobj'. It must not be
        // mistaken for object 2's own endobj.
        byte[] pdf = new Pdf()
            .Obj(1, 0, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
            .Obj(2, 0, "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n")
            .Glue("9 0 obj\n<< /Stale true >>>>endobj\n")
            .Obj(3, 0, GoodObj3)
            .Build();
        Assert.Empty(Run(pdf));
    }

    [Fact]
    public void An_in_memory_document_without_source_bytes_is_not_checked()
    {
        using var doc = PdfDocument.Load(new MemoryStream(Conforming("3  0 obj\n<< /Type /Foo >>\nendobj\n").Build(), writable: false), string.Empty);
        var ctx = new ConformanceContext(doc, ConformanceProfile.PdfA2b, sourceBytes: null);
        Assert.Empty(new IndirectObjectSpacingRule().Check(ctx));
    }
}
