using System.Collections.Generic;
using System.IO;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Embedded;

/// <summary>
/// GWG090 embeds <c>NewCenturySchlbk-Italic</c> as a /Type1C FontFile3 whose Top DICT omits the optional
/// charset operator (CFF spec, Technical Note #5176 Table 9: default 0 = ISOAdobe). The CFF parser used to
/// throw on the missing operator; the throw was swallowed by the CFF/TrueType fallback in
/// <see cref="EmbeddedFontMetrics"/>, IsValid ended up false, and the renderer silently substituted a
/// system face for a perfectly good embedded font. The program must now parse.
/// <para>The font itself is licensed Adobe data and is NOT committed as a fixture — the test reads it out
/// of the sibling gwg-gos corpus checkout and returns when that is absent, like the other GWG tests.</para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class Type1CDefaultCharsetTests
{
    private const string Gwg090 = "GWG090_Font-Support_x3.pdf";
    private const string TargetFont = "NewCenturySchlbk-Italic";

    private static string? FindGwg(string file)
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
        {
            foreach (string root in new[] { dir.FullName, Path.Combine(dir.FullName, "..") })
            {
                string p = Path.Combine(root, "gwg-gos", "Ghent_PDF_Output_Suite_V50_Patches",
                    "Categories", "1-CMYK", "Patches", file);
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    [Fact]
    public void Gwg090_NewCenturySchlbkItalic_Type1C_ParsesWithDefaultCharset()
    {
        string? path = FindGwg(Gwg090);
        if (path is null) return; // corpus not present locally

        using PdfDocument doc = PdfDocument.Load(path);

        var fonts = new Dictionary<string, PdfFont>();
        for (var pageNum = 0; pageNum < doc.PageCount; pageNum++)
            Walk(doc.GetPage(pageNum)?.GetResources(), doc, fonts, new HashSet<int>(), 0);

        Assert.True(fonts.ContainsKey(TargetFont),
            $"{TargetFont} not found in {Gwg090}; saw [{string.Join(", ", fonts.Keys)}]");

        EmbeddedFontMetrics? metrics = fonts[TargetFont].GetEmbeddedMetrics();
        Assert.NotNull(metrics);
        Assert.True(metrics!.IsValid,
            $"{TargetFont} embedded program did not parse — the renderer would substitute a system face");
        Assert.Equal(229, metrics.NumGlyphs); // .notdef + 228 ISOAdobe glyphs

        // The default charset must materialize, so glyph names resolve and the subset-coverage rule can
        // enumerate the program. Without it EnumerateProgramGlyphNames would yield only ".notdef".
        IReadOnlySet<string>? names = metrics.EnumerateProgramGlyphNames();
        Assert.NotNull(names);
        Assert.Equal(229, names!.Count);
        Assert.Contains("A", names);
    }

    /// <summary>Collects fonts keyed by BaseFont from a page's resources and, recursively, from any Form
    /// XObject's — GWG090 puts this font inside a form, not on the page directly.</summary>
    private static void Walk(PdfResources? res, PdfDocument doc, Dictionary<string, PdfFont> fonts,
        HashSet<int> seenRes, int depth)
    {
        if (res is null || depth > 12) return;
        if (res.Dictionary.IsIndirect && !seenRes.Add(res.Dictionary.ObjectNumber)) return;

        foreach (string name in res.GetFontNames())
        {
            PdfFont? font = res.GetFontObject(name);
            if (font?.BaseFont is { } baseFont) fonts[StripSubsetPrefix(baseFont)] = font;
        }

        if (res.GetXObjects() is not { } xobjs) return;
        foreach (PdfObject x in xobjs.Values)
        {
            if (Deref(x, doc) is PdfStream { Dictionary: { } sd } &&
                (sd.Get("Subtype") as PdfName)?.Value == "Form" &&
                Deref(sd.Get("Resources"), doc) is PdfDictionary rd)
            {
                Walk(new PdfResources(rd, doc), doc, fonts, seenRes, depth + 1);
            }
        }
    }

    private static PdfObject? Deref(PdfObject? o, PdfDocument doc) =>
        o is PdfIndirectReference r ? doc.ResolveReference(r) : o;

    /// <summary>Drops a subset tag (six uppercase letters and a '+'), so the lookup keys on the real name.</summary>
    private static string StripSubsetPrefix(string baseFont) =>
        baseFont.Length > 7 && baseFont[6] == '+' ? baseFont[7..] : baseFont;
}
