using System.Security.Cryptography;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Rendering.SkiaSharp;
using PdfLibrary.Structure;
using SkiaSharp;

namespace PdfLibrary.Tests.Editing;

[Trait("Category", "LocalOnly")]
public sealed class InlineImageRepairCorpusTests
{
    private const string Ford = @"D:\PdfCorpora\real-world\local-708\Ford_Edge_2012.pdf";

    [Fact]
    public void Ford_repair_preserves_all_91_binary_payloads_and_closes_the_rule_after_reload()
    {
        Assert.SkipWhen(!File.Exists(Ford), $"Ford corpus witness not present at {Ford}");
        string output = Path.Combine(Path.GetTempPath(), $"pellucid-inline-image-{Guid.NewGuid():N}.pdf");

        try
        {
            using (PdfDocument document = PdfDocument.Load(Ford))
            using (var editor = document.Edit())
            {
                Assert.Equal(5640, document.PageCount);
                InlineImageRepairPreview preview = editor.PreviewInlineImageRepairs();
                Assert.Equal(50, preview.Candidates.Count);
                Assert.Equal(91, preview.Candidates.Sum(candidate => candidate.ImageCount));
                Assert.Empty(preview.Refused);
                int[] affectedPages = preview.Candidates.SelectMany(candidate => candidate.PageNumbers)
                    .Distinct().Order().ToArray();
                Assert.Equal(50, affectedPages.Length);
                Dictionary<int, byte[]> renderHashesBefore = affectedPages.ToDictionary(
                    pageNumber => pageNumber,
                    pageNumber => RenderHash(document, pageNumber));

                Dictionary<int, byte[]> decodedBefore = preview.Candidates.ToDictionary(
                    candidate => candidate.ObjectNumber,
                    candidate => ((PdfStream)document.GetObject(candidate.ObjectNumber)!)
                        .GetDecodedData(document.Decryptor));
                Dictionary<int, List<byte[]>> payloadsBefore = decodedBefore.ToDictionary(
                    pair => pair.Key,
                    pair => Payloads(pair.Value));

                InlineImageRepairReport report = editor.RepairInlineImages();
                Assert.Equal(50, report.Applied.Count);
                Assert.Equal(91, report.Applied.Sum(applied => applied.ImageCount));
                Assert.Empty(report.Refused);

                foreach ((int objectNumber, byte[] before) in decodedBefore)
                {
                    var stream = (PdfStream)document.GetObject(objectNumber)!;
                    byte[] after = stream.GetDecodedData(document.Decryptor);
                    Assert.Equal(ReplaceAll(before, "/I true"u8, "/I false"u8), after);
                    AssertPayloadsEqual(payloadsBefore[objectNumber], Payloads(after));
                }
                foreach (int pageNumber in affectedPages)
                    Assert.Equal(renderHashesBefore[pageNumber], RenderHash(document, pageNumber));

                Assert.Empty(InlineFindings(document));
                Assert.Empty(editor.PreviewInlineImageRepairs().Candidates);
                editor.Save(output);
            }

            using PdfDocument reloaded = PdfDocument.Load(output);
            using var reloadedEditor = reloaded.Edit();
            Assert.Equal(5640, reloaded.PageCount);
            Assert.Empty(InlineFindings(reloaded));
            Assert.Empty(reloadedEditor.PreviewInlineImageRepairs().Candidates);
            Assert.Empty(reloadedEditor.PreviewInlineImageRepairs().Refused);
            Assert.Empty(reloadedEditor.RepairInlineImages().Applied);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    private static Finding[] InlineFindings(PdfDocument document) => new InlineImageRule()
        .Check(new ConformanceContext(document, ConformanceProfile.PdfA2b)).ToArray();

    private static List<byte[]> Payloads(byte[] decoded) => PdfContentParser.Parse(decoded)
        .OfType<InlineImageOperator>()
        .Select(image => image.ImageData)
        .ToList();

    private static void AssertPayloadsEqual(IReadOnlyList<byte[]> expected, IReadOnlyList<byte[]> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
            Assert.Equal(expected[i], actual[i]);
    }

    private static byte[] ReplaceAll(byte[] source, ReadOnlySpan<byte> oldValue, ReadOnlySpan<byte> newValue)
    {
        var offsets = new List<int>();
        for (var at = 0; at <= source.Length - oldValue.Length;)
        {
            int relative = source.AsSpan(at).IndexOf(oldValue);
            if (relative < 0) break;
            at += relative;
            offsets.Add(at);
            at += oldValue.Length;
        }

        var result = new byte[source.Length + offsets.Count * (newValue.Length - oldValue.Length)];
        var sourceAt = 0;
        var targetAt = 0;
        foreach (int offset in offsets)
        {
            int length = offset - sourceAt;
            source.AsSpan(sourceAt, length).CopyTo(result.AsSpan(targetAt));
            targetAt += length;
            newValue.CopyTo(result.AsSpan(targetAt));
            targetAt += newValue.Length;
            sourceAt = offset + oldValue.Length;
        }
        source.AsSpan(sourceAt).CopyTo(result.AsSpan(targetAt));
        return result;
    }

    private static byte[] RenderHash(PdfDocument document, int pageNumber)
    {
        using SKImage image = document.GetPage(pageNumber - 1)!.RenderTo().ToImage();
        using SKBitmap bitmap = SKBitmap.FromImage(image);
        return SHA256.HashData(bitmap.Bytes);
    }
}
