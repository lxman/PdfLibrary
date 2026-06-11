using System.IO;
using System.Text;
using PdfLibrary.Parsing;

namespace PdfLibrary.Tests;

public class PdfTrailerParserTests
{
    [Fact]
    public void FindStartXref_finds_keyword_past_trailing_padding()
    {
        // Some PDFs are padded with junk after %%EOF (e.g. padded to a fixed block size), pushing
        // startxref well outside the last KiB. The search must grow until it finds it. splashpres.pdf
        // (an Emscripten docs PDF) had ~27 KB of trailing zeros and previously failed to load with
        // "Could not find 'startxref' keyword".
        byte[] body = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\nstartxref\n12345\n%%EOF");
        var padded = new byte[body.Length + 30000]; // 30 KB of trailing zeros, far past the 1 KiB window
        body.CopyTo(padded, 0);
        using var ms = new MemoryStream(padded);

        Assert.Equal(12345L, PdfTrailerParser.FindStartXref(ms));
    }

    [Fact]
    public void FindStartXref_still_works_for_a_normal_trailer()
    {
        byte[] body = Encoding.ASCII.GetBytes("%PDF-1.4\nstartxref\n9\n%%EOF\n");
        using var ms = new MemoryStream(body);

        Assert.Equal(9L, PdfTrailerParser.FindStartXref(ms));
    }
}
