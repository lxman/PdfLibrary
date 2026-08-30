using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Saved-output witnesses for PDF/A-2 clause 6.2.4.4. These tests deliberately exercise ordinary full
/// rewrites before any repair policy is chosen: a save must not be mistaken for a fix, and malformed
/// <c>/Colorants</c> values must not be silently normalised.
/// </summary>
public class PdfxNChannelColorantsSaveBehaviorTests
{
    private enum ColorantsShape
    {
        Missing,
        EmptyDictionary,
        WrongType,
        CompleteDictionary,
    }

    private static PdfName N(string value) => new(value);
    private static PdfIndirectReference Ref(int number) => new(number, 0);

    private static PdfArray TintFunction() => new(
        new PdfDictionary
        {
            [N("FunctionType")] = new PdfInteger(2),
            [N("Domain")] = new PdfArray(new PdfInteger(0), new PdfInteger(1)),
            [N("C0")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)),
            [N("C1")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(1), new PdfInteger(0), new PdfInteger(0)),
            [N("N")] = new PdfInteger(1),
        });

    private static Finding[] Findings(PdfDocument document) =>
        [.. new PdfxNChannelColorantsRule().Check(
            new ConformanceContext(document, ConformanceProfile.PdfA2b))];

    private static PdfDocument Document(
        ColorantsShape shape,
        bool indirectAttributes,
        bool sharedDeviceN = false,
        bool duplicateColorant = false,
        int availableSeparations = 0,
        bool directSeparation = false,
        bool omitAttributes = false,
        bool signed = false,
        bool nChannelSubtype = true,
        bool directDeviceN = false,
        bool docMdp = false)
    {
        var document = new PdfDocument();
        var attributes = new PdfDictionary();
        if (nChannelSubtype)
            attributes[N("Subtype")] = N("NChannel");
        var separationResources = new List<PdfObject>();

        switch (shape)
        {
            case ColorantsShape.EmptyDictionary:
                attributes[N("Colorants")] = new PdfDictionary();
                break;
            case ColorantsShape.WrongType:
                attributes[N("Colorants")] = new PdfString(Encoding.ASCII.GetBytes("vendor colorants"));
                break;
            case ColorantsShape.CompleteDictionary:
                document.AddObject(13, 0, new PdfArray(
                    N("Separation"), N("Spot1"), N("DeviceCMYK"), TintFunction()));
                attributes[N("Colorants")] = new PdfDictionary { [N("Spot1")] = Ref(13) };
                separationResources.Add(Ref(13));
                break;
        }

        for (int index = 0; index < availableSeparations; index++)
        {
            int objectNumber = 13 + index;
            document.AddObject(objectNumber, 0, new PdfArray(
                N("Separation"), N("Spot1"), N("DeviceCMYK"), TintFunction()));
            separationResources.Add(Ref(objectNumber));
        }
        if (directSeparation)
            separationResources.Add(new PdfArray(
                N("Separation"), N("Spot1"), N("DeviceCMYK"), TintFunction()));

        PdfObject attributesValue = attributes;
        if (indirectAttributes)
        {
            document.AddObject(11, 0, attributes);
            attributesValue = Ref(11);
        }

        var names = duplicateColorant
            ? new PdfArray(N("Spot1"), N("Spot1"))
            : new PdfArray(N("Spot1"));
        PdfArray deviceN = omitAttributes
            ? new PdfArray(N("DeviceN"), names, N("DeviceCMYK"), TintFunction())
            : new PdfArray(N("DeviceN"), names, N("DeviceCMYK"), TintFunction(), attributesValue);
        if (!directDeviceN)
            document.AddObject(10, 0, deviceN);

        var colorSpaces = new PdfDictionary { [N("CS1")] = directDeviceN ? deviceN : Ref(10) };
        if (sharedDeviceN)
            colorSpaces[N("CS2")] = directDeviceN ? deviceN : Ref(10);
        for (int index = 0; index < separationResources.Count; index++)
            colorSpaces[N($"Spot{index + 1}")] = separationResources[index];

        document.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100)),
            [N("Resources")] = new PdfDictionary { [N("ColorSpace")] = colorSpaces },
        });
        document.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        var catalog = new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) };
        if (signed)
        {
            document.AddObject(20, 0, new PdfDictionary
            {
                [N("FT")] = N("Sig"), [N("V")] = new PdfDictionary(),
            });
            catalog[N("AcroForm")] = new PdfDictionary { [N("Fields")] = new PdfArray(Ref(20)) };
        }
        if (docMdp)
            catalog[N("Perms")] = new PdfDictionary { [N("DocMDP")] = new PdfDictionary() };
        document.AddObject(1, 0, catalog);
        document.Trailer.Dictionary[N("Root")] = Ref(1);
        return document;
    }

    private static PdfDocument SaveAndReopen(PdfDocument source)
    {
        using var saved = new MemoryStream();
        new PdfDocumentEditor(source).Save(saved);
        return PdfDocument.Load(new MemoryStream(saved.ToArray()));
    }

    private static (PdfArray Space, PdfDictionary Attributes) DeviceN(PdfDocument document)
    {
        PdfArray space = Assert.IsType<PdfArray>(document.GetObject(10));
        PdfObject rawAttributes = space[4];
        PdfDictionary attributes = Assert.IsType<PdfDictionary>(rawAttributes is PdfIndirectReference reference
            ? document.GetObject(reference.ObjectNumber)
            : rawAttributes);
        return (space, attributes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plain_full_rewrite_preserves_missing_colorants_and_attribute_indirection(bool indirectAttributes)
    {
        using PdfDocument source = Document(ColorantsShape.Missing, indirectAttributes);
        using PdfDocument reopened = SaveAndReopen(source);

        Finding finding = Assert.Single(Findings(reopened));
        Assert.Contains("Spot1", finding.Message);
        (PdfArray space, PdfDictionary attributes) = DeviceN(reopened);
        Assert.Equal(indirectAttributes, space[4] is PdfIndirectReference);
        Assert.Null(attributes.Get("Colorants"));
    }

    [Fact]
    public void Plain_full_rewrite_preserves_empty_colorants_dictionary()
    {
        using PdfDocument source = Document(ColorantsShape.EmptyDictionary, indirectAttributes: false);
        using PdfDocument reopened = SaveAndReopen(source);

        Assert.Single(Findings(reopened));
        PdfDictionary colorants = Assert.IsType<PdfDictionary>(DeviceN(reopened).Attributes.Get("Colorants"));
        Assert.Empty(colorants);
    }

    [Fact]
    public void Plain_full_rewrite_preserves_wrong_type_colorants_value()
    {
        using PdfDocument source = Document(ColorantsShape.WrongType, indirectAttributes: true);
        using PdfDocument reopened = SaveAndReopen(source);

        Assert.Single(Findings(reopened));
        PdfString colorants = Assert.IsType<PdfString>(DeviceN(reopened).Attributes.Get("Colorants"));
        Assert.Equal("vendor colorants", Encoding.ASCII.GetString(colorants.Bytes));
    }

    [Fact]
    public void Plain_full_rewrite_preserves_a_shared_device_n_reference()
    {
        using PdfDocument source = Document(
            ColorantsShape.Missing, indirectAttributes: true, sharedDeviceN: true);
        using PdfDocument reopened = SaveAndReopen(source);

        Assert.Single(Findings(reopened));
        PdfDictionary page = Assert.Single(reopened.GetPages()).Dictionary;
        PdfDictionary resources = Assert.IsType<PdfDictionary>(page.Get("Resources"));
        PdfDictionary colorSpaces = Assert.IsType<PdfDictionary>(resources.Get("ColorSpace"));
        Assert.Equal(10, Assert.IsType<PdfIndirectReference>(colorSpaces.Get("CS1")).ObjectNumber);
        Assert.Equal(10, Assert.IsType<PdfIndirectReference>(colorSpaces.Get("CS2")).ObjectNumber);
    }

    [Fact]
    public void Duplicate_colorant_names_remain_duplicate_but_are_reported_once_after_save()
    {
        using PdfDocument source = Document(
            ColorantsShape.Missing, indirectAttributes: false, duplicateColorant: true);
        using PdfDocument reopened = SaveAndReopen(source);

        Assert.Single(Findings(reopened));
        PdfArray names = Assert.IsType<PdfArray>(DeviceN(reopened).Space[1]);
        Assert.Equal(2, names.Count);
        Assert.All(names, value => Assert.Equal("Spot1", Assert.IsType<PdfName>(value).Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Complete_colorants_dictionary_and_separation_reference_survive_save(bool indirectAttributes)
    {
        using PdfDocument source = Document(ColorantsShape.CompleteDictionary, indirectAttributes);
        using PdfDocument reopened = SaveAndReopen(source);

        Assert.Empty(Findings(reopened));
        PdfDictionary colorants = Assert.IsType<PdfDictionary>(DeviceN(reopened).Attributes.Get("Colorants"));
        PdfIndirectReference separationReference = Assert.IsType<PdfIndirectReference>(colorants.Get("Spot1"));
        Assert.Equal(13, separationReference.ObjectNumber);
        PdfArray separation = Assert.IsType<PdfArray>(reopened.GetObject(13));
        Assert.Equal("Separation", Assert.IsType<PdfName>(separation[0]).Value);
        Assert.Equal("Spot1", Assert.IsType<PdfName>(separation[1]).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repair_links_the_one_existing_indirect_separation_and_round_trips(bool indirectAttributes)
    {
        using PdfDocument document = Document(
            ColorantsShape.Missing, indirectAttributes, availableSeparations: 1);
        var editor = new PdfDocumentEditor(document);

        NChannelColorantRepairCandidate candidate = Assert.Single(
            editor.PreviewNChannelColorantsRepair().Candidates);
        Assert.Equal("Spot1", candidate.Colorant);
        Assert.Equal(10, candidate.DeviceNObjectNumber);
        Assert.Equal(indirectAttributes ? 11 : null, candidate.AttributesObjectNumber);
        Assert.Equal(13, candidate.SeparationObjectNumber);
        Assert.False(candidate.CreatesAttributesDictionary);
        Assert.True(candidate.CreatesColorantsDictionary);

        NChannelColorantRepair repair = Assert.Single(editor.RepairNChannelColorants().Repaired);
        Assert.Equal(13, repair.SeparationObjectNumber);
        Assert.Empty(Findings(document));
        PdfDictionary colorants = Assert.IsType<PdfDictionary>(DeviceN(document).Attributes.Get("Colorants"));
        Assert.Equal(13, Assert.IsType<PdfIndirectReference>(colorants.Get("Spot1")).ObjectNumber);
        Assert.Empty(editor.RepairNChannelColorants().Repaired);

        using PdfDocument reopened = SaveAndReopen(document);
        Assert.Empty(Findings(reopened));
        PdfDictionary reopenedColorants = Assert.IsType<PdfDictionary>(DeviceN(reopened).Attributes.Get("Colorants"));
        Assert.Equal(13, Assert.IsType<PdfIndirectReference>(reopenedColorants.Get("Spot1")).ObjectNumber);
    }

    [Fact]
    public void Repair_can_add_the_optional_attributes_and_colorants_containers()
    {
        using PdfDocument document = Document(
            ColorantsShape.Missing,
            indirectAttributes: false,
            availableSeparations: 1,
            omitAttributes: true);
        var editor = new PdfDocumentEditor(document);

        NChannelColorantRepairCandidate candidate = Assert.Single(
            editor.PreviewNChannelColorantsRepair().Candidates);
        Assert.True(candidate.CreatesAttributesDictionary);
        Assert.True(candidate.CreatesColorantsDictionary);

        NChannelColorantRepair repair = Assert.Single(editor.RepairNChannelColorants().Repaired);
        Assert.True(repair.CreatedAttributesDictionary);
        Assert.True(repair.CreatedColorantsDictionary);
        Assert.Empty(Findings(document));
        Assert.Equal(5, DeviceN(document).Space.Count);
    }

    [Fact]
    public void Repair_adds_to_an_existing_empty_colorants_dictionary()
    {
        using PdfDocument document = Document(
            ColorantsShape.EmptyDictionary, indirectAttributes: false, availableSeparations: 1);
        var editor = new PdfDocumentEditor(document);

        NChannelColorantRepairCandidate candidate = Assert.Single(
            editor.PreviewNChannelColorantsRepair().Candidates);
        Assert.False(candidate.CreatesColorantsDictionary);
        NChannelColorantRepair repair = Assert.Single(editor.RepairNChannelColorants().Repaired);
        Assert.False(repair.CreatedColorantsDictionary);
        Assert.Empty(Findings(document));
    }

    [Fact]
    public void Wrong_type_colorants_refuses_without_discarding_its_payload()
    {
        using PdfDocument document = Document(
            ColorantsShape.WrongType, indirectAttributes: true, availableSeparations: 1);
        var editor = new PdfDocumentEditor(document);

        NChannelColorantsRepairPreview preview = editor.PreviewNChannelColorantsRepair();
        Assert.Empty(preview.Candidates);
        Assert.Contains("not a dictionary", Assert.Single(preview.Refused).Reason);
        Assert.Empty(editor.RepairNChannelColorants().Repaired);
        PdfString value = Assert.IsType<PdfString>(DeviceN(document).Attributes.Get("Colorants"));
        Assert.Equal("vendor colorants", Encoding.ASCII.GetString(value.Bytes));
        Assert.Single(Findings(document));
    }

    [Fact]
    public void Missing_separation_refuses_instead_of_deriving_an_individual_tint_transform()
    {
        using PdfDocument document = Document(ColorantsShape.Missing, indirectAttributes: false);
        var editor = new PdfDocumentEditor(document);

        NChannelColorantsRepairPreview preview = editor.PreviewNChannelColorantsRepair();
        Assert.Empty(preview.Candidates);
        Assert.Contains("in combination", Assert.Single(preview.Refused).Reason);
        Assert.Single(Findings(document));
    }

    [Fact]
    public void Multiple_indirect_separations_refuse_instead_of_choosing_object_identity()
    {
        using PdfDocument document = Document(
            ColorantsShape.Missing, indirectAttributes: false, availableSeparations: 2);
        var editor = new PdfDocumentEditor(document);

        NChannelColorantsRepairPreview preview = editor.PreviewNChannelColorantsRepair();
        Assert.Empty(preview.Candidates);
        Assert.Contains("More than one", Assert.Single(preview.Refused).Reason);
        Assert.Single(Findings(document));
    }

    [Fact]
    public void Direct_separation_refuses_instead_of_being_cloned()
    {
        using PdfDocument document = Document(
            ColorantsShape.Missing, indirectAttributes: false, directSeparation: true);
        var editor = new PdfDocumentEditor(document);

        NChannelColorantsRepairPreview preview = editor.PreviewNChannelColorantsRepair();
        Assert.Empty(preview.Candidates);
        Assert.Contains("direct or unreachable", Assert.Single(preview.Refused).Reason);
        Assert.Single(Findings(document));
    }

    [Fact]
    public void Shared_device_n_is_repaired_once_and_both_consumers_keep_the_same_object()
    {
        using PdfDocument document = Document(
            ColorantsShape.Missing,
            indirectAttributes: true,
            sharedDeviceN: true,
            availableSeparations: 1);
        var editor = new PdfDocumentEditor(document);

        Assert.Single(editor.PreviewNChannelColorantsRepair().Candidates);
        Assert.Single(editor.RepairNChannelColorants().Repaired);
        Assert.Empty(Findings(document));

        PdfDictionary page = Assert.Single(document.GetPages()).Dictionary;
        PdfDictionary resources = Assert.IsType<PdfDictionary>(page.Get("Resources"));
        PdfDictionary colorSpaces = Assert.IsType<PdfDictionary>(resources.Get("ColorSpace"));
        Assert.Equal(10, Assert.IsType<PdfIndirectReference>(colorSpaces.Get("CS1")).ObjectNumber);
        Assert.Equal(10, Assert.IsType<PdfIndirectReference>(colorSpaces.Get("CS2")).ObjectNumber);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Signature_or_doc_mdp_refuses_without_writing(bool signed, bool docMdp)
    {
        using PdfDocument document = Document(
            ColorantsShape.Missing,
            indirectAttributes: true,
            availableSeparations: 1,
            signed: signed,
            docMdp: docMdp);
        var editor = new PdfDocumentEditor(document);

        NChannelColorantsRepairPreview preview = editor.PreviewNChannelColorantsRepair();
        Assert.Empty(preview.Candidates);
        string reason = Assert.Single(preview.Refused).Reason;
        Assert.True(reason.Contains("signature", StringComparison.OrdinalIgnoreCase)
                    || reason.Contains("DocMDP", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(editor.RepairNChannelColorants().Repaired);
        Assert.Single(Findings(document));
        Assert.Null(DeviceN(document).Attributes.Get("Colorants"));
    }

    [Fact]
    public void Pdfx_scope_skips_plain_device_n_while_pdfa_scope_can_repair_it()
    {
        using PdfDocument document = Document(
            ColorantsShape.Missing,
            indirectAttributes: false,
            availableSeparations: 1,
            nChannelSubtype: false);
        var editor = new PdfDocumentEditor(document);

        Assert.Single(editor.PreviewNChannelColorantsRepair().Candidates);
        NChannelColorantsRepairPreview pdfxPreview = editor.PreviewNChannelColorantsRepair(nChannelOnly: true);
        Assert.Empty(pdfxPreview.Candidates);
        Assert.Empty(pdfxPreview.Refused);
        Assert.Empty(editor.RepairNChannelColorants(nChannelOnly: true).Repaired);
        Assert.Single(Findings(document));
    }

    [Fact]
    public void Reachable_direct_device_n_is_repaired_in_place_without_inventing_its_identity()
    {
        using PdfDocument document = Document(
            ColorantsShape.Missing,
            indirectAttributes: false,
            availableSeparations: 1,
            directDeviceN: true);
        var editor = new PdfDocumentEditor(document);

        NChannelColorantRepairCandidate candidate = Assert.Single(
            editor.PreviewNChannelColorantsRepair().Candidates);
        Assert.Null(candidate.DeviceNObjectNumber);
        Assert.Single(editor.RepairNChannelColorants().Repaired);
        Assert.Empty(Findings(document));

        using PdfDocument reopened = SaveAndReopen(document);
        Assert.Empty(Findings(reopened));
    }
}
