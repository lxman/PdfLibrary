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
/// PDF/A-2/3 clause 6.1.7.1 (<see cref="StreamObjectRule"/>), calibrated byte-for-byte against veraPDF's
/// CosStream parser. Test 1: the /Length value equals the actual stream length (the bytes after the LF
/// following <c>stream</c> and before the EOL preceding <c>endstream</c>, with veraPDF's CRLF
/// disambiguation). Test 2: <c>stream</c> is followed by CRLF or a single LF, and <c>endstream</c> is
/// immediately preceded by an EOL marker. (Test 3 — F/FFilter/FDecodeParms — is StreamExternalFileRule's.)
/// A byte-level rule: it re-reads the source bytes at each stream's cross-reference offset.
/// </summary>
public class StreamObjectRuleTests
{
    private static Finding[] Run(byte[] pdf)
    {
        using var doc = PdfDocument.Load(new MemoryStream(pdf, writable: false), string.Empty);
        var ctx = new ConformanceContext(doc, ConformanceProfile.PdfA2b, pdf);
        return [.. new StreamObjectRule().Check(ctx)];
    }

    /// <summary>Builds a byte-exact PDF with a valid classic xref whose offsets point at each object number.</summary>
    private sealed class Pdf
    {
        private readonly List<byte> _buf = [];
        private readonly List<(int num, int gen, long off)> _xref = [];

        public Pdf()
        {
            Ascii("%PDF-1.7\n");
            _buf.Add((byte)'%');
            _buf.AddRange([0xE2, 0xE3, 0xCF, 0xD3]);
            _buf.Add((byte)'\n');
        }

        public Pdf Obj(int num, int gen, byte[] rawText)
        {
            _xref.Add((num, gen, _buf.Count));
            _buf.AddRange(rawText);
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

    private static byte[] L(string s) => Encoding.Latin1.GetBytes(s);

    /// <summary>An indirect stream object (number 4): dictionary with the given declared /Length, then the
    /// <c>stream</c> keyword, the exact bytes after it, the data, the exact bytes before <c>endstream</c>.</summary>
    private static byte[] StreamObj(int declaredLength, string afterStream, string data, string beforeEndstream)
        => L($"4 0 obj\n<< /Length {declaredLength} >>\nstream{afterStream}{data}{beforeEndstream}endstream\nendobj\n");

    /// <summary>An indirect stream object with a fully-specified dictionary body (so its lexical content —
    /// strings, comments, nested dicts — can be exercised) and correct LF framing.</summary>
    private static byte[] StreamObjWithDict(string dictBody, string data)
        => L($"4 0 obj\n{dictBody}\nstream\n{data}\nendstream\nendobj\n");

    private static byte[] PdfWithStream(byte[] streamObj) => new Pdf()
        .Obj(1, 0, L("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"))
        .Obj(2, 0, L("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"))
        .Obj(3, 0, L("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n"))
        .Obj(4, 0, streamObj)
        .Build();

    private static void AssertFlagged(byte[] pdf)
    {
        Finding f = Assert.Single(Run(pdf));
        Assert.Equal("stream-object", f.RuleId);
        Assert.EndsWith("6.1.7.1", f.Clause);
        Assert.Equal(FindingSeverity.Error, f.Severity);
        Assert.Equal(4, f.ObjectNumber);
    }

    // ── conformant framing ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void A_well_formed_stream_with_crlf_framing_passes()
    {
        Assert.Empty(Run(PdfWithStream(StreamObj(5, "\r\n", "hello", "\r\n"))));
    }

    [Fact]
    public void A_well_formed_stream_with_lf_framing_passes()
    {
        Assert.Empty(Run(PdfWithStream(StreamObj(5, "\n", "hello", "\n"))));
    }

    [Fact]
    public void The_crlf_before_endstream_is_disambiguated_by_length()
    {
        // Data's last byte is CR, then a lone LF is the EOL before endstream. /Length counts the CR as data,
        // so realLength must strip only 1 byte (the LF), not treat CR+LF as a 2-byte marker.
        Assert.Empty(Run(PdfWithStream(StreamObj(4, "\r\n", "abc\r", "\n"))));
    }

    // ── test 1: /Length != realLength ───────────────────────────────────────────────────────────────
    [Fact]
    public void A_length_shorter_than_the_data_is_flagged()
    {
        AssertFlagged(PdfWithStream(StreamObj(3, "\r\n", "hello!", "\r\n"))); // 6 data bytes, /Length 3
    }

    [Fact]
    public void A_length_longer_than_the_data_is_flagged()
    {
        AssertFlagged(PdfWithStream(StreamObj(100, "\r\n", "hello!", "\r\n"))); // 6 data bytes, /Length 100
    }

    // ── test 2: stream-keyword framing ──────────────────────────────────────────────────────────────
    [Fact]
    public void Stream_followed_by_a_lone_cr_is_flagged()
    {
        AssertFlagged(PdfWithStream(StreamObj(5, "\r", "hello", "\r\n")));
    }

    [Fact]
    public void Stream_followed_by_a_space_is_flagged()
    {
        AssertFlagged(PdfWithStream(StreamObj(5, " \n", "hello", "\r\n")));
    }

    // ── test 2: endstream-keyword framing ───────────────────────────────────────────────────────────
    [Fact]
    public void Endstream_preceded_by_a_space_is_flagged()
    {
        AssertFlagged(PdfWithStream(StreamObj(6, "\r\n", "hello", " ")));
    }

    [Fact]
    public void A_conformant_content_stream_object_is_not_flagged()
    {
        // A larger, binary-ish payload with correct framing and length — the common valid case.
        string data = new string('x', 200);
        Assert.Empty(Run(PdfWithStream(StreamObj(200, "\r\n", data, "\r\n"))));
    }

    // ── FP-safety: raw scanning must respect PDF lexical structure (strings, comments, nested dicts) ─────
    [Fact]
    public void A_string_value_containing_the_stream_keyword_does_not_false_positive()
    {
        // The /Title string contains ">> stream", which a naive ">>-then-stream" anchor would lock onto.
        Assert.Empty(Run(PdfWithStream(StreamObjWithDict("<< /Title (foo >> stream bar) /Length 5 >>", "hello"))));
    }

    [Fact]
    public void A_string_value_containing_a_length_key_does_not_false_positive()
    {
        // The /Title string contains "/Length 999"; the real /Length is 5.
        Assert.Empty(Run(PdfWithStream(StreamObjWithDict("<< /Title (x /Length 999 y) /Length 5 >>", "hello"))));
    }

    [Fact]
    public void A_nested_dictionary_length_is_not_taken_for_the_stream_length()
    {
        // A nested dictionary's /Length (3) precedes the stream dictionary's own /Length (5).
        Assert.Empty(Run(PdfWithStream(StreamObjWithDict("<< /DecodeParms << /Length 3 >> /Length 5 >>", "hello"))));
    }

    [Fact]
    public void An_in_memory_document_without_source_bytes_is_not_checked()
    {
        using var doc = PdfDocument.Load(new MemoryStream(PdfWithStream(StreamObj(3, "\r\n", "hello!", "\r\n")), false), string.Empty);
        var ctx = new ConformanceContext(doc, ConformanceProfile.PdfA2b, sourceBytes: null);
        Assert.Empty(new StreamObjectRule().Check(ctx));
    }
}
