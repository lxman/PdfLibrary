using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Tests.Fonts.Embedded;
using Xunit;
using static PdfLibrary.Tests.Fonts.Embedded.ZeroAdvanceSfntFixture;

namespace PdfLibrary.Tests.Fonts.Remediation;

/// <summary>
/// F-4a Task 2: raw hmtx advance patching against opaque sfnt table bytes — no FontParser
/// dependency. Fixtures reuse the promoted <see cref="ZeroAdvanceSfntFixture"/> builder set from
/// Task 1 (a static import brings the bare builder names into scope, matching the brief's
/// unqualified calls), extended here with <see cref="ZeroAdvanceSfntFixture.FontBytesSharedTail"/>
/// for the expansion case.
/// </summary>
public class SfntAdvancePatcherTests
{
    // Fixture: the FontProgramZeroAdvanceTests builder set (Head/Maxp/Hhea/Hmtx/CmapMacFormat6),
    // shared via ZeroAdvanceSfntFixture per Task 1's promotion. Baseline font: numGlyphs 2,
    // numberOfHMetrics 2, gid0 advance 500, gid1 advance 0.

    [Fact]
    public void In_place_patch_changes_only_the_target_advance()
    {
        byte[] original = FontBytes();
        byte[]? patched = SfntAdvancePatcher.Patch(
            original, new Dictionary<ushort, ushort> { [1] = 507 }, out string? reason);
        Assert.Null(reason);
        var metrics = new EmbeddedFontMetrics(patched!);
        Assert.True(metrics.IsValid);
        Assert.Equal(507, metrics.GetAdvanceWidth(1));
        Assert.Equal(500, metrics.GetAdvanceWidth(0)); // untouched neighbour
    }

    [Fact]
    public void Untouched_tables_are_byte_identical()
    {
        // Locate each table in original and patched via their directories; every table except
        // hmtx and head (checkSumAdjustment) must be byte-identical; hhea identical when no
        // expansion happened. Write a small local directory reader for the assertion.
        byte[] original = FontBytes();
        byte[]? patched = SfntAdvancePatcher.Patch(
            original, new Dictionary<ushort, ushort> { [1] = 507 }, out string? reason);
        Assert.Null(reason);

        Dictionary<string, byte[]> originalTables = ReadTables(original);
        Dictionary<string, byte[]> patchedTables = ReadTables(patched!);

        Assert.Equal(originalTables.Count, patchedTables.Count);
        foreach ((string tag, byte[] data) in originalTables)
        {
            Assert.True(patchedTables.TryGetValue(tag, out byte[]? patchedData), $"missing table '{tag}'");
            if (tag == "hmtx")
                continue; // the table under test — expected to differ.
            if (tag == "head")
            {
                // Everything except checkSumAdjustment (bytes 8-11) must be unchanged.
                Assert.Equal(data.Length, patchedData!.Length);
                for (var i = 0; i < data.Length; i++)
                {
                    if (i is >= 8 and < 12) continue;
                    Assert.Equal(data[i], patchedData[i]);
                }
                continue;
            }
            // hhea: no expansion happened for this baseline fixture (maxGid 1 < numberOfHMetrics
            // 2), so it must be byte-identical too.
            Assert.Equal(data, patchedData);
        }
    }

    [Fact]
    public void Shared_tail_gid_forces_hmtx_expansion_and_hhea_update()
    {
        // Variant fixture: numGlyphs 3, numberOfHMetrics 1 (gid0 long metric; gids 1-2 ride the
        // tail with 2 trailing lsbs). Patch gid 2 → 480. Expect: numberOfHMetrics becomes 3 (or
        // maxPatchedGid+1 == 3), GetAdvanceWidth(2) == 480, GetAdvanceWidth(1) == gid0's shared
        // advance preserved, and GetAdvanceWidth(0) unchanged.
        byte[] original = FontBytesSharedTail(numGlyphs: 3, numberOfHMetrics: 1, gid0Advance: 500);
        byte[]? patched = SfntAdvancePatcher.Patch(
            original, new Dictionary<ushort, ushort> { [2] = 480 }, out string? reason);
        Assert.Null(reason);

        var metrics = new EmbeddedFontMetrics(patched!);
        Assert.True(metrics.IsValid);
        Assert.Equal(3, metrics.NumberOfHMetrics);
        Assert.Equal(480, metrics.GetAdvanceWidth(2));
        Assert.Equal(500, metrics.GetAdvanceWidth(1)); // gid0's shared advance, preserved
        Assert.Equal(500, metrics.GetAdvanceWidth(0)); // unchanged
    }

