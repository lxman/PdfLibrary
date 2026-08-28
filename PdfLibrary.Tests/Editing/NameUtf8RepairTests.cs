using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing;

public sealed class NameUtf8RepairTests
{
    private static readonly string BadName =
        "PANTONE 172 C _" + new string([(char)0xB8, (char)0xB1, (char)0xB1, (char)0xBE]);

    private const string Replacement = "PANTONE 172 C _~B8~B1~B1~BE";

    private sealed class Fixture : IDisposable
    {
        public required PdfDocument Document { get; init; }
        public required PdfArray Separation { get; init; }
        public required PdfObject Alternate { get; init; }
        public required PdfObject TintTransform { get; init; }
        public required IReadOnlyList<PdfDictionary> Pages { get; init; }
        public void Dispose() => Document.Dispose();
    }

    private static PdfName N(string value) => new(value);
    private static PdfIndirectReference Ref(int number) => new(number, 0);

    private static Fixture Build(int consumers = 1, bool directArray = false)
    {
        var document = new PdfDocument();
        var alternate = Ref(35);
        var tint = new PdfDictionary
        {
            [N("FunctionType")] = new PdfInteger(2),
            [N("Domain")] = new PdfArray(new PdfInteger(0), new PdfInteger(1)),
            [N("C0")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)),
            [N("C1")] = new PdfArray(new PdfInteger(0), new PdfInteger(1), new PdfInteger(1), new PdfInteger(0)),
            [N("N")] = new PdfInteger(1),
        };
        var separation = new PdfArray(N("Separation"), N(BadName), alternate, tint);
        if (!directArray)
            document.AddObject(73, 0, separation);
        document.AddObject(35, 0, new PdfArray(N("ICCBased"), Ref(36)));
        document.AddObject(36, 0, new PdfStream(new PdfDictionary { [N("N")] = new PdfInteger(4) }, [0, 0, 0, 0]));

