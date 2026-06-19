using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class PdfDocumentMutationTests
{
    [Fact]
    public void AllocateObjectNumber_IsMonotonicAndAboveExisting()
    {
        var doc = new PdfDocument();
        doc.AddObject(5, 0, new PdfDictionary());
        int a = doc.AllocateObjectNumber();
        int b = doc.AllocateObjectNumber();
        Assert.True(a > 5, $"expected > 5, got {a}");
        Assert.True(b > a, $"expected monotonic, got {a} then {b}");
    }

    [Fact]
    public void RegisterObject_AddsObject_AndReturnsResolvableRef()
    {
        var doc = new PdfDocument();
        var dict = new PdfDictionary();
        PdfIndirectReference reference = doc.RegisterObject(dict);
        Assert.Same(dict, doc.GetObject(reference.ObjectNumber));
    }

    [Fact]
    public void ReplaceObject_OverwritesSlot()
    {
        var doc = new PdfDocument();
        PdfIndirectReference reference = doc.RegisterObject(new PdfDictionary());
        var replacement = new PdfArray();
        doc.ReplaceObject(reference.ObjectNumber, replacement);
        Assert.Same(replacement, doc.GetObject(reference.ObjectNumber));
    }

    [Fact]
    public void RemoveObject_DropsSlot()
    {
        var doc = new PdfDocument();
        PdfIndirectReference reference = doc.RegisterObject(new PdfDictionary());
        doc.RemoveObject(reference.ObjectNumber);
        Assert.Null(doc.GetObject(reference.ObjectNumber));
    }
}
