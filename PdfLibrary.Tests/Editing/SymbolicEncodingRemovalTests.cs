using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// Tests for <see cref="PdfDocumentEditor.RemoveSymbolicEncoding"/> (ISO 19005-2 6.2.11.6).
///
/// <para>Fixture convention mirrors <see cref="CidToGidMapIdentityWriteTests"/> — a minimal valid
/// document (catalog + empty page tree) plus the simple TrueType font dictionary itself, with no page
/// or content stream needed since the method resolves its target directly by <see cref="FontId"/>.</para>
///
/// <para>The Flags bit values reproduce <c>FontDictionaryRule.SymbolicFlags</c>'s own reading of
/// /FontDescriptor /Flags (FontDictionaryRule.cs:316-324): bit 3 (value 4) is Symbolic, bit 6
/// (value 32) is Nonsymbolic — the same constants <c>PreflightSlice18Tests</c> pins.</para>
/// </summary>
public class SymbolicEncodingRemovalTests
{
    private const int Symbolic = 4;
    private const int Nonsymbolic = 32;

    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    [Fact]
    public void Removes_encoding_from_a_symbolic_truetype()
    {
        using PdfDocumentEditor editor =
            BuildDocument("TrueType", flags: Symbolic, encoding: N("WinAnsiEncoding")).Edit();

        bool result = editor.RemoveSymbolicEncoding(new FontId(30));

        Assert.True(result);
        Assert.Null(Dict(editor.Document, 30).Get("Encoding"));
    }

    [Fact]
    public void Refuses_a_nonsymbolic_font()
    {
        using PdfDocumentEditor editor =
            BuildDocument("TrueType", flags: Nonsymbolic, encoding: N("WinAnsiEncoding")).Edit();

        bool result = editor.RemoveSymbolicEncoding(new FontId(30));

        Assert.False(result);
        Assert.NotNull(Dict(editor.Document, 30).Get("Encoding"));
    }

    [Fact]
    public void Refuses_a_font_with_no_encoding()
    {
        using PdfDocumentEditor editor =
            BuildDocument("TrueType", flags: Symbolic, encoding: null).Edit();

        bool result = editor.RemoveSymbolicEncoding(new FontId(30));

        Assert.False(result);
        Assert.Null(Dict(editor.Document, 30).Get("Encoding"));
    }

    [Fact]
    public void Is_idempotent()
    {
        using PdfDocumentEditor editor =
            BuildDocument("TrueType", flags: Symbolic, encoding: N("WinAnsiEncoding")).Edit();
        Assert.True(editor.RemoveSymbolicEncoding(new FontId(30)));
        string firstState = Dict(editor.Document, 30).ToPdfString();

        bool second = editor.RemoveSymbolicEncoding(new FontId(30));

        Assert.False(second);
        Assert.Equal(firstState, Dict(editor.Document, 30).ToPdfString());
    }

    // ── CanRemoveSymbolicEncoding (2026-08-21 font-dictionary remediation, fix round 1) ────────────────
    // Same rationale as CidToGidMapIdentityWriteTests' query block: FontDictionaryDomain.Propose needs
    // a live, non-mutating answer, and the query/write must share one gate so they cannot disagree.

    [Fact]
    public void Query_agrees_with_the_write_for_a_symbolic_truetype()
    {
        using PdfDocumentEditor editor =
            BuildDocument("TrueType", flags: Symbolic, encoding: N("WinAnsiEncoding")).Edit();
        string before = Dict(editor.Document, 30).ToPdfString();

        bool can = editor.CanRemoveSymbolicEncoding(new FontId(30));

        Assert.True(can);
        Assert.Equal(before, Dict(editor.Document, 30).ToPdfString()); // the query wrote nothing

        Assert.Equal(can, editor.RemoveSymbolicEncoding(new FontId(30)));
    }

    [Fact]
    public void Query_agrees_with_the_write_for_a_nonsymbolic_font()
    {
        using PdfDocumentEditor editor =
            BuildDocument("TrueType", flags: Nonsymbolic, encoding: N("WinAnsiEncoding")).Edit();
        string before = Dict(editor.Document, 30).ToPdfString();

        bool can = editor.CanRemoveSymbolicEncoding(new FontId(30));

        Assert.False(can);
        Assert.Equal(before, Dict(editor.Document, 30).ToPdfString());

        Assert.Equal(can, editor.RemoveSymbolicEncoding(new FontId(30)));
    }

    [Fact]
    public void Query_agrees_with_the_write_for_a_font_with_no_encoding()
    {
        using PdfDocumentEditor editor =
            BuildDocument("TrueType", flags: Symbolic, encoding: null).Edit();
        string before = Dict(editor.Document, 30).ToPdfString();

        bool can = editor.CanRemoveSymbolicEncoding(new FontId(30));

        Assert.False(can);
        Assert.Equal(before, Dict(editor.Document, 30).ToPdfString());

        Assert.Equal(can, editor.RemoveSymbolicEncoding(new FontId(30)));
    }

    /// <summary>The query is idempotent by construction (it never writes), but this pins it directly:
    /// two calls in a row must agree with each other and leave the dictionary exactly as they found
    /// it.</summary>
    [Fact]
    public void Query_is_repeatable_and_never_mutates()
    {
        using PdfDocumentEditor editor =
            BuildDocument("TrueType", flags: Symbolic, encoding: N("WinAnsiEncoding")).Edit();
        string before = Dict(editor.Document, 30).ToPdfString();

        bool first = editor.CanRemoveSymbolicEncoding(new FontId(30));
        bool second = editor.CanRemoveSymbolicEncoding(new FontId(30));

        Assert.True(first);
        Assert.Equal(first, second);
        Assert.Equal(before, Dict(editor.Document, 30).ToPdfString());
    }

    private static PdfDictionary Dict(PdfDocument document, int objectNumber) =>
        (PdfDictionary)document.Objects[objectNumber];

    /// <summary>A minimal valid document (catalog object 1 → empty page tree object 2) plus a simple
    /// font dictionary at object 30 with the given <paramref name="subtype"/>, an optional
    /// /FontDescriptor /Flags value, and an optional /Encoding value.</summary>
    private static PdfDocument BuildDocument(string subtype, int? flags, PdfObject? encoding)
    {
        var doc = new PdfDocument();

        var fontDict = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N(subtype),
            [N("BaseFont")] = N("ABCDEF+TestFont"),
        };
        if (flags is not null)
            fontDict[N("FontDescriptor")] = new PdfDictionary
            {
                [N("Type")] = N("FontDescriptor"),
                [N("FontName")] = N("ABCDEF+TestFont"),
                [N("Flags")] = new PdfInteger(flags.Value),
            };
        if (encoding is not null)
            fontDict[N("Encoding")] = encoding;
        doc.AddObject(30, 0, fontDict);

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
