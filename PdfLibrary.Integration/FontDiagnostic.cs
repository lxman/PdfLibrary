using System;
using PdfLibrary.Core;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Integration;

/// <summary>
/// Walks every font on a given PDF page and reports the state of the rendering chain:
/// which fonts exist, how they are typed, whether FontFile2/3 data is present, whether the
/// embedded-metrics parser succeeded, and what kind of outline lookup happens for a sample CID.
///
/// Run via:
///   dotnet run --project PdfLibrary.Integration -- font-diagnose &lt;path-to-pdf&gt; &lt;page-number&gt;
/// </summary>
internal static class FontDiagnostic
{
    public static int Run(string pdfPath, int pageNumber)
    {
        Console.WriteLine($"== Font diagnostic for {pdfPath}, page {pageNumber} ==\n");

        if (!System.IO.File.Exists(pdfPath))
        {
            Console.Error.WriteLine($"PDF not found: {pdfPath}");
            return 1;
        }

        using var doc = PdfDocument.Load(pdfPath);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
        {
            Console.Error.WriteLine($"Page {pageNumber} out of range (1..{doc.PageCount}).");
            return 1;
        }

        PdfPage? page = doc.GetPage(pageNumber - 1);
        if (page is null)
        {
            Console.Error.WriteLine($"GetPage({pageNumber - 1}) returned null.");
            return 1;
        }

        PdfResources? resources = page.GetResources();
        if (resources is null)
        {
            Console.WriteLine("(no resources on page)");
            return 0;
        }

        System.Collections.Generic.List<string> names = resources.GetFontNames();
        Console.WriteLine($"Fonts on page: {names.Count} ({string.Join(", ", names)})\n");

        foreach (string name in names)
        {
            Console.WriteLine($"--- /{name} ---");
            PdfFont? font = resources.GetFontObject(name);
            if (font is null)
            {
                Console.WriteLine("  (GetFontObject returned null)");
                continue;
            }
            DescribeFont(font);
            Console.WriteLine();
        }
        return 0;
    }

    private static void DescribeFont(PdfFont font)
    {
        Console.WriteLine($"  Constructed as: {font.GetType().Name}");

        if (font is Type0Font type0)
        {
            if (type0.DescendantFont is not CidFont cid)
            {
                Console.WriteLine("  Type0 font has no CidFont descendant.");
                return;
            }
            Console.WriteLine($"  DescendantFont: CidFont");

            PdfFontDescriptor? desc = cid.GetDescriptor();
            DescribeDescriptor(desc);
            EmbeddedFontMetrics? metrics = type0.GetEmbeddedMetrics();

            // Inspect the CFF charset to see what CIDs the subset's GIDs correspond to.
            if (metrics is not null && metrics.IsCffFont)
            {
                DescribeCharset(desc);
            }

            DescribeMetrics(metrics, sampleGid: (ushort)cid.MapCidToGid(0x21));
        }
        else
        {
            PdfFontDescriptor? desc = font.GetDescriptor();
            DescribeDescriptor(desc);
            EmbeddedFontMetrics? metrics = font.GetEmbeddedMetrics();
            DescribeMetrics(metrics, sampleGid: 1);
        }
    }

    private static void DescribeDescriptor(PdfFontDescriptor? desc)
    {
        if (desc is null)
        {
            Console.WriteLine("  Descriptor: (null)");
            return;
        }
        byte[]? ff1Data = desc.GetFontFileWithLengths()?.data;
        byte[]? ff2 = desc.GetFontFile2();
        byte[]? ff3 = desc.GetFontFile3();
        Console.WriteLine($"  Descriptor: FontFile={(ff1Data is null ? "(none)" : $"{ff1Data.Length} bytes")}, " +
                          $"FontFile2={(ff2 is null ? "(none)" : $"{ff2.Length} bytes")}, " +
                          $"FontFile3={(ff3 is null ? "(none)" : $"{ff3.Length} bytes")}");
        if (ff3 is not null && ff3.Length >= 4)
        {
            string header = $"{ff3[0]:X2} {ff3[1]:X2} {ff3[2]:X2} {ff3[3]:X2}";
            string interpretation = ff3[0] == 0x01
                ? "(CFF major version)"
                : ff3[0] == 'O' && ff3[1] == 'T' && ff3[2] == 'T' && ff3[3] == 'O'
                    ? "(OpenType/CFF 'OTTO')"
                    : "(unknown)";
            Console.WriteLine($"    FontFile3 header: {header} {interpretation}");
        }
    }

