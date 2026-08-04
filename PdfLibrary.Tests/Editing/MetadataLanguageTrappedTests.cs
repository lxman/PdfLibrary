using PdfLibrary.Builder;
using PdfLibrary.Editing;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class MetadataLanguageTrappedTests
{
    private static MemoryStream OnePage() => new(
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("x", 72, 700, "Helvetica", 12)).ToByteArray());

    [Fact]
    public void Language_round_trips_through_a_save()
    {
        using var saved = new MemoryStream();
        using (PdfDocumentEditor editor = PdfDocumentEditor.Open(OnePage()))
        {
            Assert.Null(editor.Metadata.Language);
            editor.Metadata.Language = "en-US";
            editor.Save(saved);
        }

        saved.Position = 0;
        using PdfDocumentEditor reopened = PdfDocumentEditor.Open(saved);
        Assert.Equal("en-US", reopened.Metadata.Language);
    }

    [Fact]
    public void Language_set_to_null_removes_the_entry()
    {
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(OnePage());
        editor.Metadata.Language = "de";
        editor.Metadata.Language = null;
        Assert.Null(editor.Metadata.Language);
    }

    [Fact]
    public void Trapped_round_trips_all_three_values()
    {
        foreach (PdfTrapped value in new[] { PdfTrapped.True, PdfTrapped.False, PdfTrapped.Unknown })
        {
            using var saved = new MemoryStream();
            using (PdfDocumentEditor editor = PdfDocumentEditor.Open(OnePage()))
            {
                editor.Metadata.Trapped = value;
                editor.Save(saved);
            }

            saved.Position = 0;
            using PdfDocumentEditor reopened = PdfDocumentEditor.Open(saved);
            Assert.Equal(value, reopened.Metadata.Trapped);
        }
    }

    [Fact]
    public void Trapped_absent_is_null_and_distinct_from_Unknown()
    {
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(OnePage());
        Assert.Null(editor.Metadata.Trapped);          // no /Trapped key at all

        editor.Metadata.Trapped = PdfTrapped.Unknown;  // an explicit /Unknown value
        Assert.Equal(PdfTrapped.Unknown, editor.Metadata.Trapped);

        editor.Metadata.Trapped = null;                // back to absent
        Assert.Null(editor.Metadata.Trapped);
    }
}
