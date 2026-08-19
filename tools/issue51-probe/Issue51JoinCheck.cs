using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// THROWAWAY — companion to <see cref="Issue51FalseEmptyProbe"/>. DELETE with it.
///
/// <para>The probe reported ZERO issue-44 filter risk. That number is only trustworthy if the join it
/// rests on actually lands: the probe matches an AP-drawn font's dictionary object number against
/// <c>FontInventoryEntry.Id.ObjectNumber</c>, and a join that never matches produces the same zero as
/// a genuine absence. This dumps the raw inventory for the six documents the probe flagged, so the
/// zero can be read as evidence rather than assumed.</para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class Issue51JoinCheck
{
    private static readonly string[] Files =
    [
        @"D:\PdfCorpora\real-world\cc-main-2021-31-sample\0000_0000849.pdf",
        @"D:\PdfCorpora\real-world\local-708\Dynamic.pdf",
        @"D:\PdfCorpora\real-world\cc-main-2021-31-sample\0000_0000425.pdf",
        @"D:\PdfCorpora\real-world\cc-main-2021-31-sample\6000_6000059.pdf",
        @"D:\PdfCorpora\real-world\cc-main-2021-31-sample\6000_6000887.pdf",
        @"D:\PdfCorpora\real-world\cc-main-2021-31-sample\2000_2000901.pdf",
    ];

    [Fact]
    public void Dump()
    {
        var sb = new StringBuilder();

        foreach (string file in Files)
        {
            sb.AppendLine($"=== {Path.GetFileName(file)} ===");
            using PdfDocument doc = PdfDocument.Load(
                new FileStream(file, FileMode.Open, FileAccess.Read), string.Empty);

            IReadOnlyList<FontInventoryEntry> inv = FontInventory.Read(doc);
            FontInventoryEntry[] composite = [.. inv.Where(e =>
                e.Kind is FontKind.Type0CidType0 or FontKind.Type0CidType2)];

            sb.AppendLine($"inventory entries : {inv.Count}  (composite {composite.Length})");
            sb.AppendLine($"empty UsedCodes   : {inv.Count(e => e.UsedCodes.Count == 0)} " +
                          $"(composite {composite.Count(e => e.UsedCodes.Count == 0)})");

            // Holder groups that mix a drawn and an undrawn member — the shape ExpandHolderGroup filters.
            var mixed = 0;
            foreach (IGrouping<int, FontInventoryEntry> g in inv
                         .Where(e => e.ProgramHolderId is not null)
                         .GroupBy(e => e.ProgramHolderId!.Value.ObjectNumber))
            {
                bool anyDrawn = g.Any(e => e.UsedCodes.Count > 0);
                bool anyUndrawn = g.Any(e => e.UsedCodes.Count == 0);
                if (!anyDrawn || !anyUndrawn)
                    continue;

                mixed++;
                sb.AppendLine($"  MIXED holder {g.Key}: " + string.Join(", ",
                    g.Select(e => $"obj {e.Id.ObjectNumber}({e.Kind},{e.UsedCodes.Count} codes)")));
            }

            sb.AppendLine($"mixed holder groups: {mixed}");

            // Does ANY inventory entry carry an object number at all — proves the join key is populated.
            sb.AppendLine($"sample ids        : " + string.Join(", ",
                inv.Take(6).Select(e => $"{e.Id.ObjectNumber}->{e.ProgramHolderId?.ObjectNumber.ToString() ?? "-"}")));
            sb.AppendLine();
        }

        File.WriteAllText(
            Environment.GetEnvironmentVariable("ISSUE51_JOIN")
            ?? Path.Combine(Path.GetTempPath(), "issue51-join.txt"), sb.ToString());
    }
}
