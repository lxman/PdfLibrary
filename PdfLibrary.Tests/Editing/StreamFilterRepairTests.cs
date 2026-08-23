using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// Tests for <see cref="PdfDocumentEditor.PreviewStreamFilterRepairs"/> -- the read-only preview of
/// PDF/A clause 6.1.7.2 stream-filter defects (<c>PdfLibrary.Conformance.Rules.StreamFiltersRule</c>).
/// </summary>
public class StreamFilterRepairTests
{
    /// <summary>Builds a bare, unwired document (no catalog/page tree -- neither is needed here, since
    /// the classifier walks <c>_document.Objects.Values</c> directly and nothing in this file ever
    /// saves or reloads) whose single indirect stream carries <paramref name="filter"/> over
    /// <paramref name="payload"/>, encoded so the stream genuinely decodes. Mirrors
    /// <c>PdfDocumentEditorEmbedProgramTests.UnembeddedWithWidths</c>'s convention: a bare
    /// <c>new PdfDocument()</c> plus <c>RegisterObject</c> (which allocates the object number), and the
    /// editor constructed directly via <c>new PdfDocumentEditor(document)</c> rather than
    /// <c>document.Edit()</c> -- also an established idiom in this test project
    /// (<c>PdfDocumentEditorEmbedProgramTests</c>, <c>EmbedProgramRoundTripTests</c>,
    /// <c>HexStringRoundTripTests</c>). Returns the editor plus that stream's object number.</summary>
    private static (PdfDocumentEditor Editor, int ObjectNumber) OneStream(
        string filter, byte[] payload, PdfObject? decodeParms = null)
    {
        var doc = new PdfDocument();
        var stream = new PdfStream(new PdfDictionary(), []);
        stream.SetEncodedData(payload, filter);
        if (decodeParms is not null) stream.Dictionary[PdfName.DecodeParms] = decodeParms;
        PdfIndirectReference streamRef = doc.RegisterObject(stream);
        return (new PdfDocumentEditor(doc), streamRef.ObjectNumber);
    }

    [Fact]
    public void Preview_reports_an_LZW_stream_as_a_candidate()
    {
        byte[] payload = "compress me"u8.ToArray();
        (PdfDocumentEditor editor, int n) = OneStream("LZWDecode", payload);

        StreamFilterRepairPreview preview = editor.PreviewStreamFilterRepairs();

        StreamFilterRepairCandidate candidate = Assert.Single(preview.Candidates);
        Assert.Equal(n, candidate.ObjectNumber);
        Assert.Equal(["LZWDecode"], candidate.FilterChain);
        Assert.Empty(preview.Refused);
    }

    [Fact]
    public void Preview_ignores_a_stream_whose_filters_are_all_permitted()
    {
        (PdfDocumentEditor editor, _) = OneStream("FlateDecode", "already fine"u8.ToArray());

        StreamFilterRepairPreview preview = editor.PreviewStreamFilterRepairs();

        Assert.Empty(preview.Candidates);
        Assert.Empty(preview.Refused);
    }

    [Fact]
    public void Preview_ignores_a_stream_with_no_filter_at_all()
    {
        var doc = new PdfDocument();
        var stream = new PdfStream(new PdfDictionary(), "raw"u8.ToArray());
        doc.RegisterObject(stream);

        StreamFilterRepairPreview preview = new PdfDocumentEditor(doc).PreviewStreamFilterRepairs();

        Assert.Empty(preview.Candidates);
        Assert.Empty(preview.Refused);
    }

    // A Crypt filter whose parms Name is Identity is PERMITTED by StreamFiltersRule, so the classifier
    // must not refuse it -- refusing here would report a defect the preflighter never raised.
    [Fact]
    public void Preview_treats_an_Identity_Crypt_filter_as_permitted()
    {
        var doc = new PdfDocument();
        var stream = new PdfStream(new PdfDictionary(), "x"u8.ToArray());
        stream.Dictionary[PdfName.Filter] = new PdfName("Crypt");
        var parms = new PdfDictionary();
        parms[new PdfName("Name")] = new PdfName("Identity");
        stream.Dictionary[PdfName.DecodeParms] = parms;
        doc.RegisterObject(stream);

        StreamFilterRepairPreview preview = new PdfDocumentEditor(doc).PreviewStreamFilterRepairs();

        Assert.Empty(preview.Candidates);
        Assert.Empty(preview.Refused);
    }

