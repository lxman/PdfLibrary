using System;
using System.Collections.Generic;
using System.Text;

namespace PdfLibrary.Tests.Fonts.Embedded;

/// <summary>
/// Builds synthetic sfnt programs: a header, a table directory, and whatever payloads the caller
/// hands over. Deliberately NOT a valid-font builder — it builds a DIRECTORY OVER PAYLOADS, and the
/// payloads are meant to be broken. A caller wanting a program that parses cleanly should use
/// <c>MinimalCff</c> or the corpus, not this.
/// <para>Validated against reality before being trusted: corrupting a real TrueType font
/// (Alef-Regular) one table at a time produced the same stage and the same exception type as the
/// synthetic equivalents. That check is why this file exists instead of a vendored font binary.</para>
/// <para>Not linked into FontParser.Tests the way MinimalCff is. MinimalCff earned that because
/// parser-level and metrics-level charset tests needed the same fixtures; nothing outside this
/// assembly needs this one yet. Add the Compile/Link item when a second consumer appears.</para>
/// </summary>
internal static class MinimalSfnt
{
    /// <summary>A table too short for its reader — the shape that throws for head, maxp, hhea, name.
    /// NOT the shape that throws for cmap, which returns cleanly when short.</summary>
    public static byte[] TooShort() => new byte[4];

    /// <summary>A table of plausible size but garbage content — the shape cmap needs.</summary>
    public static byte[] Garbage(int length)
    {
        var b = new byte[length];
        Array.Fill(b, (byte)0xFF);
        return b;
    }

    /// <summary>A 54-byte all-zero head. Parses SUCCESSFULLY and yields UnitsPerEm 0 — the defect
    /// Task 4 clamps. Used here because a parseable head is a precondition for reaching the lazy
    /// loca/glyf stage at all.</summary>
    public static byte[] ZeroHead() => new byte[54];

    /// <summary>A 6-byte maxp (version 0.5 + numGlyphs). NumGlyphs must be non-zero or
    /// LoadGlyphTables returns before it reaches the loca reader.</summary>
    public static byte[] Maxp(ushort numGlyphs) =>
        [0x00, 0x00, 0x50, 0x00, (byte)(numGlyphs >> 8), (byte)numGlyphs];

    /// <summary>Header + directory + payloads. Tables are sorted by tag, as the format requires.
    /// Checksums are written as zero; nothing in the reader validates them.</summary>
    public static byte[] Build(params (string Tag, byte[] Data)[] tables)
    {
        Array.Sort(tables, (a, b) => string.CompareOrdinal(a.Tag, b.Tag));

        var data = new List<byte>();
        U32(data, 0x00010000);        // sfntVersion: TrueType outlines
        U16(data, tables.Length);
        U16(data, 0); U16(data, 0); U16(data, 0); // searchRange/entrySelector/rangeShift: unread

        int offset = 12 + tables.Length * 16;
        foreach ((string tag, byte[] payload) in tables)
        {
            data.AddRange(Encoding.ASCII.GetBytes(tag));
            U32(data, 0);             // checksum: not validated
            U32(data, offset);
            U32(data, payload.Length);
            offset += payload.Length;
        }

        foreach ((_, byte[] payload) in tables) data.AddRange(payload);
        return data.ToArray();
    }

    private static void U16(List<byte> d, int v) { d.Add((byte)(v >> 8)); d.Add((byte)v); }

    private static void U32(List<byte> d, int v)
    {
        d.Add((byte)(v >> 24)); d.Add((byte)(v >> 16)); d.Add((byte)(v >> 8)); d.Add((byte)v);
    }
}
