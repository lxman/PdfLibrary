using PdfLibrary.Builder;
using PdfLibrary.Conformance;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// <see cref="PdfDocumentEditor.SetFileId"/> is a minimal PUBLIC wrapper around the trailer's
/// /ID entry. <see cref="PdfDocument.Trailer"/> and the primitive types that back it
/// (<c>PdfTrailer</c>, <c>PdfString</c>, <c>PdfArray</c>) are all `internal`, so nothing outside
/// this assembly (and its InternalsVisibleTo grantees) can reach
/// <c>editor.Document.Trailer.Id = new PdfArray(...)</c> — the exact mechanism
/// <c>ResaveVerificationTests.FileId_finding_is_cleared_when_the_caller_sets_trailer_id_before_saving</c>
/// proves. Pellucid.Core's remediation-spine Task 3 needs that mechanism from outside the engine
/// assembly (it is a NuGet/ProjectReference consumer, not IVT-listed), hence this wrapper.
/// </summary>
public class PdfDocumentEditorFileIdTests
{
    private static byte[] BuilderBytesWithoutId()
    {
        byte[] raw = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hello", 100, 700))
            .ToByteArray();
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(raw));
        using PdfDocumentEditor stripEditor = doc.Edit();
        stripEditor.Document.Trailer.Dictionary.Remove(new PdfLibrary.Core.Primitives.PdfName("ID"));
        using var ms = new MemoryStream();
        stripEditor.Save(ms);
        return ms.ToArray();
    }

    [Fact]
    public void SetFileId_clears_the_file_id_finding_after_a_save()
    {
        byte[] original = BuilderBytesWithoutId();
        PreflightResult before = Preflighter.Check(original, ConformanceProfile.PdfA2b);
        Assert.Contains(before.Findings, f => f.RuleId == "file-id" && f.Severity == FindingSeverity.Error);

        using PdfDocument doc = PdfDocument.Load(new MemoryStream(original));
        using PdfDocumentEditor editor = doc.Edit();
        editor.SetFileId([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                           0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10]);
        using var outMs = new MemoryStream();
        editor.Save(outMs);

        PreflightResult after = Preflighter.Check(outMs.ToArray(), ConformanceProfile.PdfA2b);
        Assert.DoesNotContain(after.Findings, f => f.RuleId == "file-id");
    }

    [Fact]
    public void SetFileId_uses_the_same_bytes_for_both_array_elements()
    {
        byte[] original = BuilderBytesWithoutId();
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(original));
        using PdfDocumentEditor editor = doc.Edit();
        byte[] idBytes = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00, 0x11,
                          0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99];
        editor.SetFileId(idBytes);
        using var outMs = new MemoryStream();
        editor.Save(outMs);

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(outMs.ToArray()));
        using PdfDocumentEditor readBack = reloaded.Edit();
        PdfLibrary.Core.Primitives.PdfArray? idArray = readBack.Document.Trailer.Id;
        Assert.NotNull(idArray);
        Assert.Equal(2, idArray!.Count);
        var e0 = Assert.IsType<PdfLibrary.Core.Primitives.PdfString>(idArray[0]);
        var e1 = Assert.IsType<PdfLibrary.Core.Primitives.PdfString>(idArray[1]);
        Assert.Equal(idBytes, e0.Bytes);
        Assert.Equal(idBytes, e1.Bytes);
    }

    [Fact]
    public void SetFileId_null_throws()
    {
        using PdfDocumentEditor editor = PdfDocumentEditor.CreateBlank();
        Assert.Throws<ArgumentNullException>(() => editor.SetFileId(null!));
    }
}
