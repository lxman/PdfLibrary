using System;
using System.IO;
using System.Threading;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Content;

/// <summary>
/// GWG161 contains a cyclic Form XObject /Do (a form that transitively invokes itself). Before the
/// cycle guard in <see cref="PdfLibrary.Content.PdfTextExtractor"/>, extracting its text recursed until
/// the stack overflowed — which crashed PdfLibrary.App on opening the file (text extraction runs on open for
/// the search/text layer). The guard must make extraction terminate.
/// </summary>
public class CyclicFormTextExtractionTests
{
    private static string? FindGwg(string file)
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
        {
            string p = Path.Combine(dir.FullName, "gwg-gos", "Ghent_PDF_Output_Suite_V50_Patches",
                "Categories", "1-CMYK", "Patches", file);
            if (File.Exists(p)) return p;
            // Also check a sibling ~/RiderProjects/gwg-gos layout
            string sib = Path.Combine(dir.FullName, "..", "gwg-gos", "Ghent_PDF_Output_Suite_V50_Patches",
                "Categories", "1-CMYK", "Patches", file);
            if (File.Exists(sib)) return sib;
        }
        return null;
    }

    [Fact]
    public void Gwg161_text_extraction_terminates_no_stack_overflow()
    {
        string? path = FindGwg("GWG161_Transp_Basic_BM_DeviceCMYK_Knockout_X4.pdf");
        if (path is null) return; // corpus not present locally

        using PdfDocument doc = PdfDocument.Load(path);

        // Run on a small-stack thread so a regression (unbounded recursion) fails fast as a caught
        // exception rather than tearing down the whole test host.
        Exception? error = null;
        var done = false;
        var t = new Thread(() =>
        {
            try { _ = doc.ExtractAllText(); done = true; }
            catch (Exception e) { error = e; }
        }, 512 * 1024);
        t.Start();
        bool finished = t.Join(TimeSpan.FromSeconds(20));

        Assert.True(finished, "text extraction did not terminate (probable runaway form recursion)");
        Assert.Null(error);
        Assert.True(done);
    }
}
