using System;
using System.IO;
using System.Linq;
using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Parsing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Parsing;

/// <summary>
/// Issue 57: a hex string must be written back as a hex string.
///
/// <para><c>PdfParser</c> built every parsed string with the <c>PdfString</c> constructor's DEFAULT
/// format (Literal), so <c>ToPdfString()</c> — which switches on that format — emitted octal escapes
/// for strings the source wrote as <c>&lt;…&gt;</c>. `/ID [&lt;9020EE64…&gt;]` came back as
/// `[(\220 \356d\276…)]`: legal per ISO 32000-1 7.3.4.2, but a gratuitous rewrite of bytes nobody
/// edited, at 4 characters per non-printable byte instead of 2. Unintentional rather than a decision —
/// <c>PdfStringFormat.Hexadecimal</c> exists, the writer switches on it, and <c>PdfString.FromText</c>
/// sets it deliberately for UTF-16.</para>
///
/// <para>The fix adds <c>PdfTokenType.HexString</c>. That makes the lexer's output a breaking change
/// for every consumer switching on <c>PdfTokenType.String</c> — notably <c>PdfContentParser</c>, whose
/// dictionary sites are switch EXPRESSIONS with a <c>_ =&gt; null</c> fallback that would have dropped
/// hex operands SILENTLY rather than throwing. The content-stream tests below exist for that.</para>
/// </summary>
public class HexStringRoundTripTests
{
    private const string BinaryId = "9020EE64BE189242A019BF8AA166A8E4";

    /// <summary>A minimal document whose trailer /ID holds binary hex strings and whose Info /Title is
    /// hex-encoded UTF-16BE — the three shapes hex exists for.</summary>
    private static byte[] DocWithHexStrings()
    {
        var sb = new StringBuilder();
        sb.Append("%PDF-1.7\n");
        var offsets = new int[5];
        void Obj(int n, string body)
        {
            offsets[n] = Encoding.Latin1.GetByteCount(sb.ToString());
            sb.Append(n).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }
        Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        Obj(4, "<< /Title <FEFF00480065006C006C006F> >>");
        int xref = Encoding.Latin1.GetByteCount(sb.ToString());
        sb.Append("xref\n0 5\n0000000000 65535 f \n");
        for (var i = 1; i <= 4; i++) sb.Append(offsets[i].ToString("D10")).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size 5 /Root 1 0 R /Info 4 0 R /ID [<").Append(BinaryId).Append("> <")
          .Append(BinaryId).Append(">] >>\nstartxref\n").Append(xref).Append("\n%%EOF");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private static string SaveAndRead(byte[] original)
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(original, writable: false), string.Empty);
        var editor = new PdfDocumentEditor(doc);
        using var saved = new MemoryStream();
        editor.Save(saved);
        return Encoding.Latin1.GetString(saved.ToArray());
    }

    [Fact]
    public void A_binary_hex_string_is_written_back_as_hex()
    {
        string after = SaveAndRead(DocWithHexStrings());

        Assert.Contains("<" + BinaryId + ">", after, StringComparison.OrdinalIgnoreCase);
        // And specifically NOT the octal-escaped literal it used to become.
        Assert.DoesNotContain(@"\220 \356", after, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hex_encoded_utf16_title_is_written_back_as_hex()
    {
        string after = SaveAndRead(DocWithHexStrings());

        Assert.Contains("FEFF00480065006C006C006F", after, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\376\377", after, StringComparison.Ordinal);
    }

    [Fact]
    public void A_literal_string_is_still_written_back_as_a_literal()
    {
        // The other half of the contract — the fix must not turn every string into hex.
        byte[] original = Encoding.Latin1.GetBytes(
            Encoding.Latin1.GetString(DocWithHexStrings())
                .Replace("<FEFF00480065006C006C006F>", "(Hello)", StringComparison.Ordinal));

        string after = SaveAndRead(original);
        Assert.Contains("(Hello)", after, StringComparison.Ordinal);
    }

    [Fact]
    public void The_lexer_reports_hex_and_literal_as_distinct_token_types()
    {
        var lexer = new PdfLexer(new MemoryStream(Encoding.Latin1.GetBytes("<48656C6C6F> (Hello)")));

        PdfToken hex = lexer.NextToken();
        PdfToken literal = lexer.NextToken();

        Assert.Equal(PdfTokenType.HexString, hex.Type);
        Assert.Equal(PdfTokenType.String, literal.Type);
        // Same decoded bytes: the types differ only in how the source WROTE them.
        Assert.Equal(hex.Value, literal.Value);
    }

    // ── content streams: the silent-drop risk ────────────────────────────────
    //
    // PdfContentParser's dictionary sites are switch EXPRESSIONS with `_ => null`. A missing HexString
    // arm would not throw — it would yield a null operand, so `<0041> Tj` would draw nothing and an
    // inline-image key would go missing, with no error anywhere. These pin that it does not.

    [Fact]
    public void A_hex_string_operand_survives_content_parsing()
    {
        var ops = PdfContentParser.Parse(Encoding.Latin1.GetBytes("BT <48656C6C6F> Tj ET")).ToList();

        PdfOperator tj = Assert.Single(ops, o => o.Name == "Tj");
        var operand = Assert.IsType<PdfString>(Assert.Single(tj.Operands));
        Assert.Equal("Hello", operand.Value);
    }

    [Fact]
    public void A_hex_string_inside_a_TJ_array_survives_content_parsing()
    {
        var ops = PdfContentParser.Parse(
            Encoding.Latin1.GetBytes("BT [<48656C6C6F> -200 (World)] TJ ET")).ToList();

        PdfOperator tj = Assert.Single(ops, o => o.Name == "TJ");
        var array = Assert.IsType<PdfArray>(Assert.Single(tj.Operands));
        Assert.Equal(3, array.Count);
        Assert.Equal("Hello", Assert.IsType<PdfString>(array[0]).Value);
        Assert.Equal("World", Assert.IsType<PdfString>(array[2]).Value);
    }
}
