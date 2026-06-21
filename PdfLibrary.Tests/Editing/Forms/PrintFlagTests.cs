using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

/// <summary>
/// A filled widget must carry the /F Print flag (bit 3, value 4) so its appearance prints, not just
/// displays. Widgets authored without /F default to non-printing — the bug where filled values showed
/// on screen in Chrome but vanished from print.
/// </summary>
public class PrintFlagTests
{
    private static int WidgetFlags(PdfDocument doc, PdfFormField f)
    {
        PdfDictionary w = f.Widgets[0];
        PdfObject? flagObj = w.Get(new PdfName("F"));
        if (flagObj is PdfIndirectReference r) flagObj = doc.GetObject(r.ObjectNumber);
        return flagObj is PdfInteger i ? i.Value : 0;
    }

    [Fact]
    public void FillingTextField_SetsPrintFlag()
    {
        byte[] pdf = FormTestDocs.WithTextField("name");
        string outPath = System.IO.Path.GetTempFileName();
        try
        {
            using (PdfDocument doc = PdfDocument.Load(new System.IO.MemoryStream(pdf)))
            {
                PdfDocumentEditor edit = doc.Edit();
                ((PdfTextField)edit.Forms["name"]!).Value = "x";
                edit.Save(outPath);
            }
            using PdfDocument re = PdfDocument.Load(outPath);
            int flags = WidgetFlags(re, re.Edit().Forms["name"]!);
            Assert.Equal(4, flags & 4); // Print bit set
        }
        finally { System.IO.File.Delete(outPath); }
    }

    [Fact]
    public void CheckingCheckbox_SetsPrintFlag()
    {
        byte[] pdf = FormTestDocs.WithCheckbox("agree");
        string outPath = System.IO.Path.GetTempFileName();
        try
        {
            using (PdfDocument doc = PdfDocument.Load(new System.IO.MemoryStream(pdf)))
            {
                PdfDocumentEditor edit = doc.Edit();
                ((PdfButtonField)edit.Forms["agree"]!).Check();
                edit.Save(outPath);
            }
            using PdfDocument re = PdfDocument.Load(outPath);
            int flags = WidgetFlags(re, re.Edit().Forms["agree"]!);
            Assert.Equal(4, flags & 4);
        }
        finally { System.IO.File.Delete(outPath); }
    }
}