        var pageRefs = new PdfArray();
        var pages = new List<PdfDictionary>();
        for (var i = 0; i < consumers; i++)
        {
            int pageNumber = 10 + i;
            var page = new PdfDictionary
            {
                [N("Type")] = N("Page"),
                [N("Parent")] = Ref(2),
                [N("MediaBox")] = new PdfArray(
                    new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100)),
                [N("Resources")] = new PdfDictionary
                {
                    [N("ColorSpace")] = new PdfDictionary
                    {
                        [N(i == consumers - 1 ? "CS0" : "CS1")] = directArray ? separation : Ref(73),
                    },
                },
            };
            document.AddObject(pageNumber, 0, page);
            pageRefs.Add(Ref(pageNumber));
            pages.Add(page);
        }

        document.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = pageRefs, [N("Count")] = new PdfInteger(consumers),
        });
        document.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        document.Trailer.Dictionary[N("Root")] = Ref(1);

        return new Fixture
        {
            Document = document,
            Separation = separation,
            Alternate = alternate,
            TintTransform = tint,
            Pages = pages,
        };
    }

    private static Finding[] Findings(PdfDocument document) =>
        [.. new NameUtf8Rule().Check(new ConformanceContext(document, ConformanceProfile.PdfA2b))];

    [Fact]
    public void One_indirect_page_resource_Separation_name_is_a_candidate()
    {
        using Fixture fixture = Build(consumers: 6);

        NameUtf8RepairPreview preview = new PdfDocumentEditor(fixture.Document).PreviewNameUtf8Repair();

        NameUtf8RepairCandidate candidate = Assert.IsType<NameUtf8RepairCandidate>(preview.Candidate);
        Assert.Empty(preview.Refused);
        Assert.Equal(73, candidate.ObjectNumber);
        Assert.Equal(1, candidate.ArrayIndex);
        Assert.Equal("50414E544F4E45203137322043205FB8B1B1BE", candidate.OriginalBytesHex);
        Assert.Equal(Replacement, candidate.ReplacementName);
        Assert.Equal(6, candidate.ConsumerCount);
    }

    [Fact]
    public void Repair_changes_only_the_shared_colourant_value_and_round_trips_idempotently()
    {
        using Fixture fixture = Build(consumers: 6);
        PdfObject family = fixture.Separation[0];
        PdfObject alternate = fixture.Separation[2];
        PdfObject tint = fixture.Separation[3];
        PdfObject[] consumerRefs =
        [
            .. fixture.Pages.Select(page =>
                ((PdfDictionary)((PdfDictionary)page.Get("Resources")!).Get("ColorSpace")!).Values.Single())
        ];
        var editor = new PdfDocumentEditor(fixture.Document);

        NameUtf8RepairReport report = editor.RepairNameUtf8();

        NameUtf8Repair repair = Assert.IsType<NameUtf8Repair>(report.Repaired);
        Assert.Empty(report.Refused);
        Assert.Equal(Replacement, Assert.IsType<PdfName>(fixture.Separation[1]).Value);
        Assert.Same(family, fixture.Separation[0]);
        Assert.Same(alternate, fixture.Separation[2]);
        Assert.Same(tint, fixture.Separation[3]);
        Assert.Same(fixture.Alternate, fixture.Separation[2]);
        Assert.Same(fixture.TintTransform, fixture.Separation[3]);
        Assert.All(fixture.Pages.Select((page, index) => (page, index)), item =>
        {
            PdfObject current = ((PdfDictionary)((PdfDictionary)item.page.Get("Resources")!)
                .Get("ColorSpace")!).Values.Single();
            Assert.Same(consumerRefs[item.index], current);
        });
        Assert.Empty(Findings(fixture.Document));
        Assert.Null(editor.RepairNameUtf8().Repaired);

        using var output = new MemoryStream();
        editor.Save(output);
        using PdfDocument reopened = PdfDocument.Load(new MemoryStream(output.ToArray()));
        Assert.Empty(Findings(reopened));
        Assert.Null(new PdfDocumentEditor(reopened).RepairNameUtf8().Repaired);
    }

    [Fact]
    public void Direct_Separation_array_is_refused()
    {
        using Fixture fixture = Build(directArray: true);

        NameUtf8RepairPreview preview = new PdfDocumentEditor(fixture.Document).PreviewNameUtf8Repair();

        Assert.Null(preview.Candidate);
        Assert.Contains("indirect", Assert.Single(preview.Refused).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BadName, Assert.IsType<PdfName>(fixture.Separation[1]).Value);
    }

    [Fact]
    public void Invalid_dictionary_key_is_refused()
    {
        using Fixture fixture = Build();
        fixture.Separation[1] = N("Valid");
        ((PdfDictionary)fixture.Document.Objects[1])[N(BadName)] = PdfBoolean.True;

        NameUtf8RepairPreview preview = new PdfDocumentEditor(fixture.Document).PreviewNameUtf8Repair();

        Assert.Null(preview.Candidate);
        Assert.Contains("dictionary key", Assert.Single(preview.Refused).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repeated_or_independent_invalid_names_are_refused()
    {
        using Fixture fixture = Build();
        ((PdfDictionary)fixture.Document.Objects[1])[N("Other")] = N(BadName);

        NameUtf8RepairPreview preview = new PdfDocumentEditor(fixture.Document).PreviewNameUtf8Repair();

        Assert.Null(preview.Candidate);
        Assert.Contains("2 invalid", Assert.Single(preview.Refused).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Consumer_outside_a_page_colour_space_resource_is_refused()
    {
        using Fixture fixture = Build();
        ((PdfDictionary)fixture.Document.Objects[1])[N("Custom")] = Ref(73);

        NameUtf8RepairPreview preview = new PdfDocumentEditor(fixture.Document).PreviewNameUtf8Repair();

        Assert.Null(preview.Candidate);
        Assert.Contains("consumer path", Assert.Single(preview.Refused).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deterministic_replacement_collision_is_refused()
    {
        using Fixture fixture = Build();
        ((PdfDictionary)fixture.Document.Objects[1])[N("Existing")] = N(Replacement);

        NameUtf8RepairPreview preview = new PdfDocumentEditor(fixture.Document).PreviewNameUtf8Repair();

        Assert.Null(preview.Candidate);
        Assert.Contains("already exists", Assert.Single(preview.Refused).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Signed_and_DocMDP_documents_are_refused(bool signed, bool docMdp)
    {
        using Fixture fixture = Build();
        PdfDictionary catalog = (PdfDictionary)fixture.Document.Objects[1];
        if (docMdp)
            catalog[N("Perms")] = new PdfDictionary { [N("DocMDP")] = Ref(90) };
        if (signed || docMdp)
            fixture.Document.AddObject(90, 0, new PdfDictionary
            {
                [N("Type")] = N("Sig"),
                [N("ByteRange")] = new PdfArray(
                    new PdfInteger(0), new PdfInteger(10), new PdfInteger(20), new PdfInteger(10)),
                [N("Contents")] = PdfString.FromText("signature"),
            });

        NameUtf8RepairPreview preview = new PdfDocumentEditor(fixture.Document).PreviewNameUtf8Repair();

        Assert.Null(preview.Candidate);
        Assert.Contains("signature", Assert.Single(preview.Refused).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Direct_nested_signature_dictionary_is_refused()
    {
        using Fixture fixture = Build();
        ((PdfDictionary)fixture.Document.Objects[1])[N("AcroForm")] = new PdfDictionary
        {
            [N("Fields")] = new PdfArray(new PdfDictionary
            {
                [N("FT")] = N("Sig"),
                [N("V")] = new PdfDictionary
                {
                    [N("Type")] = N("Sig"),
                    [N("ByteRange")] = new PdfArray(
                        new PdfInteger(0), new PdfInteger(10), new PdfInteger(20), new PdfInteger(10)),
                    [N("Contents")] = PdfString.FromText("signature"),
                },
            }),
        };

        NameUtf8RepairPreview preview = new PdfDocumentEditor(fixture.Document).PreviewNameUtf8Repair();

        Assert.Null(preview.Candidate);
        Assert.Contains("signature", Assert.Single(preview.Refused).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repair_reclassifies_live_state_and_refuses_preview_drift()
    {
        using Fixture fixture = Build();
        var editor = new PdfDocumentEditor(fixture.Document);
        Assert.NotNull(editor.PreviewNameUtf8Repair().Candidate);
        ((PdfDictionary)fixture.Document.Objects[1])[N("Other")] = N(BadName);

        NameUtf8RepairReport report = editor.RepairNameUtf8();

        Assert.Null(report.Repaired);
        Assert.Single(report.Refused);
        Assert.Equal(BadName, Assert.IsType<PdfName>(fixture.Separation[1]).Value);
    }
}
