using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing.Stamping;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class FormXObjectCompilerTests
{
    [Fact]
    public void CompileInto_TextStamp_ProducesClonedFormXObject()
    {
        using PdfDocument target = PdfDocument.CreateEmpty();
        PdfIndirectReference xref = FormXObjectCompiler.CompileInto(
            target, 200, 100, p => p.AddText("STAMPME", 10, 50, "Helvetica", 12));

        var xobj = (PdfStream)target.GetObject(xref.ObjectNumber)!;
        Assert.Equal("XObject", ((PdfName)xobj.Dictionary[PdfName.TypeName]).Value);
        Assert.Equal("Form", ((PdfName)xobj.Dictionary[PdfName.Subtype]).Value);
        Assert.True(xobj.Dictionary.ContainsKey(new PdfName("BBox")));
        Assert.True(xobj.Dictionary.ContainsKey(new PdfName("Resources")));

        string content = Encoding.ASCII.GetString(xobj.GetDecodedData());
        Assert.Contains("STAMPME", content);
    }
}
