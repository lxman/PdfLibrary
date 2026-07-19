using PdfLibrary.Builder;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class EmbeddedFileAuthoringTests
{
    private static MemoryStream OnePagePdf()
    {
        var ms = new MemoryStream();
        new PdfDocumentBuilder().AddPage(_ => { }).Save(ms);
        ms.Position = 0;
        return ms;
    }

    private static byte[] EditAndSave(MemoryStream source, Action<PdfDocumentEditor> edit)
    {
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(source, leaveOpen: true);
        edit(editor);
        var outMs = new MemoryStream();
        editor.Save(outMs);
        return outMs.ToArray();
    }

    private static IReadOnlyList<EmbeddedFileDescriptor> Reload(byte[] pdf)
    {
        using var doc = PdfDocument.Load(new MemoryStream(pdf), "");
        return doc.GetEmbeddedFiles();
    }

    [Fact]
    public void AddEmbeddedFile_RoundTrips_All_Metadata_And_Data()
    {
        byte[] payload = "<invoice/>"u8.ToArray();
        var mod = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
        byte[] pdf = EditAndSave(OnePagePdf(), e => e.AddEmbeddedFile(new PdfEmbeddedFileSpec
        {
            Name = "factur-x.xml",
            Data = payload,
            MimeType = "text/xml",
            Description = "Factur-X invoice",
            ModificationDate = mod,
            Relationship = PdfAfRelationship.Alternative,
            AssociateWithDocument = true,
        }));

        EmbeddedFileDescriptor file = Assert.Single(Reload(pdf));
        Assert.Equal("factur-x.xml", file.Name);
        Assert.Equal("factur-x.xml", file.FileName);
        Assert.Equal("factur-x.xml", file.UnicodeFileName);
        Assert.Equal("Factur-X invoice", file.Description);
        Assert.Equal("Alternative", file.AfRelationship);
        Assert.Equal("text/xml", file.MimeType);   // proves /Subtype name (with '/') survives serialization
        Assert.True(file.IsAssociated);
        Assert.True(file.HasData);
        Assert.Equal(payload, file.GetDataBytes());
    }

    [Fact]
    public void AddEmbeddedFile_SameName_Replaces_Existing_Entry_And_Af()
    {
        MemoryStream src = OnePagePdf();
        byte[] pdf = EditAndSave(src, e =>
        {
            e.AddEmbeddedFile(new PdfEmbeddedFileSpec
            { Name = "factur-x.xml", Data = "old"u8.ToArray(), AssociateWithDocument = true });
            e.AddEmbeddedFile(new PdfEmbeddedFileSpec
            { Name = "factur-x.xml", Data = "new"u8.ToArray(), AssociateWithDocument = true });
        });

        EmbeddedFileDescriptor file = Assert.Single(Reload(pdf)); // one name-tree entry AND one /AF entry
        Assert.Equal("new"u8.ToArray(), file.GetDataBytes());
    }

    [Fact]
    public void AddEmbeddedFile_Multiple_Entries_Sorted_By_Key()
    {
        byte[] pdf = EditAndSave(OnePagePdf(), e =>
        {
            e.AddEmbeddedFile(new PdfEmbeddedFileSpec { Name = "zeta.xml", Data = "z"u8.ToArray() });
            e.AddEmbeddedFile(new PdfEmbeddedFileSpec { Name = "alpha.xml", Data = "a"u8.ToArray() });
        });

        IReadOnlyList<EmbeddedFileDescriptor> files = Reload(pdf);
        Assert.Equal(new[] { "alpha.xml", "zeta.xml" }, files.Select(f => f.Name).ToArray());
    }

    [Fact]
    public void AddEmbeddedFile_Without_Association_Leaves_IsAssociated_False()
    {
        byte[] pdf = EditAndSave(OnePagePdf(), e =>
            e.AddEmbeddedFile(new PdfEmbeddedFileSpec { Name = "extra.csv", Data = "1,2"u8.ToArray() }));
        Assert.False(Assert.Single(Reload(pdf)).IsAssociated);
    }

    [Fact]
    public void AddEmbeddedFile_Associated_Without_Relationship_Defaults_To_Unspecified()
    {
        byte[] pdf = EditAndSave(OnePagePdf(), e => e.AddEmbeddedFile(new PdfEmbeddedFileSpec
        {
            Name = "extra.csv",
            Data = "1,2"u8.ToArray(),
            AssociateWithDocument = true,
        }));

        EmbeddedFileDescriptor file = Assert.Single(Reload(pdf));
        Assert.Equal("Unspecified", file.AfRelationship);
        Assert.True(file.IsAssociated);
    }
}
