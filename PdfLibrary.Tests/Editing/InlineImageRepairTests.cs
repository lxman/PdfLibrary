using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing;

public sealed class InlineImageRepairTests
{
    private static PdfName N(string value) => new(value);
    private static PdfIndirectReference Ref(int number) => new(number, 0);
    private static byte[] A(string value) => Encoding.ASCII.GetBytes(value);

    private static PdfDocument OnePage(
        byte[] content,
        string? containerFilter = "FlateDecode",
        bool indirectContent = true,
        bool decodeParms = false)
    {
        var document = new PdfDocument();
        var stream = new PdfStream(new PdfDictionary(), content);
        if (containerFilter is not null)
            stream.SetEncodedData(content, containerFilter);
        if (decodeParms)
            stream.Dictionary[N("DecodeParms")] = new PdfDictionary { [N("Predictor")] = new PdfInteger(1) };

        PdfObject contents;
        if (indirectContent)
        {
            document.AddObject(4, 0, stream);
            contents = Ref(4);
        }
        else
        {
            contents = stream;
        }

        AddPageTree(document, contents, new PdfDictionary());
        return document;
    }

    private static void AddPageTree(PdfDocument document, PdfObject contents, PdfDictionary resources)
    {
        document.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2), [N("Contents")] = contents,
            [N("Resources")] = resources, [N("MediaBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100)),
        });
        document.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        document.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        document.Trailer.Dictionary[N("Root")] = Ref(1);
    }

    private static byte[] Inline(byte[] payload, string interpolate = "/I true", string extra = "")
    {
        byte[] head = A($"q BI /W 1 /H 1 /BPC 8 /CS /RGB {interpolate} {extra} ID\n");
        byte[] tail = A("\nEI Q");
        return [.. head, .. payload, .. tail];
    }

    private static PdfStream Content(PdfDocument document) => document.GetPage(0)!.GetContents().Single();

    private static List<byte[]> Payloads(byte[] decoded) => PdfContentParser.Parse(decoded)
        .OfType<InlineImageOperator>().Select(image => image.ImageData).ToList();

    private static Finding[] Findings(PdfDocument document) => new InlineImageRule()
        .Check(new ConformanceContext(document, ConformanceProfile.PdfA2b)).ToArray();

    [Fact]
    public void Repair_changes_only_true_tokens_and_preserves_binary_payloads()
    {
        byte[] payload1 = [0, 1, 2, (byte)'E', (byte)'I', 3, (byte)'(', (byte)')', 255];
        byte[] payload2 = [17, (byte)'x', (byte)'E', (byte)'I', (byte)'y', 0, 254];
        byte[] before = [.. Inline(payload1), (byte)'\n', .. Inline(payload2, "/Interpolate true")];
        using PdfDocument document = OnePage(before);
        using var editor = document.Edit();

        InlineImageRepairCandidate candidate = Assert.Single(editor.PreviewInlineImageRepairs().Candidates);
        Assert.Equal(4, candidate.ObjectNumber);
        Assert.Equal([1], candidate.PageNumbers);
        Assert.Equal(2, candidate.ImageCount);
        Assert.Empty(editor.PreviewInlineImageRepairs().Refused);
        List<byte[]> payloadsBefore = Payloads(before);

        InlineImageRepair applied = Assert.Single(editor.RepairInlineImages().Applied);
        Assert.Equal(2, applied.ImageCount);
        byte[] after = Content(document).GetDecodedData(document.Decryptor);
        byte[] expected = Encoding.Latin1.GetBytes(
            Encoding.Latin1.GetString(before).Replace("true", "false", StringComparison.Ordinal));
        Assert.Equal(expected, after);
        Assert.Equal(payloadsBefore, Payloads(after), ByteArrayListComparer.Instance);
        Assert.Empty(Findings(document));
        Assert.Empty(editor.PreviewInlineImageRepairs().Candidates);
        Assert.Empty(editor.RepairInlineImages().Applied);
    }

    [Fact]
    public void Repair_survives_save_reload_and_repreflight()
    {
        using PdfDocument document = OnePage(Inline([1, 2, 3, 4]));
        using var editor = document.Edit();
        editor.RepairInlineImages();
        using var output = new MemoryStream();
        editor.Save(output);
        output.Position = 0;

        using PdfDocument reloaded = PdfDocument.Load(output, leaveOpen: true);
        using var reloadedEditor = reloaded.Edit();
        Assert.Empty(Findings(reloaded));
        Assert.Empty(reloadedEditor.PreviewInlineImageRepairs().Candidates);
        Assert.Contains("/I false", Encoding.ASCII.GetString(Content(reloaded).GetDecodedData(reloaded.Decryptor)));
    }

    [Theory]
    [InlineData(null, true, false, "not encoded")]
    [InlineData("FlateDecode", false, false, "direct")]
    [InlineData("FlateDecode", true, true, "DecodeParms")]
    public void Unsupported_container_shapes_are_refused(
        string? filter,
        bool indirect,
        bool decodeParms,
        string reasonFragment)
    {
        using PdfDocument document = OnePage(Inline([1, 2, 3]), filter, indirect, decodeParms);
        using var editor = document.Edit();

        InlineImageRepairPreview preview = editor.PreviewInlineImageRepairs();
        Assert.Empty(preview.Candidates);
        Assert.Contains(preview.Refused, refusal =>
            refusal.Reason.Contains(reasonFragment, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(editor.RepairInlineImages().Applied);
        Assert.Single(Findings(document));
    }

    [Fact]
    public void Duplicate_interpolation_aliases_are_refused()
    {
        using PdfDocument document = OnePage(Inline([1, 2, 3], "/I true /Interpolate true"));
        using var editor = document.Edit();

        InlineImageRepairPreview preview = editor.PreviewInlineImageRepairs();
        Assert.Empty(preview.Candidates);
        Assert.Contains(preview.Refused, refusal => refusal.Reason.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Forbidden_inline_filter_is_reported_but_payload_is_not_transcoded()
    {
        using PdfDocument document = OnePage(Inline([1, 2, 3], extra: "/F /LZWDecode"));
        using var editor = document.Edit();

        InlineImageRepairPreview preview = editor.PreviewInlineImageRepairs();
        Assert.Single(preview.Candidates);
        Assert.Contains(preview.Refused, refusal => refusal.Reason.Contains("transcode", StringComparison.OrdinalIgnoreCase));
        InlineImageRepairReport report = editor.RepairInlineImages();
        Assert.Single(report.Applied);
        Assert.Contains(Findings(document), finding => finding.Clause.Contains("6.1.10", StringComparison.Ordinal));
    }

    [Fact]
    public void Invoked_form_violation_is_refused_as_unowned()
    {
        var document = new PdfDocument();
        byte[] page = A("q /Fm0 Do Q");
        var pageStream = new PdfStream(new PdfDictionary(), page);
        pageStream.SetEncodedData(page, "FlateDecode");
        document.AddObject(4, 0, pageStream);
        byte[] form = Inline([1, 2, 3]);
        var formDictionary = new PdfDictionary
        {
            [N("Type")] = N("XObject"), [N("Subtype")] = N("Form"),
            [N("BBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(10), new PdfInteger(10)),
        };
        var formStream = new PdfStream(formDictionary, form);
        formStream.SetEncodedData(form, "FlateDecode");
        document.AddObject(10, 0, formStream);
        AddPageTree(document, Ref(4), new PdfDictionary
        {
            [N("XObject")] = new PdfDictionary { [N("Fm0")] = Ref(10) },
        });
        using (document)
        using (var editor = document.Edit())
        {
            InlineImageRepairPreview preview = editor.PreviewInlineImageRepairs();
            Assert.Empty(preview.Candidates);
            Assert.Contains(preview.Refused, refusal => refusal.Reason.Contains("Form", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Inline_image_split_across_page_streams_is_refused()
    {
        var document = new PdfDocument();
        byte[] first = A("q BI /W 1 /H 1 /BPC 8 /CS /RGB /I true");
        byte[] second = A("ID\nabc\nEI Q");
        var firstStream = new PdfStream(new PdfDictionary(), first);
        var secondStream = new PdfStream(new PdfDictionary(), second);
        firstStream.SetEncodedData(first, "FlateDecode");
        secondStream.SetEncodedData(second, "FlateDecode");
        document.AddObject(4, 0, firstStream);
        document.AddObject(5, 0, secondStream);
        AddPageTree(document, new PdfArray(Ref(4), Ref(5)), new PdfDictionary());

        using (document)
        using (var editor = document.Edit())
        {
            Assert.Single(Findings(document));
            InlineImageRepairPreview preview = editor.PreviewInlineImageRepairs();
            Assert.Empty(preview.Candidates);
            Assert.Contains(preview.Refused, refusal =>
                refusal.Reason.Contains("stream boundary", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Signature_protection_refuses_all_candidates()
    {
        using PdfDocument document = OnePage(Inline([1, 2, 3]));
        document.AddObject(8, 0, new PdfDictionary
        {
            [N("Type")] = N("Sig"), [N("ByteRange")] = new PdfArray(new PdfInteger(0), new PdfInteger(1)),
        });
        using var editor = document.Edit();

        InlineImageRepairPreview preview = editor.PreviewInlineImageRepairs();
        Assert.Empty(preview.Candidates);
        Assert.Contains(preview.Refused, refusal => refusal.Reason.Contains("signed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Conforming_signed_document_does_not_invent_a_refusal()
    {
        using PdfDocument document = OnePage(Inline([1, 2, 3], "/I false"));
        document.AddObject(8, 0, new PdfDictionary
        {
            [N("Type")] = N("Sig"), [N("ByteRange")] = new PdfArray(new PdfInteger(0), new PdfInteger(1)),
        });
        using var editor = document.Edit();

        InlineImageRepairPreview preview = editor.PreviewInlineImageRepairs();
        Assert.Empty(preview.Candidates);
        Assert.Empty(preview.Refused);
    }

    [Fact]
    public void Selector_is_honoured_and_live_drift_is_refused()
    {
        using PdfDocument document = OnePage(Inline([1, 2, 3]));
        using var editor = document.Edit();

        Assert.Empty(editor.RepairInlineImages(new HashSet<int> { 99 }).Applied);
        Assert.Contains(editor.RepairInlineImages(new HashSet<int> { 99 }).Refused, refusal => refusal.ObjectNumber == 99);
        Assert.Single(editor.PreviewInlineImageRepairs().Candidates);
        Assert.Single(editor.RepairInlineImages(new HashSet<int> { 4 }).Applied);
    }

    private sealed class ByteArrayListComparer : IEqualityComparer<List<byte[]>>
    {
        public static ByteArrayListComparer Instance { get; } = new();

        public bool Equals(List<byte[]>? x, List<byte[]>? y) =>
            x is not null && y is not null && x.Count == y.Count
            && x.Zip(y).All(pair => pair.First.SequenceEqual(pair.Second));

        public int GetHashCode(List<byte[]> obj) => obj.Count;
    }
}
