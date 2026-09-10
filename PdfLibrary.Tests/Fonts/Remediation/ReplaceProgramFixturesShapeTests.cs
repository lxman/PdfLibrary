using System.Linq;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Remediation;

/// <summary>
/// Per-holder merge program, Task 1: pins the fixture SHAPES later tasks' merge tests rely on, via
/// <see cref="FontInventory"/> — the same read model <c>FontRemediationPlanner</c> groups over. See
/// <see cref="ReplaceProgramFixtures.SharedDescendantDoc"/> and
/// <see cref="ReplaceProgramFixtures.SharedDescriptorDoc"/>.
/// </summary>
public sealed class ReplaceProgramFixturesShapeTests
{
    [Fact]
    public void SharedDescendantDoc_two_wrappers_one_program_holder()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc();
        IReadOnlyList<FontInventoryEntry> inventory = FontInventory.Read(doc);
        var composites = inventory.Where(e => e.ProgramHolderId is not null).ToList();
        Assert.Equal(2, composites.Count);
        Assert.Single(composites.Select(e => e.ProgramHolderId!.Value.ObjectNumber).Distinct());
        Assert.All(composites, e => Assert.NotEmpty(e.UsedCodes));
    }

    [Fact]
    public void SharedDescriptorDoc_two_holders_one_descriptor()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescriptorDoc();
        IReadOnlyList<FontInventoryEntry> inventory = FontInventory.Read(doc);
        var holders = inventory.Where(e => e.ProgramHolderId is not null)
            .Select(e => e.ProgramHolderId!.Value.ObjectNumber).Distinct().ToList();
        Assert.Equal(2, holders.Count);
        // Both holders' /FontDescriptor entries resolve to one object — the group identity Task 4's
        // planner grouping keys on. Read it the raw-dictionary way, the same way
        // FontRemediationPlanner.DescriptorObjectNumber does.
        List<int?> descriptors = [.. holders.Select(h =>
            (doc.GetObject(h) as PdfDictionary)?.Get("FontDescriptor") is PdfIndirectReference r
                ? r.ObjectNumber : (int?)null)];
        Assert.Equal(2, descriptors.Count);
        Assert.NotNull(descriptors[0]);
        Assert.Equal(descriptors[0], descriptors[1]);
    }

    [Fact]
    public void SharedProgramStreamDoc_two_descriptors_one_program_stream()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedProgramStreamDoc();
        IReadOnlyList<FontInventoryEntry> inventory = FontInventory.Read(doc);
        var holders = inventory.Where(e => e.ProgramHolderId is not null)
            .Select(e => e.ProgramHolderId!.Value.ObjectNumber).Distinct().ToList();
        Assert.True(holders.Count == 2,
            $"Expected two holders; inventory was: {string.Join("; ", inventory.Select(e =>
                $"id={e.Id.ObjectNumber}, holder={e.ProgramHolderId?.ObjectNumber}, kind={e.Kind}"))}");

        List<int> descriptors = [.. holders.Select(h =>
            Assert.IsType<PdfIndirectReference>(
                Assert.IsType<PdfDictionary>(doc.GetObject(h)).Get("FontDescriptor")).ObjectNumber)];
        Assert.Equal(2, descriptors.Distinct().Count());

        List<int> programs = [.. descriptors.Select(d =>
            Assert.IsType<PdfIndirectReference>(
                Assert.IsType<PdfDictionary>(doc.GetObject(d)).Get("FontFile2")).ObjectNumber)];
        Assert.Single(programs.Distinct());
        Assert.Equal(3, programs[0]);
    }
}
