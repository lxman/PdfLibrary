using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Document;

/// <summary>
/// The read-only Tagged-PDF tag tree (<see cref="PdfDocument.GetTagTree"/>): structure hierarchy,
/// role-map resolution, accessibility attributes, and per-element text via the marked-content (MCID)
/// mapping. Built from a hand-assembled tagged document so the MCID → text mapping is verifiable.
/// </summary>
public class TagTreeTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfString Str(string s) => new(Encoding.Latin1.GetBytes(s));

    /// <summary>A one-page tagged document. The page content shows two paragraphs, each in its own
    /// marked-content sequence (MCID 0 and 1); the structure tree pairs a &lt;P&gt; with each, plus a
    /// &lt;Figure&gt; carrying only /Alt (no content) under a &lt;Document&gt; root. /MarkInfo /Marked is true.</summary>
    private static PdfDocument TaggedDoc()
    {
        var doc = new PdfDocument();

        // Base-14 font (object 5).
        doc.AddObject(5, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"), [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("Helvetica"), [N("Encoding")] = N("WinAnsiEncoding"),
        });

        // Content stream (object 4): two tagged paragraphs + an untagged (artifact) run.
        const string content =
            "/P <</MCID 0>> BDC BT /F1 12 Tf 72 700 Td (First para) Tj ET EMC " +
            "/P <</MCID 1>> BDC BT /F1 12 Tf 72 680 Td (Second para) Tj ET EMC " +
            "/Artifact BMC BT /F1 12 Tf 72 40 Td (page footer) Tj ET EMC";
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.Latin1.GetBytes(content)));

        // Page (object 3).
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2), [N("Contents")] = Ref(4),
            [N("MediaBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792)),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F1")] = Ref(5) } },
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });

        // Structure tree: StructTreeRoot(10) → Document(14) → [P(MCID 0), P(MCID 1), Figure(Alt)] (11–13).
        doc.AddObject(11, 0, Elem("P", parent: 14, pg: 3, k: new PdfInteger(0)));
        doc.AddObject(12, 0, Elem("P", parent: 14, pg: 3, k: new PdfInteger(1)));
        PdfDictionary figure = Elem("Figure", parent: 14, pg: 3, k: null);
        figure[N("Alt")] = Str("A chart");
        doc.AddObject(13, 0, figure);
        doc.AddObject(14, 0, new PdfDictionary
        {
            [N("Type")] = N("StructElem"), [N("S")] = N("Document"), [N("Parent")] = Ref(10),
            [N("K")] = new PdfArray(Ref(11), Ref(12), Ref(13)),
        });
        doc.AddObject(10, 0, new PdfDictionary
        {
            [N("Type")] = N("StructTreeRoot"), [N("K")] = Ref(14),
        });

        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2), [N("StructTreeRoot")] = Ref(10),
            [N("MarkInfo")] = new PdfDictionary { [N("Marked")] = PdfBoolean.True },
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static PdfDictionary Elem(string type, int parent, int pg, PdfObject? k)
    {
        var e = new PdfDictionary
        {
            [N("Type")] = N("StructElem"), [N("S")] = N(type), [N("Parent")] = Ref(parent), [N("Pg")] = Ref(pg),
        };
        if (k is not null) e[N("K")] = k;
        return e;
    }

    [Fact]
    public void Reads_structure_attributes_and_per_element_text()
    {
        TagTree tree = TaggedDoc().GetTagTree();

        Assert.True(tree.IsTagged);
        TagNode document = Assert.Single(tree.Roots);
        Assert.Equal("Document", document.Type);
        Assert.Equal(3, document.Children.Count);

        TagNode p1 = document.Children[0];
        TagNode p2 = document.Children[1];
        TagNode figure = document.Children[2];

        Assert.Equal("P", p1.Type);
        Assert.True(p1.IsStandard);
        Assert.Equal(0, p1.PageIndex);
        Assert.Contains("First para", p1.Text);
        Assert.DoesNotContain("Second", p1.Text);   // each element gets only its own MCID's text

        Assert.Contains("Second para", p2.Text);
        Assert.DoesNotContain("First", p2.Text);

        Assert.Equal("Figure", figure.Type);
        Assert.Equal("A chart", figure.Alt);
        Assert.Equal("", figure.Text);               // a figure with no content MCID has no text

        // The artifact run ("page footer") is not tagged, so it appears on no element.
        Assert.DoesNotContain("footer", p1.Text + p2.Text + figure.Text);
    }

    [Fact]
    public void Untagged_document_reports_empty_tree()
    {
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfDictionary { [N("Type")] = N("Page"), [N("Parent")] = Ref(2) });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);

        TagTree tree = doc.GetTagTree();
        Assert.False(tree.IsTagged);
        Assert.Empty(tree.Roots);
    }

    [Fact]
    public void Role_mapped_custom_type_resolves_to_standard()
    {
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfDictionary { [N("Type")] = N("Page"), [N("Parent")] = Ref(2) });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(11, 0, new PdfDictionary { [N("Type")] = N("StructElem"), [N("S")] = N("Heading1"), [N("Parent")] = Ref(10) });
        doc.AddObject(10, 0, new PdfDictionary
        {
            [N("Type")] = N("StructTreeRoot"), [N("K")] = Ref(11),
            [N("RoleMap")] = new PdfDictionary { [N("Heading1")] = N("H1") },
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2), [N("StructTreeRoot")] = Ref(10) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);

        TagNode node = Assert.Single(doc.GetTagTree().Roots);
        Assert.Equal("H1", node.Type);          // role-mapped standard type
        Assert.Equal("Heading1", node.RawType); // literal /S
        Assert.True(node.IsStandard);
    }

    [Fact]
    [Trait("Category", "LocalOnly")]
    public void Reference_file_tag_tree_is_populated()
    {
        // Real-world smoke test over a PDF Association PDF/UA reference file (not vendored).
        string? path = new[]
            {
                "/Users/michaeljordan/RiderProjects/PDFUA-Reference-Files/PDFUA-Ref-2-02_Invoice.pdf",
            }.FirstOrDefault(System.IO.File.Exists);
        Assert.SkipUnless(path is not null, "no reference file");

        using var doc = PdfDocument.Load(
            new System.IO.MemoryStream(System.IO.File.ReadAllBytes(path!)), string.Empty);
        TagTree tree = doc.GetTagTree();

        Assert.True(tree.IsTagged);
        Assert.NotEmpty(tree.Roots);

        var all = new List<TagNode>();
        void Collect(TagNode n) { all.Add(n); foreach (TagNode c in n.Children) Collect(c); }
        foreach (TagNode r in tree.Roots) Collect(r);

        Assert.Contains(all, n => n.Type == "H1");                       // headings present and typed
        Assert.Contains(all, n => n.Text.Length > 0);                    // per-element text extracted
        Assert.Contains(all, n => n.Type == "Figure" && n.Alt is { Length: > 0 }); // figure alt text
    }
}
