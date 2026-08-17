using System.Collections.Generic;

namespace PdfLibrary.Tests.Fonts.Embedded;

/// <summary>
/// The minimal-TrueType byte builders shared by FontProgramZeroAdvanceTests (issue 26) and
/// ProgramWidthResolverTests (F-4a Task 1): a font whose lone (1,0) Mac-Roman format-6 cmap
/// subtable maps code 10 (LINE FEED) to gid 1, with a parameterizable gid-1 advance so both the
/// zero-advance skip and an ordinary measurable width mismatch can be exercised against the exact
/// same program shape. Promoted out of FontProgramZeroAdvanceTests (F-4a Task 1) rather than
/// duplicated, so a future change to the fixture's shape only needs to happen once.
/// </summary>
internal static class ZeroAdvanceSfntFixture
{
    private static void U16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
    private static void U32(List<byte> b, uint v)
    { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }

    public static byte[] Head(ushort macStyle = 0)
    {
        var b = new List<byte>();
        U32(b, 0x00010000);            // version 1.0
        U32(b, 0);                     // fontRevision
        U32(b, 0);                     // checkSumAdjustment
        U32(b, 0x5F0F3CF5);            // magicNumber
        U16(b, 0);                     // flags
        U16(b, 1000);                  // unitsPerEm
        for (var i = 0; i < 16; i++) b.Add(0); // created + modified (2 × longdatetime)
        U16(b, 0); U16(b, 0); U16(b, 0); U16(b, 0); // xMin yMin xMax yMax
        U16(b, macStyle);              // macStyle (bit 0 bold, bit 1 italic — issue 43's style pin)
        U16(b, 8);                     // lowestRecPPEM
        U16(b, 2);                     // fontDirectionHint
        U16(b, 0);                     // indexToLocFormat
        U16(b, 0);                     // glyphDataFormat
        return b.ToArray();            // 54 bytes
    }

    public static byte[] Maxp(ushort numGlyphs)
    {
        var b = new List<byte>();
        U32(b, 0x00010000);
        U16(b, numGlyphs);
        for (var i = 0; i < 13; i++) U16(b, 0); // maxPoints … maxComponentDepth
        return b.ToArray();            // 32 bytes
    }

    public static byte[] Hhea(ushort numberOfHMetrics)
    {
        var b = new List<byte>();
        U32(b, 0x00010000);
        U16(b, 800);                   // ascender
        U16(b, unchecked((ushort)-200)); // descender
        U16(b, 0);                     // lineGap
        U16(b, 500);                   // advanceWidthMax
        for (var i = 0; i < 3; i++) U16(b, 0); // minLSB, minRSB, xMaxExtent
        U16(b, 1); U16(b, 0); U16(b, 0);       // caretSlopeRise/Run, caretOffset
        for (var i = 0; i < 4; i++) U16(b, 0); // reserved
        U16(b, 0);                     // metricDataFormat
        U16(b, numberOfHMetrics);
        return b.ToArray();            // 36 bytes
    }

    /// <summary>hmtx: gid 0 advances 500; gid 1 advances <paramref name="gid1Advance"/> (0
    /// reproduces issue 26's zero-advance defect; a nonzero value gives an ordinary measurable
    /// mismatch).</summary>
    public static byte[] Hmtx(ushort gid1Advance = 0)
    {
        var b = new List<byte>();
        U16(b, 500); U16(b, 0);        // gid 0: advance 500, lsb 0
        U16(b, gid1Advance); U16(b, 0); // gid 1: advance gid1Advance, lsb 0
        return b.ToArray();
    }

    /// <summary>A lone (1,0) Mac-Roman format-6 subtable mapping code 10 → gid 1.</summary>
    public static byte[] CmapMacFormat6()
    {
        var b = new List<byte>();
        U16(b, 0);                     // table version
        U16(b, 1);                     // numTables
        U16(b, 1); U16(b, 0);          // platform 1 (Macintosh), encoding 0 (Roman)
        U32(b, 12);                    // subtable offset
        U16(b, 6);                     // format 6
        U16(b, 12);                    // length (5 × u16 header + 1 × u16 entry)
        U16(b, 0);                     // language
        U16(b, 10);                    // firstCode = 10 (LINE FEED)
        U16(b, 1);                     // entryCount
        U16(b, 1);                     // glyphIndexArray = [gid 1]
        return b.ToArray();
    }

    public static byte[] FontBytes(ushort gid1Advance = 0, ushort macStyle = 0) => MinimalSfnt.Build(
        ("head", Head(macStyle)),
        ("maxp", Maxp(2)),
        ("hhea", Hhea(2)),
        ("hmtx", Hmtx(gid1Advance)),
        ("cmap", CmapMacFormat6()),
        ("glyf", new byte[4]));        // content unused; presence required for IsValid

    /// <summary>hmtx for a font whose tail glyphs share gid 0's long metric (numberOfHMetrics=1):
    /// gid 0 is the sole long metric (advance <paramref name="gid0Advance"/>, lsb 0); gids
    /// 1..<paramref name="numGlyphs"/>-1 ride the tail as bare lsbs (all 0), inheriting gid 0's
    /// advance per the shared-tail rule. Used by SfntAdvancePatcherTests' expansion case (F-4a
    /// Task 2), which patches a gid past numberOfHMetrics and must promote it into a long metric.
    /// </summary>
    public static byte[] HmtxSharedTail(ushort numGlyphs, ushort gid0Advance)
    {
        var b = new List<byte>();
        U16(b, gid0Advance); U16(b, 0);          // gid 0: the sole long metric
        for (var gid = 1; gid < numGlyphs; gid++) U16(b, 0); // trailing lsb array, all zero
        return b.ToArray();
    }

    /// <summary>A font with a shared hmtx tail: <paramref name="numGlyphs"/> glyphs, only
    /// <paramref name="numberOfHMetrics"/> long metrics. Same cmap/glyf shape as
    /// <see cref="FontBytes"/> so the fixture is otherwise unremarkable.</summary>
    public static byte[] FontBytesSharedTail(
        ushort numGlyphs, ushort numberOfHMetrics, ushort gid0Advance = 500) => MinimalSfnt.Build(
        ("head", Head()),
        ("maxp", Maxp(numGlyphs)),
        ("hhea", Hhea(numberOfHMetrics)),
        ("hmtx", HmtxSharedTail(numGlyphs, gid0Advance)),
        ("cmap", CmapMacFormat6()),
        ("glyf", new byte[4]));
}
