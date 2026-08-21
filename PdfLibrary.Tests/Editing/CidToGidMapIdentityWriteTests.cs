using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// Tests for <see cref="PdfDocumentEditor.SetCidToGidMapIdentity"/> (ISO 19005-2 6.2.11.3.2).
///
/// <para>No vendored fixture for this shape exists in this project — hand-built <see cref="PdfDocument"/>
/// construction via <c>AddObject</c> is the established convention here (see
/// <c>PdfDocumentEditorFontsTests</c>'s own comment on the point). A CIDFontType2 descendant needs no
/// Type0 wrapper or page tree to exercise this method — it resolves the target object directly by
/// <see cref="FontId"/> — so the fixture is just a minimal valid document (catalog + empty page tree,
/// mirroring <c>PdfDocument.CreateEmpty</c>) plus the descendant dictionary itself.</para>
/// </summary>
public class CidToGidMapIdentityWriteTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    [Fact]
    public void Writes_identity_onto_a_cidfonttype2_that_omits_the_key()
    {
        using PdfDocumentEditor editor = BuildDocument("CIDFontType2", cidToGidMap: null).Edit();

        bool result = editor.SetCidToGidMapIdentity(new FontId(21));

        Assert.True(result);
        Assert.Equal("Identity", ((PdfName)Dict(editor.Document, 21).Get("CIDToGIDMap")!).Value);
    }

    [Fact]
    public void Refuses_a_font_that_already_carries_a_cidtogidmap()
    {
        using PdfDocumentEditor nameEditor =
            BuildDocument("CIDFontType2", cidToGidMap: N("Identity")).Edit();
        Assert.False(nameEditor.SetCidToGidMapIdentity(new FontId(21)));
        Assert.Equal("Identity", ((PdfName)Dict(nameEditor.Document, 21).Get("CIDToGIDMap")!).Value);

        var streamObj = new PdfStream(new PdfDictionary(), [0, 0]);
        using PdfDocumentEditor streamEditor = BuildDocument("CIDFontType2", cidToGidMap: streamObj).Edit();
        Assert.False(streamEditor.SetCidToGidMapIdentity(new FontId(21)));
        Assert.Same(streamObj, Dict(streamEditor.Document, 21).Get("CIDToGIDMap"));
    }

    [Fact]
    public void Refuses_a_non_cidfonttype2()
    {
        using PdfDocumentEditor editor = BuildDocument("CIDFontType0", cidToGidMap: null).Edit();

        bool result = editor.SetCidToGidMapIdentity(new FontId(21));

        Assert.False(result);
        Assert.Null(Dict(editor.Document, 21).Get("CIDToGIDMap"));
    }

    [Fact]
    public void Is_idempotent()
    {
        using PdfDocumentEditor editor = BuildDocument("CIDFontType2", cidToGidMap: null).Edit();
        Assert.True(editor.SetCidToGidMapIdentity(new FontId(21)));
        string firstState = Dict(editor.Document, 21).ToPdfString();

        bool second = editor.SetCidToGidMapIdentity(new FontId(21));

        Assert.False(second);
        Assert.Equal(firstState, Dict(editor.Document, 21).ToPdfString());
    }

    // ── CanSetCidToGidMapIdentity (2026-08-21 font-dictionary remediation, fix round 1) ────────────────
    // A caller (FontDictionaryDomain.Propose) needs a live, honest, NON-MUTATING answer to "would the
    // write succeed right now" -- these pin that the query and the write share one gate and can never
    // disagree, and that the query itself never touches the dictionary.

    [Fact]
    public void Query_agrees_with_the_write_for_a_settable_font()
    {
        using PdfDocumentEditor editor = BuildDocument("CIDFontType2", cidToGidMap: null).Edit();
        string before = Dict(editor.Document, 21).ToPdfString();

        bool can = editor.CanSetCidToGidMapIdentity(new FontId(21));

        Assert.True(can);
        Assert.Equal(before, Dict(editor.Document, 21).ToPdfString()); // the query wrote nothing

        Assert.Equal(can, editor.SetCidToGidMapIdentity(new FontId(21)));
    }

    [Fact]
    public void Query_agrees_with_the_write_for_a_font_that_already_carries_a_cidtogidmap()
    {
        using PdfDocumentEditor editor = BuildDocument("CIDFontType2", cidToGidMap: N("Identity")).Edit();
        string before = Dict(editor.Document, 21).ToPdfString();

        bool can = editor.CanSetCidToGidMapIdentity(new FontId(21));

        Assert.False(can);
        Assert.Equal(before, Dict(editor.Document, 21).ToPdfString());

        Assert.Equal(can, editor.SetCidToGidMapIdentity(new FontId(21)));
    }

    [Fact]
    public void Query_agrees_with_the_write_for_a_non_cidfonttype2()
    {
        using PdfDocumentEditor editor = BuildDocument("CIDFontType0", cidToGidMap: null).Edit();
        string before = Dict(editor.Document, 21).ToPdfString();

        bool can = editor.CanSetCidToGidMapIdentity(new FontId(21));

        Assert.False(can);
        Assert.Equal(before, Dict(editor.Document, 21).ToPdfString());

        Assert.Equal(can, editor.SetCidToGidMapIdentity(new FontId(21)));
    }

    /// <summary>The query is idempotent by construction (it never writes), but this pins it directly
    /// rather than trusting that fact: two calls in a row must agree with each other and leave the
    /// dictionary exactly as they found it.</summary>
    [Fact]
    public void Query_is_repeatable_and_never_mutates()
    {
        using PdfDocumentEditor editor = BuildDocument("CIDFontType2", cidToGidMap: null).Edit();
        string before = Dict(editor.Document, 21).ToPdfString();

        bool first = editor.CanSetCidToGidMapIdentity(new FontId(21));
        bool second = editor.CanSetCidToGidMapIdentity(new FontId(21));

        Assert.True(first);
        Assert.Equal(first, second);
        Assert.Equal(before, Dict(editor.Document, 21).ToPdfString());
    }

    private static PdfDictionary Dict(PdfDocument document, int objectNumber) =>
        (PdfDictionary)document.Objects[objectNumber];

    /// <summary>A minimal valid document (catalog object 1 → empty page tree object 2) plus a
    /// descendant CIDFont dictionary at object 21 with the given <paramref name="subtype"/> and
    /// optional <c>/CIDToGIDMap</c> value (stored directly — the method under test only inspects the
    /// raw dictionary entry for presence, never resolves it, so a direct value exercises the guard
    /// identically to an indirect one).</summary>
    private static PdfDocument BuildDocument(string subtype, PdfObject? cidToGidMap)
    {
        var doc = new PdfDocument();

        var cidDict = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N(subtype),
            [N("BaseFont")] = N("CIDFontX"),
            [N("FontDescriptor")] = new PdfDictionary
            {
                [N("Type")] = N("FontDescriptor"),
                [N("FontName")] = N("CIDFontX"),
            },
        };
        if (cidToGidMap is not null)
            cidDict[N("CIDToGIDMap")] = cidToGidMap;
        doc.AddObject(21, 0, cidDict);

        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(),
            [N("Count")] = new PdfInteger(0),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }
}