    private static void DescribeCharset(PdfFontDescriptor? desc)
    {
        byte[]? ff3 = desc?.GetFontFile3();
        if (ff3 is null) return;
        try
        {
            var cff = new FontParser.Tables.Cff.Type1.Type1Table(ff3);
            FontParser.Tables.Cff.Type1.Charsets.ICharset? cs = cff.CharSet;
            Console.WriteLine($"  CFF charset type: {cs?.GetType().Name ?? "null"}");
            switch (cs)
            {
                case FontParser.Tables.Cff.Type1.Charsets.CharsetsFormat0 f0:
                    Console.Write("    Format 0 SIDs/CIDs (GID 1..): ");
                    for (int i = 0; i < Math.Min(20, f0.Glyphs.Count); i++) Console.Write($"{f0.Glyphs[i]} ");
                    if (f0.Glyphs.Count > 20) Console.Write($"... ({f0.Glyphs.Count} total)");
                    Console.WriteLine();
                    break;
                case FontParser.Tables.Cff.Type1.Charsets.CharsetsFormat1 f1:
                    Console.WriteLine($"    Format 1 ranges: {f1.Ranges.Count}");
                    for (int i = 0; i < Math.Min(10, f1.Ranges.Count); i++)
                        Console.WriteLine($"      Range {i}: First={f1.Ranges[i].First}, NumberLeft={f1.Ranges[i].NumberLeft} (covers CIDs {f1.Ranges[i].First}..{f1.Ranges[i].First + f1.Ranges[i].NumberLeft})");
                    if (f1.Ranges.Count > 10) Console.WriteLine($"      ... ({f1.Ranges.Count} total ranges)");
                    break;
                case FontParser.Tables.Cff.Type1.Charsets.CharsetsFormat2 f2:
                    Console.WriteLine($"    Format 2 ranges: {f2.Ranges.Count}");
                    for (int i = 0; i < Math.Min(10, f2.Ranges.Count); i++)
                        Console.WriteLine($"      Range {i}: First={f2.Ranges[i].First}, NumberLeft={f2.Ranges[i].NumberLeft} (covers CIDs {f2.Ranges[i].First}..{f2.Ranges[i].First + f2.Ranges[i].NumberLeft})");
                    if (f2.Ranges.Count > 10) Console.WriteLine($"      ... ({f2.Ranges.Count} total ranges)");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Charset inspection threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void DescribeMetrics(EmbeddedFontMetrics? metrics, ushort sampleGid)
    {
        if (metrics is null)
        {
            Console.WriteLine("  EmbeddedFontMetrics: (null)");
            return;
        }
        Console.WriteLine($"  EmbeddedFontMetrics: IsValid={metrics.IsValid}, IsCffFont={metrics.IsCffFont}, " +
                          $"IsType1Font={metrics.IsType1Font}, UnitsPerEm={metrics.UnitsPerEm}, " +
                          $"NumGlyphs={metrics.NumGlyphs}");

        if (!metrics.IsValid) return;

        try
        {
            FontParser.Tables.Cff.GlyphOutline? cffOut = metrics.GetCffGlyphOutlineDirect(sampleGid);
            if (cffOut is null)
            {
                Console.WriteLine($"  GetCffGlyphOutlineDirect({sampleGid}) = null");
            }
            else
            {
                int cmdCount = cffOut.Commands?.Count ?? 0;
                Console.WriteLine($"  GetCffGlyphOutlineDirect({sampleGid}) = {cmdCount} path command(s)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  GetCffGlyphOutlineDirect({sampleGid}) threw: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            GlyphOutline? gOut = metrics.GetGlyphOutline(sampleGid);
            int contours = gOut?.Contours?.Count ?? 0;
            Console.WriteLine($"  GetGlyphOutline({sampleGid}) = {contours} contour(s)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  GetGlyphOutline({sampleGid}) threw: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
