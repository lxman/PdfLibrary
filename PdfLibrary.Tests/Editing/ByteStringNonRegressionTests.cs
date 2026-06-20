using PdfLibrary.Builder;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// Non-regression guards: proves that the PdfDocEncoding/text-string sub-project did NOT corrupt byte-string paths.
/// All three tests document already-correct behavior and should PASS immediately.
/// If any FAIL, that is a real regression — investigate before proceeding.
/// </summary>
public class ByteStringNonRegressionTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static byte[] OnePageDoc() =>
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hello PDF", 100, 700))
            .ToByteArray();

    private static byte[] SaveViaEditor(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Edit().Save(ms);
        return ms.ToArray();
    }

    // ── Test 1: /ID bytes survive a save/reload cycle ─────────────────────────
    //
    // Variant used: REAL /ID test.
    // PdfDocumentBuilder.Create() writes a 16-byte random /ID pair into the trailer
    // (see PdfDocumentWriter.cs line ~674).  We load the doc, read the first /ID
    // element's raw bytes, save via the editor, reload, and assert the bytes are
    // identical.  This guards against the text-encoding layer accidentally treating
    // the binary /ID bytes as a text string and re-encoding them.

    [Fact]
    public void TrailerId_SurvivesSaveReload_ByteIdentical()
    {
        byte[] docBytes = OnePageDoc();

        // -- read original /ID --
        byte[] originalId;
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(docBytes)))
        {
            PdfArray? idArray = doc.Trailer.Id;
            Assert.NotNull(idArray);
            Assert.True(idArray!.Count >= 2, "/ID must have two elements");
            var idString = idArray[0] as PdfString;
            Assert.NotNull(idString);
            originalId = idString!.Bytes;
            Assert.Equal(16, originalId.Length); // standard 16-byte MD5-style ID
        }

        // -- save via editor, reload, re-read /ID --
        byte[] savedBytes = SaveViaEditor(PdfDocument.Load(new MemoryStream(docBytes)));

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(savedBytes));
        PdfArray? savedIdArray = reloaded.Trailer.Id;
        Assert.NotNull(savedIdArray);
        Assert.True(savedIdArray!.Count >= 2);
        var savedIdString = savedIdArray[0] as PdfString;
        Assert.NotNull(savedIdString);
        byte[] savedId = savedIdString!.Bytes;

        Assert.Equal(originalId, savedId);
    }

    // ── Test 2: Content-stream text extraction is unchanged ───────────────────
    //
    // Variant used: in-memory doc built with PdfDocumentBuilder.
    // We build a doc with known text ("Hello PDF"), extract all text, and assert
    // the known string is present.  This guards against ExtractText accidentally
    // using GetText() (which BOM-sniffs) instead of reading show-string bytes
    // via Value (Latin-1) as it always has.

    [Fact]
    public void TextExtraction_ShowString_ReturnsKnownContent()
    {
        byte[] docBytes = OnePageDoc();
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(docBytes));

        string all = doc.ExtractAllText();

        Assert.False(string.IsNullOrWhiteSpace(all), "Extracted text must be non-empty");
        Assert.Contains("Hello PDF", all, StringComparison.Ordinal);
    }

    // ── Test 3: Named-destination name round-trips as bytes (non-ASCII) ───────
    //
    // Variant used: non-ASCII name via a byte > 0x7F.
    // PDF name-tree keys are PDF strings (byte strings), not text strings. We use
    // the name "desté" which contains byte 0xE9 (é in Latin-1), which is > 0x7F.
    // We set a destination under this name, save/reload, and assert the destination
    // is retrievable under the SAME name — proving the name-tree key survives the
    // round-trip as raw bytes and is NOT re-encoded via the text-string path.

    [Fact]
    public void NamedDestination_NonAsciiName_RoundTripsAsBytes()
    {
        const string destName = "desté"; // contains byte 0xE9 (Latin-1 é)

        byte[] docBytes = OnePageDoc();
        byte[] saved;

        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(docBytes)))
        {
            PdfDocumentEditor edit = doc.Edit();
            edit.NamedDestinations.Set(destName, PdfDestination.FitPage(0));

            using var ms = new MemoryStream();
            edit.Save(ms);
            saved = ms.ToArray();
        }

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(saved));
        PdfDocumentEditor edit2 = reloaded.Edit();
        PdfDestination? got = edit2.NamedDestinations.Get(destName);

        Assert.NotNull(got);
        Assert.Equal(0, got!.PageIndex);
        Assert.Equal(PdfDestinationType.Fit, got.Type);
    }
}
