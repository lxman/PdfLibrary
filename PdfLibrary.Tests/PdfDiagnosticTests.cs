using PdfLibrary.Parsing;
using System.Text;

namespace PdfLibrary.Tests;

[Trait("Category", "LocalOnly")]
public class PdfDiagnosticTests
{
    [Fact]
    public void DiagnosePdf20Parsing()
    {
        // Arrange
        string testFilePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "PDFs", "pdf20examples", "Simple PDF 2.0 file.pdf");

        testFilePath = Path.GetFullPath(testFilePath);

        using FileStream stream = File.OpenRead(testFilePath);

        // Find startxref position
        long startxref = PdfTrailerParser.FindStartXref(stream);
        Console.WriteLine($"startxref position: {startxref}");

        // Seek to startxref
        stream.Position = startxref;

        // Read 512 bytes to see what's there
        var buffer = new byte[512];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);
        string content = Encoding.ASCII.GetString(buffer, 0, bytesRead);
        Console.WriteLine("Content at startxref position:");
        Console.WriteLine(content);
        Console.WriteLine();

        // Now test manually reading through xref to see where trailer detection fails
        stream.Position = startxref;

        // Read "xref" keyword
        var lineBuffer = new byte[256];
        var linePos = 0;
        int b;
        while ((b = stream.ReadByte()) != -1 && b != '\n' && linePos < 255)
        {
            if (b != '\r') lineBuffer[linePos++] = (byte)b;
        }
        string line = Encoding.ASCII.GetString(lineBuffer, 0, linePos);
        Console.WriteLine($"Line 1: '{line}' (pos after: {stream.Position})");

        // Read subsection header "0 10"
        linePos = 0;
        while ((b = stream.ReadByte()) != -1 && b != '\n' && linePos < 255)
        {
            if (b != '\r') lineBuffer[linePos++] = (byte)b;
        }
        line = Encoding.ASCII.GetString(lineBuffer, 0, linePos);
        Console.WriteLine($"Line 2 (subsection): '{line}' (pos after: {stream.Position})");

        // Skip 10 xref entries
        for (var i = 0; i < 10; i++)
        {
            linePos = 0;
            while ((b = stream.ReadByte()) != -1 && b != '\n' && linePos < 255)
            {
                if (b != '\r') lineBuffer[linePos++] = (byte)b;
            }
            if (i == 9) // Show last entry
            {
                line = Encoding.ASCII.GetString(lineBuffer, 0, linePos);
                Console.WriteLine($"Last xref entry: '{line}' (pos after: {stream.Position})");
            }
        }

        // Read next line - should be "trailer"
        linePos = 0;
        while ((b = stream.ReadByte()) != -1 && b != '\n' && linePos < 255)
        {
            if (b != '\r') lineBuffer[linePos++] = (byte)b;
        }
        line = Encoding.ASCII.GetString(lineBuffer, 0, linePos);
        Console.WriteLine($"After xref entries: '{line}' (pos after: {stream.Position})");
    }
}
