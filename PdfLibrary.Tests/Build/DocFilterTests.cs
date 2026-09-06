using System.Xml.Linq;
using PdfLibrary.Build;

namespace PdfLibrary.Tests.Build;

// Probes for the filter. The test assembly itself is the metadata under test, so the expectations
// cannot drift from what the compiler actually emitted.
public class DocFilterProbe
{
    public int Value { get; }
    public DocFilterProbe(int value) => Value = value;
    public void Visible(string text) { _ = text; }
    internal void Hidden() { }
    public int this[int index] => index;
    public event EventHandler? Changed;
    // Named HiddenEvent, not Hidden: the internal void Hidden() method above already owns that
    // identifier, and C# does not allow a method and an event to share a name on the same type
    // (CS0102). This still probes an internal event's E: doc-id, which is the point of the case.
    internal event EventHandler? HiddenEvent;
    public void Raise() { Changed?.Invoke(this, EventArgs.Empty); HiddenEvent?.Invoke(this, EventArgs.Empty); }
    public class Inner { public int X = 1; }
    internal class Secret { public int Y = 2; }
}

internal class DocFilterSecret
{
    public int Exposed = 3;
}

public class DocFilterTests
{
    private static DocFilter.PublicSurface Surface()
    {
        using FileStream fs = File.OpenRead(typeof(DocFilterTests).Assembly.Location);
        return DocFilter.ReadPublicSurface(fs);
    }

    [Theory]
    [InlineData("T:PdfLibrary.Tests.Build.DocFilterProbe", true)]
    [InlineData("M:PdfLibrary.Tests.Build.DocFilterProbe.#ctor(System.Int32)", true)]
    [InlineData("M:PdfLibrary.Tests.Build.DocFilterProbe.Visible(System.String)", true)]
    [InlineData("P:PdfLibrary.Tests.Build.DocFilterProbe.Value", true)]
    [InlineData("T:PdfLibrary.Tests.Build.DocFilterProbe.Inner", true)]
    [InlineData("F:PdfLibrary.Tests.Build.DocFilterProbe.Inner.X", true)]
    [InlineData("N:PdfLibrary.Tests.Build", true)]
    [InlineData("M:PdfLibrary.Tests.Build.DocFilterProbe.Hidden", false)]
    [InlineData("T:PdfLibrary.Tests.Build.DocFilterProbe.Secret", false)]
    [InlineData("F:PdfLibrary.Tests.Build.DocFilterProbe.Secret.Y", false)]
    [InlineData("T:PdfLibrary.Tests.Build.DocFilterSecret", false)]
    [InlineData("F:PdfLibrary.Tests.Build.DocFilterSecret.Exposed", false)]
    [InlineData("P:PdfLibrary.Tests.Build.DocFilterProbe.Item(System.Int32)", true)]
    [InlineData("E:PdfLibrary.Tests.Build.DocFilterProbe.Changed", true)]
    [InlineData("E:PdfLibrary.Tests.Build.DocFilterProbe.HiddenEvent", false)]
    public void IsPublic_resolves_doc_ids_against_the_assembly(string docId, bool expected)
    {
        Assert.Equal(expected, DocFilter.IsPublic(docId, Surface()));
    }

    [Fact]
    public void Filter_removes_only_non_public_entries_and_reports_counts()
    {
        var doc = XDocument.Parse("""
            <doc>
              <assembly><name>PdfLibrary.Tests</name></assembly>
              <members>
                <member name="T:PdfLibrary.Tests.Build.DocFilterProbe"><summary>keep</summary></member>
                <member name="M:PdfLibrary.Tests.Build.DocFilterProbe.Hidden"><summary>drop</summary></member>
                <member name="T:PdfLibrary.Tests.Build.DocFilterSecret"><summary>drop</summary></member>
                <member name="P:PdfLibrary.Tests.Build.DocFilterProbe.Value"><summary>keep</summary></member>
              </members>
            </doc>
            """);
        (XDocument filtered, int removed, int kept) = DocFilter.Filter(doc, Surface());
        Assert.Equal(2, removed);
        Assert.Equal(2, kept);
        List<string> names = filtered.Root!.Element("members")!.Elements("member")
            .Select(m => m.Attribute("name")!.Value).ToList();
        Assert.Equal(["T:PdfLibrary.Tests.Build.DocFilterProbe", "P:PdfLibrary.Tests.Build.DocFilterProbe.Value"], names);
    }

    [Fact]
    public void Filter_is_idempotent()
    {
        var doc = XDocument.Parse("""<doc><members><member name="T:PdfLibrary.Tests.Build.DocFilterProbe"/></members></doc>""");
        DocFilter.PublicSurface surface = Surface();
        (XDocument once, int removed1, _) = DocFilter.Filter(doc, surface);
        (_, int removed2, int kept2) = DocFilter.Filter(once, surface);
        Assert.Equal(0, removed1);
        Assert.Equal(0, removed2);
        Assert.Equal(1, kept2);
    }
}
