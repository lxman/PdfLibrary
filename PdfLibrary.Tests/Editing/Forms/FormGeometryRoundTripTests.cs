using System.IO;
using PdfLibrary.Builder;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

public class FormGeometryRoundTripTests
{
    [Fact]
    public void EnumerateGeometry_Fill_Flatten_Save_Reload()
    {
        using PdfDocumentEditor editor = FormTestDocs.SingleTextAndCheckbox(
            textRect: new PdfRect(100, 700, 300, 720),
            checkRect: new PdfRect(100, 660, 116, 676),
            checkOnState: "Yes");

        // 1) Geometry: widget rect → image pixels at 1× scale.
        //    editor.Pages implements IReadOnlyList<PdfPage>, so Pages[0] gives the first page.
        PdfFieldWidget tw = editor.Forms["text1"]!.Widgets[0];
        PdfPage page = editor.Pages[tw.PageIndex];
        PageGeometry geo = page.GetGeometry(1.0);
        PdfRect pixelRect = geo.MapRectToImage(tw.Rect);
        Assert.True(pixelRect.Width > 0, $"Expected positive pixel width; got {pixelRect.Width}");
        Assert.True(pixelRect.Height > 0, $"Expected positive pixel height; got {pixelRect.Height}");

        // 2) Fill via the existing public fill API.
        ((PdfTextField)editor.Forms["text1"]!).Value = "Hello";
        ((PdfButtonField)editor.Forms["check1"]!).Check();

        // 3) Flatten + save to a MemoryStream.
        editor.Forms.Flatten();
        byte[] outBytes;
        using (var ms = new MemoryStream())
        {
            editor.Save(ms);
            outBytes = ms.ToArray();
        }

        // 4) Reload — AcroForm is gone (flattened), content must load with >= 1 page.
        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(outBytes));
        Assert.True(reloaded.PageCount >= 1,
            $"Expected at least 1 page after reload; got {reloaded.PageCount}");
    }
}
