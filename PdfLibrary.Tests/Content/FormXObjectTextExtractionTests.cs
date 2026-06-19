using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Content;

/// <summary>
/// Regression tests for text extraction through Form XObjects.
///
/// <see cref="PdfLibrary.Content.PdfTextExtractor"/> must thread the owning <see cref="PdfDocument"/>
/// into nested Form XObject extraction so the form's content stream can be decrypted and its fonts
/// (indirect ToUnicode/encoding/descriptor streams) resolved. Before that fix the extractor created
/// the form's <c>PdfResources</c> with no document and decoded the form stream with a null decryptor,
/// so an encrypted PDF whose body lived in a Form XObject extracted ONLY the page-level footer — the
/// allmand spec sheet returned just 61 chars ("Powered by TCPDF ... Courtesy of Machine.Market")
/// instead of its ~2568-char body.
/// </summary>
public class FormXObjectTextExtractionTests
{
    // An RC4-40 encrypted (empty user password) TCPDF spec sheet. Machine.Market re-imposition wraps
    // the original page as a Form XObject that uses subset CFF (AbadiMT) fonts with NO ToUnicode CMap,
    // so correct extraction depends on resolving the font descriptors via the threaded document.
    private static string AllmandPath() => Path.GetFullPath(Path.Combine(
        Directory.GetCurrentDirectory(),
        "..", "..", "..", "..", "ImageLibrary", "TestImages", "jpeg_test",
        "allmand-backhoe-loaders-spec-e15132.pdf"));

    [Fact]
    public void EncryptedDoc_FormXObjectBody_IsExtracted_NotJustPageFooter()
    {
        string path = AllmandPath();
        if (!File.Exists(path))
            return; // guarded like the other fixture-based tests

        using PdfDocument doc = PdfDocument.Load(path);
        Assert.True(doc.IsEncrypted);

        string text = doc.ExtractAllText();

        // The page-level footer alone is ~61 chars; recovering the Form XObject body pushes well past
        // that. (Pre-fix this returned 61.)
        Assert.True(text.Length > 500, $"expected form body text, got {text.Length} chars: \"{text}\"");
        // A stable token from the Form XObject body — completely missing before the document-threading fix.
        Assert.Contains("alphabetical notation", text);
        // The page-level footer must still extract too.
        Assert.Contains("TCPDF", text);
    }
}
