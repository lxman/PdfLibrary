using System.IO;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

public class FormReadTests
{
    [Fact]
    public void Reads_TextField_NameTypeValue()
    {
        byte[] pdf = FormTestDocs.WithTextField("fullName", "Jane Doe");
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        PdfFormField f = doc.Edit().Forms["fullName"]!;
        var t = Assert.IsType<PdfTextField>(f);
        Assert.Equal(PdfFormFieldType.Text, t.Type);
        Assert.Equal("fullName", t.FullName);
        Assert.Equal("Jane Doe", t.Value);
    }

    [Fact]
    public void Reads_Checkbox_Radio_Choice()
    {
        using var cb = PdfDocument.Load(new MemoryStream(FormTestDocs.WithCheckbox("agree", true)));
        var c = (PdfButtonField)cb.Edit().Forms["agree"]!;
        Assert.Equal(ButtonKind.Checkbox, c.Kind);
        Assert.True(c.IsChecked);

        using var rb = PdfDocument.Load(new MemoryStream(FormTestDocs.WithRadioGroup("color", new[]{"red","blue"}, "red")));
        var radio = (PdfButtonField)rb.Edit().Forms["color"]!;
        Assert.Equal(ButtonKind.Radio, radio.Kind);
        Assert.Equal("red", radio.SelectedOption);
        Assert.Equal(new[]{"red","blue"}, radio.Options);

        using var ch = PdfDocument.Load(new MemoryStream(FormTestDocs.WithChoice("city",
            new[]{("NYC","New York"),("LA","Los Angeles")}, combo:true, selected:new[]{"NYC"})));
        var choice = (PdfChoiceField)ch.Edit().Forms["city"]!;
        Assert.True(choice.IsCombo);
        Assert.Equal(new[]{"NYC"}, choice.SelectedValues);
    }
}
