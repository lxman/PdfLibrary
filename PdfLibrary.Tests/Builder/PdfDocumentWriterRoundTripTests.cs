using System.Text;
using PdfLibrary.Builder;
using PdfLibrary.Builder.Layer;
using PdfLibrary.Document;
using PdfLibrary.Security;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Builder;

/// <summary>
/// End-to-end round-trip tests for the builder write path: build → serialize → re-parse → assert.
/// Exercises PdfDocumentWriter (2,343 LOC, previously untested) against PdfDocument.Load
/// to catch regressions where writer output diverges from what the parser accepts.
/// </summary>
public class PdfDocumentWriterRoundTripTests
{
    private static PdfDocument LoadRoundTrip(PdfDocumentBuilder builder, string? password = null)
    {
        byte[] bytes = builder.ToByteArray();
        var ms = new MemoryStream(bytes);
        return password is null
            ? PdfDocument.Load(ms)
            : PdfDocument.Load(ms, password);
    }

    // ----- Basic structure -----

    [Fact]
    public void SinglePage_RoundTrips_WithCorrectPageCount()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hello", 100, 700));

        using PdfDocument doc = LoadRoundTrip(builder);

        Assert.Equal(1, doc.PageCount);
        Assert.NotNull(doc.GetPage(0));
    }

    [Fact]
    public void MultiplePages_RoundTrip_PreservesCount()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .AddPage(p => p.AddText("Page 2", 100, 700))
            .AddPage(p => p.AddText("Page 3", 100, 700));

        using PdfDocument doc = LoadRoundTrip(builder);

        Assert.Equal(3, doc.PageCount);
    }

    [Fact]
    public void MixedPageSizes_RoundTrip_PreservesDimensions()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.Letter, p => p.AddText("Letter", 100, 700))
            .AddPage(PdfPageSize.A4, p => p.AddText("A4", 100, 700))
            .AddPage(PdfPageSize.A5, p => p.AddText("A5", 100, 700));

        using PdfDocument doc = LoadRoundTrip(builder);

        Assert.Equal(3, doc.PageCount);

        PdfPage? letter = doc.GetPage(0);
        PdfPage? a4 = doc.GetPage(1);
        PdfPage? a5 = doc.GetPage(2);

        Assert.NotNull(letter);
        Assert.NotNull(a4);
        Assert.NotNull(a5);
        Assert.Equal(612, letter.Width, 1);
        Assert.Equal(792, letter.Height, 1);
        Assert.Equal(595, a4.Width, 1);
        Assert.Equal(842, a4.Height, 1);
        Assert.Equal(420, a5.Width, 1);
        Assert.Equal(595, a5.Height, 1);
    }

    [Fact]
    public void EmptyDocument_NoPages_StillProducesValidPdf()
    {
        // No pages — writer must still emit a parseable shell.
        var builder = PdfDocumentBuilder.Create();

        byte[] bytes = builder.ToByteArray();

        Assert.NotEmpty(bytes);
        string header = Encoding.ASCII.GetString(bytes, 0, 8);
        Assert.StartsWith("%PDF-", header);
    }

    // ----- Metadata -----

    [Fact]
    public void Metadata_TitleAndAuthor_RoundTrip()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .WithMetadata(m => m
                .SetTitle("Round Trip Title")
                .SetAuthor("Test Author")
                .SetSubject("Regression test")
                .SetKeywords("test, round-trip, pdf")
                .SetCreator("PdfLibrary.Tests")
                .SetProducer("PdfLibrary"))
            .AddPage(p => p.AddText("body", 100, 700));

        byte[] bytes = builder.ToByteArray();
        string content = Encoding.ASCII.GetString(bytes);

        // The /Info dictionary is internal; verify via the raw stream that the values made it in.
        Assert.Contains("Round Trip Title", content);
        Assert.Contains("Test Author", content);
        Assert.Contains("Regression test", content);

        // And confirm the document still parses cleanly.
        using PdfDocument doc = LoadRoundTrip(builder);
        Assert.Equal(1, doc.PageCount);
    }

    // ----- Bookmarks -----

    [Fact]
    public void Bookmarks_FlatList_RoundTripsAndParses()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Chapter 1", 100, 700))
            .AddPage(p => p.AddText("Chapter 2", 100, 700))
            .AddPage(p => p.AddText("Chapter 3", 100, 700))
            .AddBookmark("Chapter 1", 0)
            .AddBookmark("Chapter 2", 1)
            .AddBookmark("Chapter 3", 2);

        using PdfDocument doc = LoadRoundTrip(builder);

        Assert.Equal(3, doc.PageCount);
        // Re-parsed PDF should expose the outlines dictionary on the catalog.
        // Detailed outline walking is internal; the byte-stream side is covered by PdfBookmarkTests.
    }

    [Fact]
    public void Bookmarks_Nested_RoundTrip_RemainsParseable()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Top", 100, 700))
            .AddBookmark("Parent", b => b
                .ToPage(0)
                .AddChild("Child 1")
                .AddChild("Child 2", c => c.AddChild("Grandchild")));

        // The risk here is that nested outline /First /Last /Next /Prev pointers become
        // inconsistent — when that happens, the parser silently fails or hangs. Confirm load.
        using PdfDocument doc = LoadRoundTrip(builder);
        Assert.Equal(1, doc.PageCount);
    }

    // ----- Layers (Optional Content Groups) -----

    [Fact]
    public void Layers_Defined_RoundTrip_ProducesValidPdf()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Background", out PdfLayer _)
            .DefineLayer("Foreground", out PdfLayer _)
            .AddPage(p => p.AddText("Layered", 100, 700));

        using PdfDocument doc = LoadRoundTrip(builder);
        Assert.Equal(1, doc.PageCount);
    }

    // ----- Page labels -----

    [Fact]
    public void PageLabels_DecimalWithPrefix_RoundTrip()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Cover", 100, 700))
            .AddPage(p => p.AddText("Body 1", 100, 700))
            .AddPage(p => p.AddText("Body 2", 100, 700))
            .SetPageLabels(0, 1, "A-")
            .SetPageLabels(1);

        using PdfDocument doc = LoadRoundTrip(builder);
        Assert.Equal(3, doc.PageCount);
    }

    // ----- Encryption -----

    [Fact]
    public void Encryption_Aes256_WithPassword_RoundTrip()
    {
        const string password = "secret-256";
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .WithPassword(password)
            .AddPage(p => p.AddText("Encrypted body", 100, 700));

        using PdfDocument doc = LoadRoundTrip(builder, password);

        Assert.True(doc.IsEncrypted);
        Assert.NotNull(doc.Decryptor);
        Assert.True(doc.Decryptor.IsDecrypted);
        Assert.Equal(PdfEncryptionMethod.Aes256, doc.Decryptor.Method);
        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void Encryption_Aes128_WithPassword_RoundTrip()
    {
        const string password = "secret-128";
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .WithEncryption(e => e
                .WithUserPassword(password)
                .WithOwnerPassword("owner")
                .WithMethod(PdfEncryptionMethod.Aes128)
                .AllowAll())
            .AddPage(p => p.AddText("Encrypted body", 100, 700));

        using PdfDocument doc = LoadRoundTrip(builder, password);

        Assert.True(doc.IsEncrypted);
        Assert.NotNull(doc.Decryptor);
        Assert.Equal(PdfEncryptionMethod.Aes128, doc.Decryptor.Method);
    }

    [Fact]
    public void Encryption_Rc4_128_WithPassword_RoundTrip()
    {
        const string password = "rc4-test";
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .WithEncryption(e => e
                .WithUserPassword(password)
                .WithOwnerPassword("owner")
                .WithMethod(PdfEncryptionMethod.Rc4_128)
                .AllowAll())
            .AddPage(p => p.AddText("Encrypted body", 100, 700));

        using PdfDocument doc = LoadRoundTrip(builder, password);

        Assert.True(doc.IsEncrypted);
        Assert.NotNull(doc.Decryptor);
        Assert.Equal(PdfEncryptionMethod.Rc4_128, doc.Decryptor.Method);
    }

    [Fact]
    public void Encryption_WrongPassword_ThrowsPdfSecurityException()
    {
        // Using AES-128 explicitly so the test exercises the password-check path,
        // not the AES-256 "always rejects" path documented above.
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .WithEncryption(e => e
                .WithUserPassword("correct-password")
                .WithOwnerPassword("owner")
                .WithMethod(PdfEncryptionMethod.Aes128)
                .AllowAll())
            .AddPage(p => p.AddText("body", 100, 700));

        byte[] bytes = builder.ToByteArray();
        var ms = new MemoryStream(bytes);

        Assert.Throws<PdfSecurityException>(() =>
            PdfDocument.Load(ms, "wrong-password"));
    }

    [Fact]
    public void Encryption_RestrictedPermissions_RoundTrip_AppliesFlags()
    {
        // Build with only print + copy permissions; everything else should be denied after re-load.
        // Using AES-128 because AES-256 doesn't round-trip yet (see Encryption_Aes256... above).
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .WithEncryption(e => e
                .WithUserPassword("u")
                .WithOwnerPassword("o")
                .WithMethod(PdfEncryptionMethod.Aes128)
                .DenyAll()
                .AllowPrinting()
                .AllowCopying())
            .AddPage(p => p.AddText("body", 100, 700));

        using PdfDocument doc = LoadRoundTrip(builder, "u");

        Assert.True(doc.IsEncrypted);
        PdfPermissions perms = doc.Permissions;
        Assert.True(perms.CanPrint);
        Assert.True(perms.CanCopyContent);
        Assert.False(perms.CanModifyContents);
        Assert.False(perms.CanModifyAnnotations);
        Assert.False(perms.CanFillForms);
    }

    // ----- Text content round-trip -----

    [Fact]
    public void TextContent_StandardFont_ExtractsBack()
    {
        const string body = "RoundTripTextProbe";
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText(body, 100, 700, "Helvetica", 12));

        using PdfDocument doc = LoadRoundTrip(builder);

        string extracted = doc.ExtractAllText();
        // The extractor concatenates glyphs; tolerate spacing variations.
        Assert.Contains(body, extracted.Replace(" ", string.Empty));
    }

    [Fact]
    public void TextContent_MultipleFonts_AllExtract()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p
                .AddText("AAA-Helvetica", 100, 700, "Helvetica", 10)
                .AddText("BBB-TimesRoman", 100, 670, "Times-Roman", 10)
                .AddText("CCC-Courier", 100, 640, "Courier", 10));

        using PdfDocument doc = LoadRoundTrip(builder);

        string extracted = doc.ExtractAllText().Replace(" ", string.Empty);
        Assert.Contains("AAA-Helvetica", extracted);
        Assert.Contains("BBB-TimesRoman", extracted);
        Assert.Contains("CCC-Courier", extracted);
    }

    // ----- PDF byte-level guarantees the writer should always honor -----

    [Fact]
    public void Output_AlwaysStartsWithPdfHeader()
    {
        byte[] bytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("x", 100, 700))
            .ToByteArray();

        Assert.True(bytes.Length > 16);
        string header = Encoding.ASCII.GetString(bytes, 0, 5);
        Assert.Equal("%PDF-", header);
    }

    [Fact]
    public void Output_ContainsEofMarker()
    {
        byte[] bytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("x", 100, 700))
            .ToByteArray();

        string tail = Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 32), Math.Min(32, bytes.Length));
        Assert.Contains("%%EOF", tail);
    }

    [Fact]
    public void Output_ContainsXrefStructure()
    {
        byte[] bytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("x", 100, 700))
            .ToByteArray();

        string content = Encoding.ASCII.GetString(bytes);
        Assert.Contains("xref", content);
        Assert.Contains("trailer", content);
        Assert.Contains("startxref", content);
    }

    [Fact]
    public void Output_TwoConsecutiveBuilds_AreDeterministicInShape()
    {
        using PdfDocument doc1 = LoadRoundTrip(Build());
        using PdfDocument doc2 = LoadRoundTrip(Build());

        Assert.Equal(doc1.PageCount, doc2.PageCount);
        Assert.Equal(doc1.Version, doc2.Version);
        return;

        // Builders may differ in bytes (timestamps, ids), but a second identical build
        // should produce a document with the same structural shape.
        PdfDocumentBuilder Build() => PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("deterministic", 100, 700))
            .AddPage(p => p.AddText("page two", 100, 700));
    }
}
