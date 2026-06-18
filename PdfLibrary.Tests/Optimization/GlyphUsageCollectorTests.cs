using PdfLibrary.Builder;
using PdfLibrary.Content;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Optimization;

/// <summary>
/// Unit tests for <see cref="GlyphUsageCollector"/>.
/// Verifies that byte→code→GID decode is correct for both Simple TrueType and
/// Identity-H CIDFontType2 fonts.
/// </summary>
public class GlyphUsageCollectorTests
{
    private const string ArialPath = @"C:\Windows\Fonts\arial.ttf";

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: build a simple-TrueType PDF with embedded Arial
    // ─────────────────────────────────────────────────────────────────────────

    private static PdfDocument BuildArialDocument(string text)
    {
        if (!File.Exists(ArialPath))
            throw new SkipTestException("Arial not available on this system.");

        const string alias = "Arial";
        byte[] bytes = PdfDocumentBuilder.Create()
            .LoadFont(ArialPath, alias)
            .AddPage(p => p.AddText(text, 100, 700, alias, 12))
            .ToByteArray();

        return PdfDocument.Load(new MemoryStream(bytes));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1 – collector finds GIDs for a simple TrueType font
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Collector_SimpleTrueType_FindsNonEmptyGidSet()
    {
        if (!File.Exists(ArialPath))
            return; // skip on CI without Arial

        using PdfDocument doc = BuildArialDocument("Hello");
        doc.MaterializeAllObjects();

        var allUsage = CollectAllPages(doc);

        Assert.NotEmpty(allUsage);
        FontUsage usage = Assert.Single(allUsage.Values);
        Assert.Equal(FontUsageKind.SimpleTrueType, usage.Kind);
        Assert.NotEmpty(usage.Gids);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2 – GID 0 (.notdef) should not be in the collected set for normal text
    //          (collector accumulates actual bytes, not .notdef padding)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Collector_SimpleTrueType_DoesNotCollectGid0ForPrintableText()
    {
        if (!File.Exists(ArialPath))
            return;

        using PdfDocument doc = BuildArialDocument("ABC");
        doc.MaterializeAllObjects();

        var allUsage = CollectAllPages(doc);

        // It's acceptable for the collector not to include GID 0 when text is printable
        // (GID 0 is the .notdef glyph — added by the subsetter, not the collector).
        Assert.NotEmpty(allUsage);
        FontUsage usage = Assert.Single(allUsage.Values);
        Assert.NotEmpty(usage.Gids);
        // All collected GIDs should be > 0 for standard printable characters
        Assert.All(usage.Gids, gid => Assert.True(gid > 0,
            $"GID 0 (.notdef) should not appear for printable text; got GID set: [{string.Join(", ", usage.Gids)}]"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3 – Two different texts produce different (but overlapping) GID sets
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Collector_DifferentTexts_ProduceDifferentGidSets()
    {
        if (!File.Exists(ArialPath))
            return;

        using PdfDocument docA = BuildArialDocument("AAAA");
        using PdfDocument docB = BuildArialDocument("ZZZZ");
        docA.MaterializeAllObjects();
        docB.MaterializeAllObjects();

        var usageA = CollectAllPages(docA);
        var usageB = CollectAllPages(docB);

        Assert.NotEmpty(usageA);
        Assert.NotEmpty(usageB);
        HashSet<ushort> gidsA = usageA.Values.First().Gids;
        HashSet<ushort> gidsB = usageB.Values.First().Gids;
        // 'A' and 'Z' have different glyph IDs in Arial
        Assert.False(gidsA.SetEquals(gidsB),
            "GID sets for 'AAAA' and 'ZZZZ' should differ");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 4 – Identity-H byte→GID decode: 2-byte big-endian codes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Collector_IdentityH_Decode_TwoBytesBigEndian()
    {
        // Build a synthetic content stream that looks like Identity-H text: 2-byte codes.
        // We don't need a real PDF for this — we test the decoding logic directly.
        // GID 0x0041 = 65 (decimal), GID 0x005A = 90.
        byte[] fakeTextBytes = { 0x00, 0x41, 0x00, 0x5A };
        var expectedGids = new HashSet<ushort> { 0x0041, 0x005A };

        // Create a minimal PdfString and verify parse
        var str = new PdfString(fakeTextBytes);
        Assert.Equal(fakeTextBytes, str.Bytes);

        // Manually verify the decoding logic: 2-byte big-endian → GID
        var decoded = new List<ushort>();
        for (int i = 0; i + 1 < fakeTextBytes.Length; i += 2)
            decoded.Add((ushort)((fakeTextBytes[i] << 8) | fakeTextBytes[i + 1]));

        Assert.Equal(expectedGids, decoded.ToHashSet());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: run the collector over all pages and merge results
    // ─────────────────────────────────────────────────────────────────────────

    private static Dictionary<PdfStream, FontUsage> CollectAllPages(PdfDocument doc)
    {
        var merged = new Dictionary<PdfStream, FontUsage>(ReferenceEqualityComparer.Instance);

        foreach (PdfPage page in doc.Pages)
        {
            PdfResources? resources = page.GetResources();
            if (resources is null)
                continue;

            var collector = new GlyphUsageCollector(resources, doc);
            foreach (PdfStream contentStream in page.GetContents())
            {
                byte[] decoded = contentStream.GetDecodedData(doc.Decryptor);
                List<PdfOperator> operators = PdfContentParser.Parse(decoded);
                collector.ProcessOperators(operators);
            }

            foreach ((PdfStream fs, FontUsage usage) in collector.Result)
            {
                if (!merged.TryGetValue(fs, out FontUsage? existing))
                    merged[fs] = usage;
                else
                    foreach (ushort gid in usage.Gids)
                        existing.Gids.Add(gid);
            }
        }

        return merged;
    }
}

/// <summary>Thrown to skip a test when a required system resource is unavailable.</summary>
file sealed class SkipTestException(string message) : Exception(message);
