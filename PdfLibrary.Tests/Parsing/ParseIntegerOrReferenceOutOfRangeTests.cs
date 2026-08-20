using System.IO;
using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Parsing;
using Xunit;

namespace PdfLibrary.Tests.Parsing;

/// <summary>
/// Root-cause found by a real-corpus scan of the ISO 19005-2 6.1.13 test 1 fixtures: an integer
/// literal too large for Int32, sitting in the "could this be an indirect reference?" lookahead
/// position, was silently dropped rather than becoming a <see cref="PdfInteger"/>.
///
/// <para><see cref="PdfParser"/>'s object-lookahead for <c>N G R</c> / <c>N G obj</c> reads a second
/// integer token as a candidate generation number. When that candidate is a well-formed integer that
/// does not fit in <c>int</c> (e.g. <c>2157483648</c>), <c>int.TryParse</c> fails and the code
/// returned early WITHOUT pushing the consumed token back — the token, and the integer it represented,
/// vanished. This made ISO 19005-2 6.1.13 test 1 ("no integer outside ±2147483647") undetectable
/// anywhere the violating literal sat in this lookahead position — which is every top-level array/
/// dictionary-value position, since every integer is provisionally read as a possible object number.</para>
/// </summary>
public class ParseIntegerOrReferenceOutOfRangeTests
{
    private static PdfArray ParseArray(string content)
    {
        var parser = new PdfParser(new MemoryStream(Encoding.ASCII.GetBytes(content)));
        return Assert.IsType<PdfArray>(parser.ReadObject());
    }

    [Fact]
    public void An_out_of_range_integer_after_a_candidate_object_number_is_not_dropped()
    {
        PdfArray array = ParseArray("[0 0 2157483648]");

        Assert.Equal(3, array.Count);
        Assert.IsType<PdfInteger>(array[0]);
        Assert.Equal(0L, Assert.IsType<PdfInteger>(array[0]).LongValue);
        Assert.IsType<PdfInteger>(array[1]);
        Assert.Equal(0L, Assert.IsType<PdfInteger>(array[1]).LongValue);
        var third = Assert.IsType<PdfInteger>(array[2]);
        Assert.Equal(2157483648L, third.LongValue);
    }

    [Fact]
    public void A_negative_out_of_range_integer_after_a_candidate_object_number_is_not_dropped()
    {
        // fail-a's shape: -2157483648 in a /Widths array, e.g. "[100 -2157483648]".
        PdfArray array = ParseArray("[100 -2157483648]");

        Assert.Equal(2, array.Count);
        var second = Assert.IsType<PdfInteger>(array[1]);
        Assert.Equal(-2157483648L, second.LongValue);
    }

    /// <summary>Fix-round-1 regression: a sign-only token ("-") in the same lookahead position is
    /// NOT a well-formed integer — <c>long.TryParse</c> fails on it too — so pushing it back would
    /// re-enter <see cref="PdfParser.ParseIntegerOrReference"/>, fail <c>long.TryParse</c> at the top,
    /// and THROW <see cref="PdfParseException"/>. Before the fix in this file's target method existed
    /// at all, a bare "-" here was silently dropped and the file still loaded (with the array one
    /// element short). The fix must keep that pre-existing (wrong but non-fatal) behaviour rather
    /// than newly throwing on a malformed real-world producer's file.</summary>
    [Fact]
    public void A_sign_only_token_between_integers_does_not_throw()
    {
        PdfArray array = ParseArray("[500 - 500]");

        // The malformed "-" is dropped (pre-existing behaviour, unchanged) rather than becoming a
        // third element or crashing the load.
        Assert.Equal(2, array.Count);
        Assert.Equal(500L, Assert.IsType<PdfInteger>(array[0]).LongValue);
        Assert.Equal(500L, Assert.IsType<PdfInteger>(array[1]).LongValue);
    }
}