    [Fact]
    public void Whole_file_checksum_reconciles()
    {
        // Sum every big-endian u32 of the patched file (zero-padded to 4) — with
        // checkSumAdjustment INCLUDED, the total must equal 0xB1B0AFBA (the defining property).
        byte[] original = FontBytes();
        byte[]? patched = SfntAdvancePatcher.Patch(
            original, new Dictionary<ushort, ushort> { [1] = 507 }, out string? reason);
        Assert.Null(reason);

        uint sum = 0;
        for (var i = 0; i < patched!.Length; i += 4)
        {
            uint word = 0;
            for (var b = 0; b < 4; b++)
                word = (word << 8) | (uint)(i + b < patched.Length ? patched[i + b] : 0);
            sum = unchecked(sum + word);
        }
        Assert.Equal(0xB1B0AFBAu, sum);
    }

    [Fact]
    public void Missing_hmtx_fails_with_a_named_reason()
    {
        byte[] noHmtx = MinimalSfnt.Build(("head", Head()), ("maxp", Maxp(2)), ("hhea", Hhea(2)));
        Assert.Null(SfntAdvancePatcher.Patch(noHmtx, new Dictionary<ushort, ushort> { [1] = 507 }, out string? reason));
        Assert.Contains("hmtx", reason);
    }

    [Fact]
    public void Otto_version_tag_is_accepted()
    {
        // Rebuild the fixture bytes with the sfnt version u32 replaced by 0x4F54544F ('OTTO')
        // (flip the first four bytes of MinimalSfnt.Build's output) and assert Patch succeeds.
        byte[] original = FontBytes();
        original[0] = (byte)'O';
        original[1] = (byte)'T';
        original[2] = (byte)'T';
        original[3] = (byte)'O';

        byte[]? patched = SfntAdvancePatcher.Patch(
            original, new Dictionary<ushort, ushort> { [1] = 507 }, out string? reason);
        Assert.Null(reason);
        Assert.NotNull(patched);
    }

    [Fact]
    public void A_gid_at_or_beyond_numGlyphs_fails_rather_than_writes()
    {
        Assert.Null(SfntAdvancePatcher.Patch(FontBytes(), new Dictionary<ushort, ushort> { [9] = 500 }, out string? reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void Corrupt_directory_entry_with_overflowing_offset_length_fails_rather_than_throws()
    {
        // A directory entry offset=0xFFFFFFF0, length=0x20 sums to 0x10 in uint arithmetic
        // (wraps past uint.MaxValue) — an addition-based bounds check would pass it, and the
        // cast to int inside program.AsSpan((int)offset, (int)length) would then go negative and
        // throw ArgumentOutOfRangeException instead of Patch returning null + a reason. Hand-edit
        // a valid fixture's first directory entry (offset at entry+8, length at entry+12) to pin
        // the subtraction-based fix.
        byte[] program = FontBytes();
        const int entry = 12; // the first table's directory entry, right after the 12-byte header
        WriteU32(program, entry + 8, 0xFFFFFFF0);  // offset
        WriteU32(program, entry + 12, 0x20);       // length

        byte[]? patched = null;
        string? reason = null;
        Exception? thrown = Record.Exception(() =>
            patched = SfntAdvancePatcher.Patch(
                program, new Dictionary<ushort, ushort> { [1] = 507 }, out reason));

        Assert.Null(thrown);
        Assert.Null(patched);
        Assert.NotNull(reason);
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    /// <summary>Minimal sfnt table-directory reader for the byte-identity assertion — reads only
    /// what the assertion needs (tag/offset/length), deliberately not reusing
    /// SfntAdvancePatcher's own directory parser so the test does not validate itself against its
    /// own logic.</summary>
    private static Dictionary<string, byte[]> ReadTables(byte[] program)
    {
        int numTables = (program[4] << 8) | program[5];
        var result = new Dictionary<string, byte[]>();
        for (var i = 0; i < numTables; i++)
        {
            int entry = 12 + i * 16;
            string tag = System.Text.Encoding.ASCII.GetString(program, entry, 4);
            uint offset = (uint)((program[entry + 8] << 24) | (program[entry + 9] << 16)
                | (program[entry + 10] << 8) | program[entry + 11]);
            uint length = (uint)((program[entry + 12] << 24) | (program[entry + 13] << 16)
                | (program[entry + 14] << 8) | program[entry + 15]);
            result[tag] = program.AsSpan((int)offset, (int)length).ToArray();
        }
        return result;
    }
}
