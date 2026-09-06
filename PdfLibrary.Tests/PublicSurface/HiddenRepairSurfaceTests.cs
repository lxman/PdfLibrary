using System.Reflection;
using PdfLibrary.Conformance;
using PdfLibrary.Editing;

namespace PdfLibrary.Tests.PublicSurface;

// The repair side of the editor is Pellucid's product, not the library's (release spec 2026-09-06, D2).
// It stays in this assembly and Pellucid reaches it through InternalsVisibleTo. This guard is the narrow,
// fast check for the families that were public on master before 2.6.0; the checked-in PublicAPI.*.txt
// files (Task 4) are the exhaustive contract.
public class HiddenRepairSurfaceTests
{
    // Type-name fragments that identify the hidden families. DocumentProofPreview is the one
    // deliberate exception (a read-only fact API used by Pellucid.Core).
    private static readonly string[] HiddenTypeMarkers =
        ["Repair", "Refusal", "OwnerKind", "ProhibitedAction", ".Fonts.Remediation."];

    private static readonly string[] HiddenEditorMethods =
    [
        "EmbedProgram", "SetCidSet", "SetCharSet", "SetToUnicode", "ReplaceProgramBytes",
        "ReplaceCompositeProgram", "HasFont", "CanSetCidToGidMapIdentity", "SetCidToGidMapIdentity",
        "CanRemoveSymbolicEncoding", "RemoveSymbolicEncoding",
    ];

    [Fact]
    public void No_exported_type_belongs_to_the_repair_family()
    {
        List<string> offenders = typeof(PdfDocumentEditor).Assembly.GetExportedTypes()
            .Select(t => t.FullName!)
            .Where(n => n != "PdfLibrary.Editing.DocumentProofPreview")
            .Where(n => HiddenTypeMarkers.Any(n.Contains))
            .OrderBy(n => n)
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void PdfDocumentEditor_exposes_no_repair_or_font_program_mutation()
    {
        HashSet<string> names = typeof(PdfDocumentEditor)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToHashSet();
        List<string> offenders = names
            .Where(n => (n.StartsWith("Preview", StringComparison.Ordinal) && n != "PreviewDocumentProof")
                        || n.StartsWith("Repair", StringComparison.Ordinal)
                        || HiddenEditorMethods.Contains(n))
            .OrderBy(n => n)
            .ToList();
        Assert.Empty(offenders);
        Assert.Contains("PreviewDocumentProof", names);
        Assert.Contains("SetFileId", names);
        Assert.Contains("ConsolidateOutputIntents", names);
        Assert.Contains("ReplaceOutputIntentProfile", names);
    }

    [Fact]
    public void XmpConformance_exposes_only_reads()
    {
        HashSet<string> names = typeof(XmpConformance)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.Name)
            .ToHashSet();
        Assert.DoesNotContain("PreviewExtensionSchemaStructureRepairs", names);
        Assert.DoesNotContain("RepairExtensionSchemaStructure", names);
        Assert.Contains("ClassifyProperties", names);
        Assert.Contains("ModernEquivalentOf", names);
    }
}