    [Fact]
    public void Preview_refuses_a_disallowed_filter_that_is_not_LZW()
    {
        var doc = new PdfDocument();
        var stream = new PdfStream(new PdfDictionary(), "x"u8.ToArray());
        stream.Dictionary[PdfName.Filter] = new PdfName("Crypt");
        var parms = new PdfDictionary();
        parms[new PdfName("Name")] = new PdfName("StdCF");   // NOT Identity -> a real violation
        stream.Dictionary[PdfName.DecodeParms] = parms;
        PdfIndirectReference streamRef = doc.RegisterObject(stream);
        int n = streamRef.ObjectNumber;

        StreamFilterRepairPreview preview = new PdfDocumentEditor(doc).PreviewStreamFilterRepairs();

        Assert.Empty(preview.Candidates);
        StreamFilterRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(n, refusal.ObjectNumber);
        Assert.Contains("Crypt", refusal.Reason, StringComparison.Ordinal);
    }

    // Regression for the fix-round-1 Critical: FilterChainOf (a display-only helper) COMPACTS a
    // /Filter array when it skips a malformed (non-name) entry, so a position taken from its output no
    // longer matches the true /Filter array index. StreamFiltersRule.Check indexes /DecodeParms by the
    // TRUE array position, never a compacted one. Here /Filter is [<malformed> /Crypt] -- /Crypt sits
    // at true position 1, not 0 -- and /DecodeParms is an array whose slot 1 (aligned with /Crypt) is
    // NOT Identity. A classifier that reads DecodeParms[0] (the compacted index) instead of
    // DecodeParms[1] (the true index) sees an empty dictionary, misreads it as Identity, and reports
    // neither a candidate nor a refusal -- exactly the "reads as nothing wrong" hole the brief named.
    [Fact]
    public void Preview_refuses_Crypt_when_DecodeParms_is_aligned_by_true_array_position()
    {
        var doc = new PdfDocument();
        var stream = new PdfStream(new PdfDictionary(), "x"u8.ToArray());
        stream.Dictionary[PdfName.Filter] = new PdfArray(new PdfInteger(0), new PdfName("Crypt"));
        var slot0 = new PdfDictionary();                                                  // malformed entry's slot -- must never be read
        var slot1 = new PdfDictionary { [new PdfName("Name")] = new PdfName("StdCF") };   // /Crypt's true slot -- NOT Identity
        stream.Dictionary[PdfName.DecodeParms] = new PdfArray(slot0, slot1);
        PdfIndirectReference streamRef = doc.RegisterObject(stream);
        int n = streamRef.ObjectNumber;

        StreamFilterRepairPreview preview = new PdfDocumentEditor(doc).PreviewStreamFilterRepairs();

        Assert.Empty(preview.Candidates);
        StreamFilterRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(n, refusal.ObjectNumber);
        Assert.Contains("Crypt", refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_refuses_an_LZW_stream_whose_data_will_not_decode()
    {
        var doc = new PdfDocument();
        // Deliberately not LZW-encoded data under an LZWDecode filter.
        var stream = new PdfStream(new PdfDictionary(), new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
        stream.Dictionary[PdfName.Filter] = new PdfName("LZWDecode");
        PdfIndirectReference streamRef = doc.RegisterObject(stream);
        int n = streamRef.ObjectNumber;

        StreamFilterRepairPreview preview = new PdfDocumentEditor(doc).PreviewStreamFilterRepairs();

        Assert.Empty(preview.Candidates);
        StreamFilterRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(n, refusal.ObjectNumber);
    }

    // Preview must be genuinely read-only -- calling it twice returns the same answer and mutates
    // nothing. This is the property that lets the domain's Propose call it safely.
    [Fact]
    public void Preview_is_read_only_and_repeatable()
    {
        (PdfDocumentEditor editor, int n) = OneStream("LZWDecode", "twice"u8.ToArray());

        StreamFilterRepairPreview first = editor.PreviewStreamFilterRepairs();
        StreamFilterRepairPreview second = editor.PreviewStreamFilterRepairs();

        Assert.Equal(n, Assert.Single(first.Candidates).ObjectNumber);
        Assert.Equal(n, Assert.Single(second.Candidates).ObjectNumber);
    }
}
