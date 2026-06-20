using System.Text;
using System.Text.RegularExpressions;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

public class TextAppearanceMultilineTests
{
    // ── AP stream helpers (mirrors TextAppearanceTests) ───────────────────────

    private static string ApStreamText(PdfDocument doc, PdfTextField field)
    {
        var widget = field.Widgets[0];
        var apRaw = widget.Get(new PdfName("AP"));
        var ap = FormFieldTree.Resolve(doc, apRaw) as PdfDictionary;
        Assert.NotNull(ap);

        var nRaw = ap!.Get(new PdfName("N"));
        var nObj = FormFieldTree.Resolve(doc, nRaw);
        Assert.NotNull(nObj);

        var stream = nObj as PdfStream;
        if (stream is null && nObj is PdfIndirectReference ir)
            stream = doc.GetObject(ir.ObjectNumber) as PdfStream;

        Assert.NotNull(stream);
        return Encoding.ASCII.GetString(stream!.GetDecodedData());
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Multiline_WrapsToMultipleLines()
    {
        // Ff bit 13 (1-based) = bit 12 (0-based) = 4096
        const int multilineFf = 1 << 12; // = 4096
        const string longValue = "The quick brown fox jumps over the lazy dog repeatedly";

        byte[] pdf = FormTestDocs.WithTextFieldEx(
            name: "ml",
            value: null,
            ff: multilineFf,
            maxLen: null,
            rectW: 200,
            rectH: 60);

        string outPath = Path.GetTempFileName();
        try
        {
            using (PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf)))
            {
                var edit = doc.Edit();
                var field = (PdfTextField)edit.Forms["ml"]!;
                field.Value = longValue;
                edit.Save(outPath);
            }

            using PdfDocument re = PdfDocument.Load(outPath);
            var f = (PdfTextField)re.Edit().Forms["ml"]!;
            string ap = ApStreamText(re, f);

            // Must contain the /Tx BMC wrapper
            Assert.Contains("/Tx BMC", ap);

            // Must have at least 2 Tj show operations (at least 2 lines)
            int tjCount = Regex.Matches(ap, @"\bTj\b").Count;
            Assert.True(tjCount >= 2,
                $"Expected ≥2 Tj operations for multiline but got {tjCount}.\nAP stream:\n{ap}");

            // Evidence of multiple lines: either negative y in Td, or ≥2 Tm lines, or ≥1 Td line
            bool hasTd = Regex.IsMatch(ap, @"-[\d.]+\s+Td");
            bool hasMultipleTm = Regex.Matches(ap, @"\bTm\b").Count >= 2;
            bool hasAnyTd = ap.Contains(" Td");
            Assert.True(hasTd || hasMultipleTm || hasAnyTd,
                $"Expected evidence of multiple lines (negative Td or multiple Tm) in:\n{ap}");
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Comb_DistributesCharsIntoCells()
    {
        // Ff bit 25 (1-based) = bit 24 (0-based) = 16777216
        const int combFf = 1 << 24; // = 16777216
        const string combValue = "ABC";
        const int maxLen = 5;
        const double rectW = 100;
        const double rectH = 20;

        byte[] pdf = FormTestDocs.WithTextFieldEx(
            name: "comb",
            value: null,
            ff: combFf,
            maxLen: maxLen,
            rectW: rectW,
            rectH: rectH);

        string outPath = Path.GetTempFileName();
        try
        {
            using (PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf)))
            {
                var edit = doc.Edit();
                var field = (PdfTextField)edit.Forms["comb"]!;
                field.Value = combValue;
                edit.Save(outPath);
            }

            using PdfDocument re = PdfDocument.Load(outPath);
            var f = (PdfTextField)re.Edit().Forms["comb"]!;
            string ap = ApStreamText(re, f);

            // Must contain the /Tx BMC wrapper
            Assert.Contains("/Tx BMC", ap);

            // Must have exactly 3 Tj operations (one per character in "ABC")
            int tjCount = Regex.Matches(ap, @"\bTj\b").Count;
            Assert.Equal(3, tjCount);

            // Extract Tm x positions from "1 0 0 1 <x> <y> Tm" lines
            var tmMatches = Regex.Matches(ap, @"1 0 0 1 ([\d.]+) ([\d.]+) Tm");
            Assert.Equal(3, tmMatches.Count);

            double[] xPositions = tmMatches
                .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();

            // x positions must be strictly increasing (each char in its own cell)
            for (int i = 1; i < xPositions.Length; i++)
            {
                Assert.True(xPositions[i] > xPositions[i - 1],
                    $"Expected increasing x positions but got [{string.Join(", ", xPositions)}]");
            }

            // Cell width = (rectW - 2*pad) / maxLen = (100 - 4) / 5 = 19.2
            // Spacing between adjacent cell centers should be roughly cellW
            const double pad = 2.0;
            double expectedCellW = (rectW - 2 * pad) / maxLen;
            double actualSpacing = xPositions[1] - xPositions[0];
            // Allow ±50% tolerance for character width variation within cell centering
            Assert.True(
                actualSpacing > expectedCellW * 0.5 && actualSpacing < expectedCellW * 1.5,
                $"Expected inter-char spacing ≈{expectedCellW:F2} but got {actualSpacing:F2}");
        }
        finally
        {
            File.Delete(outPath);
        }
    }
}
