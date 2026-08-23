using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Filters;
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

    // ---- Task 3: RepairStreamFilters (the write side) -------------------------------------------

    /// <summary>Resolves the live <see cref="PdfStream"/> for <paramref name="objectNumber"/> off the
    /// document the editor wraps, so a test can inspect a stream's dictionary/data after a repair has
    /// run. <see cref="PdfDocumentEditor.Document"/> is public; mirrors how other Editing tests read a
    /// mutated object back by number (e.g. <c>PdfDocumentEditorFontsTests</c>,
    /// <c>FileSpecNameRepairTests</c>): a plain cast off <c>document.Objects[n]</c>.</summary>
    private static PdfStream FindStream(PdfDocumentEditor editor, int objectNumber) =>
        (PdfStream)editor.Document.Objects[objectNumber];

    /// <summary>Encodes bytes in ASCIIHexDecode's wire format, for the chain test's synthetic
    /// [/ASCIIHexDecode /LZWDecode] fixture. Per the controller ruling for this task: ASCIIHexDecode,
    /// not ASCII85Decode -- <c>AsciiHexDecodeFilter.Encode</c> is verified implemented (exercised
    /// directly by <c>StreamFilterTests</c>), while <c>Ascii85DecodeFilter.Encode</c> is not. Delegates
    /// to the real filter so the fixture is built with the same code a reader would use to decode it.</summary>
    private static byte[] AsciiHexEncode(byte[] data) => new AsciiHexDecodeFilter().Encode(data);

    [Fact]
    public void Repair_converts_an_LZW_stream_to_Flate()
    {
        byte[] payload = "convert me to flate"u8.ToArray();
        (PdfDocumentEditor editor, int n) = OneStream("LZWDecode", payload);

        StreamFilterRepairReport report = editor.RepairStreamFilters(new HashSet<int> { n });

        StreamFilterRepair applied = Assert.Single(report.Applied);
        Assert.Equal(n, applied.ObjectNumber);
        Assert.Equal(["LZWDecode"], applied.Before);
        Assert.Equal("FlateDecode", applied.After);
        Assert.Empty(report.Refused);
    }

    // THE load-bearing test of this whole program. The justification for making this repair automatic
    // rather than a user choice is that it cannot change the rendered page -- so that claim is proven
    // here byte for byte, not asserted in prose.
    //
    // Random-but-seeded rather than long runs of a repeated character: a repetitive payload keeps the
    // LZW dictionary far from the 511-entry code-width boundary (see the /EarlyChange note on
    // Repair_does_not_leave_the_LZW_DecodeParms_behind below, where that boundary is exactly what made
    // a mismatched decode parameter throw). A codec bug that only shows on high-entropy data is exactly
    // what a run-length-friendly payload would hide.
    [Fact]
    public void Repair_is_lossless_the_decoded_bytes_are_unchanged()
    {
        var payload = new byte[10001];
        new Random(20260823).NextBytes(payload);
        (PdfDocumentEditor editor, int n) = OneStream("LZWDecode", payload);

        editor.RepairStreamFilters(new HashSet<int> { n });

        PdfStream repaired = FindStream(editor, n);
        Assert.Equal(payload, repaired.GetDecodedData());
        Assert.Equal("FlateDecode", Assert.IsType<PdfName>(repaired.Dictionary.Get("Filter")).Value);
    }

    // Task 1's fix, observed from this feature's side: an LZW stream's /DecodeParms must not survive
    // onto the Flate stream, where /EarlyChange would be re-read as a predictor parameter.
    //
    // NOTE on the fixture: the brief's draft set /EarlyChange 0 here. OneStream builds the stream via
    // PdfStream.SetEncodedData, whose LZWDecode encoder is LzwDecodeFilter.Encode -> Lzw.Compress(data)
    // -> LzwOptions.PdfDefault, i.e. EarlyChange = true. LzwDecodeFilter.Decode DOES honour an
    // /EarlyChange decode parameter, so a fixture whose bytes were encoded with EarlyChange=true but
    // whose /DecodeParms claims EarlyChange=0 asks the decoder to use a code-width schedule the encoder
    // never used. That mismatch is confirmed real: decoding 20KB of high-entropy bytes encoded at
    // EarlyChange=true, with a claimed EarlyChange=0, throws ("Invalid LZW code: 542. Expected code <=
    // 512") once the dictionary crosses the 511-entry width boundary -- verified with a throwaway probe
    // during this task, not asserted from reading the source alone. It does NOT manifest on this test's
    // own 14-byte payload (too short to reach that boundary either way), so the brief's literal 0 would
    // have passed here by accident, not by correctness. Using EarlyChange=1 instead matches the actual
    // encoding unconditionally, so the fixture is correct regardless of payload size, and the test
    // isolates exactly the one thing it claims to prove: the key is gone afterwards -- not that some
    // particular (here, harmless-by-luck) mismatched value survives being ignored.
    [Fact]
    public void Repair_does_not_leave_the_LZW_DecodeParms_behind()
    {
        byte[] payload = "parms must go"u8.ToArray();
        var parms = new PdfDictionary();
        parms[new PdfName("EarlyChange")] = new PdfInteger(1);
        (PdfDocumentEditor editor, int n) = OneStream("LZWDecode", payload, parms);

        editor.RepairStreamFilters(new HashSet<int> { n });

        PdfStream repaired = FindStream(editor, n);
        Assert.Null(repaired.Dictionary.Get("DecodeParms"));
        Assert.Equal(payload, repaired.GetDecodedData());
    }

    // The staged set is load-bearing, not a convenience: a caller that resolved null to "everything"
    // would repair streams the user never staged or explicitly undid.
    [Fact]
    public void Repair_touches_only_the_object_numbers_it_was_given()
    {
        var doc = new PdfDocument();
        var staged = new PdfStream(new PdfDictionary(), []);
        staged.SetEncodedData("staged"u8.ToArray(), "LZWDecode");
        var unstaged = new PdfStream(new PdfDictionary(), []);
        unstaged.SetEncodedData("unstaged"u8.ToArray(), "LZWDecode");
        int stagedNumber = doc.RegisterObject(staged).ObjectNumber;
        int unstagedNumber = doc.RegisterObject(unstaged).ObjectNumber;
        var editor = new PdfDocumentEditor(doc);

        StreamFilterRepairReport report = editor.RepairStreamFilters(new HashSet<int> { stagedNumber });

        Assert.Equal(stagedNumber, Assert.Single(report.Applied).ObjectNumber);
        Assert.Empty(report.Refused);
        Assert.Equal("LZWDecode",
            Assert.IsType<PdfName>(FindStream(editor, unstagedNumber).Dictionary.Get("Filter")).Value);
    }

    // The other half of the objectNumbers contract, uncovered until now: null means "every offending
    // stream in the document," not "none." Repair_touches_only_the_object_numbers_it_was_given only
    // proves the staged-set side; a regression that made null behave like an empty set would still pass
    // every other test in this file. Two streams, not one -- a single-stream fixture cannot distinguish
    // "converted everything" from "converted the one stream it happened to see."
    [Fact]
    public void Repair_with_no_staged_set_converts_every_offending_stream()
    {
        var doc = new PdfDocument();
        var first = new PdfStream(new PdfDictionary(), []);
        first.SetEncodedData("first"u8.ToArray(), "LZWDecode");
        var second = new PdfStream(new PdfDictionary(), []);
        second.SetEncodedData("second"u8.ToArray(), "LZWDecode");
        int firstNumber = doc.RegisterObject(first).ObjectNumber;
        int secondNumber = doc.RegisterObject(second).ObjectNumber;
        var editor = new PdfDocumentEditor(doc);

        StreamFilterRepairReport report = editor.RepairStreamFilters();

        Assert.Equal(2, report.Applied.Count);
        Assert.Contains(report.Applied, r => r.ObjectNumber == firstNumber);
        Assert.Contains(report.Applied, r => r.ObjectNumber == secondNumber);
        Assert.Equal("FlateDecode",
            Assert.IsType<PdfName>(FindStream(editor, firstNumber).Dictionary.Get("Filter")).Value);
        Assert.Equal("FlateDecode",
            Assert.IsType<PdfName>(FindStream(editor, secondNumber).Dictionary.Get("Filter")).Value);
    }

    [Fact]
    public void Repair_refuses_the_same_streams_the_preview_refuses()
    {
        var doc = new PdfDocument();
        var bad = new PdfStream(new PdfDictionary(), new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
        bad.Dictionary[PdfName.Filter] = new PdfName("LZWDecode");
        int n = doc.RegisterObject(bad).ObjectNumber;
        var editor = new PdfDocumentEditor(doc);

        StreamFilterRepairPreview preview = editor.PreviewStreamFilterRepairs();
        StreamFilterRepairReport report = editor.RepairStreamFilters(new HashSet<int> { n });

        Assert.Empty(report.Applied);
        Assert.Equal(
            Assert.Single(preview.Refused).ObjectNumber,
            Assert.Single(report.Refused).ObjectNumber);
    }

    // A chained [/ASCIIHexDecode /LZWDecode] fully decodes to ONE FlateDecode stream. The corpus has no
    // such specimen (all 12 documents carry a bare /LZWDecode), so this is the synthetic fixture that
    // keeps the chain path honest -- a canary calibrated only to today's corpus would pass while the
    // code is wrong. Uses ASCIIHexDecode rather than ASCII85Decode per the controller ruling for this
    // task (see AsciiHexEncode above).
    [Fact]
    public void Repair_collapses_a_filter_chain_containing_LZW_to_a_single_Flate()
    {
        byte[] payload = "chained payload"u8.ToArray();
        var doc = new PdfDocument();
        var stream = new PdfStream(new PdfDictionary(), []);
        stream.SetEncodedData(payload, "LZWDecode");                     // inner: LZW bytes
        byte[] lzwBytes = stream.Data;                                   // raw, still LZW-encoded
        var chained = new PdfStream(new PdfDictionary(), AsciiHexEncode(lzwBytes));
        chained.Dictionary[PdfName.Filter] =
            new PdfArray(new PdfName("ASCIIHexDecode"), new PdfName("LZWDecode"));
        int n = doc.RegisterObject(chained).ObjectNumber;
        var editor = new PdfDocumentEditor(doc);

        StreamFilterRepairReport report = editor.RepairStreamFilters(new HashSet<int> { n });

        Assert.Equal(["ASCIIHexDecode", "LZWDecode"], Assert.Single(report.Applied).Before);
        PdfStream repaired = FindStream(editor, n);
        Assert.Equal("FlateDecode", Assert.IsType<PdfName>(repaired.Dictionary.Get("Filter")).Value);
        Assert.Equal(payload, repaired.GetDecodedData());
    }
}
