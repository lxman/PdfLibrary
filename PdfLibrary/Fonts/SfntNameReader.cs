using System.Text;

namespace PdfLibrary.Fonts;

/// <summary>Reads a face's identity from sfnt bytes using only the table directory, `name` and
/// `head`. Deliberately does NOT use FontParser.SfntFont: this runs over every installed font at
/// index time, and it must not pay for parsing glyph data it will never look at.</summary>
internal static class SfntNameReader
{
    /// <summary>Number of faces: the `ttcf` header's count for a collection, otherwise 1. Wraps the
    /// stream implementation — the two used to be separate copies of one algorithm, and the
    /// platform-0 decode fix had to be made twice before they were collapsed.</summary>
    public static int FaceCount(byte[] data) => FaceCount(new MemoryStream(data, writable: false));

    /// <summary>In-memory twin of <see cref="ReadFace(Stream, int, string)"/>, for callers holding
    /// bytes a third-party provider handed them rather than a file they can seek. Wrapping an array
    /// already in memory reads nothing new, so the never-read-a-whole-font-file rule that governs
    /// indexing is not in play here.</summary>
    public static FontFaceRecord? ReadFace(byte[] data, int faceIndex, string path) =>
        ReadFace(new MemoryStream(data, writable: false), faceIndex, path);

    /// <summary>Number of faces: the `ttcf` header's count for a collection, otherwise 1. Reads a
    /// few bytes of a seekable stream instead of the whole file into memory. Used by
    /// <see cref="FontMetadataIndex"/> so indexing 732 installed fonts touches KBs, not the ~471 MB
    /// the files add up to.</summary>
    public static int FaceCount(Stream s)
    {
        if (s.Length < 12) return 0;
        if (!IsTtc(s)) return 1;
        var n = (int)U32(s, 8);
        return n is > 0 and < 0x10000 ? n : 0;
    }

    /// <summary>Reads one face's identity — PostScript name, families, subfamily and style bits —
    /// from the `name` and `head` tables, seeking to each field rather than loading the file.</summary>
    public static FontFaceRecord? ReadFace(Stream s, int faceIndex, string path)
    {
        try
        {
            long b = 0;
            if (IsTtc(s))
            {
                if (faceIndex >= FaceCount(s)) return null;
                b = U32(s, 12 + faceIndex * 4);
            }
            else if (faceIndex != 0) return null;

            if (b + 12 > s.Length) return null;
            int numTables = U16(s, b + 4);

            long nameOff = 0, headOff = 0;
            for (var i = 0; i < numTables; i++)
            {
                long rec = b + 12 + i * 16;
                if (rec + 16 > s.Length) return null;
                // Table offsets inside a .ttc are FILE-absolute, not face-relative.
                long off = U32(s, rec + 8);
                if (Tag(s, rec) == "name") nameOff = off;
                else if (Tag(s, rec) == "head") headOff = off;
            }
            if (nameOff == 0 || nameOff + 6 > s.Length) return null;

            var macStyle = 0;
            if (headOff > 0 && headOff + 46 <= s.Length) macStyle = U16(s, headOff + 44);

            var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string ps = "", english = "", subfamily = "";
            int count = U16(s, nameOff + 2), storage = U16(s, nameOff + 4);
            for (var i = 0; i < count; i++)
            {
                long r = nameOff + 6 + i * 12;
                if (r + 12 > s.Length) break;
                int pid = U16(s, r), lang = U16(s, r + 4), nid = U16(s, r + 6);
                int len = U16(s, r + 8), off = U16(s, r + 10);
                if (nid is not (1 or 2 or 6 or 16 or 17)) continue;

                long strAt = nameOff + storage + off;
                if (len == 0 || strAt + len > s.Length) continue;
                byte[] raw = ReadBytes(s, strAt, len);
                // Platform 0 (Unicode) strings are UTF-16BE exactly as platform 3 (Windows) are;
                // only the legacy Mac platform 1 is a byte encoding. ASCII-decoding a platform-0
                // record yields "T\0e\0s\0t\0..." whose INTERIOR NULs Trim('\0') cannot remove, so
                // a font whose only records are platform 0 — spec-legal, emitted by some OTF/CJK
                // toolchains — would index entirely under garbage keys.
                bool utf16 = pid is 3 or 0;
                string v = (utf16
                    ? Encoding.BigEndianUnicode.GetString(raw)
                    : Encoding.ASCII.GetString(raw)).Trim('\0').Trim();
                if (v.Length == 0) continue;

                // Platform 0 deliberately does NOT count as English: its language field is
                // language-neutral, so a platform-0 record is no evidence the string IS English, and
                // treating it as such would let the LAST such record overwrite a genuine 0x409 one.
                // Platform-0-only fonts still resolve, via the `english.Length == 0` fallback below.
                bool isEnglish = (pid == 3 && lang == 0x409) || (pid == 1 && lang == 0);
                switch (nid)
                {
                    case 6 when ps.Length == 0 || isEnglish: ps = v; break;
                    case 1 or 16:
                        families.Add(v);
                        // An English record always wins, whatever the record order.
                        if (isEnglish || english.Length == 0) english = v;
                        break;
                    case 2 or 17 when subfamily.Length == 0 || isEnglish: subfamily = v; break;
                }
            }
            if (ps.Length == 0 && families.Count == 0) return null;

            bool italic = (macStyle & 0x2) != 0
                       || subfamily.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                       || subfamily.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
            bool bold = (macStyle & 0x1) != 0
                     || subfamily.Contains("Bold", StringComparison.OrdinalIgnoreCase);

            return new FontFaceRecord(path, faceIndex, ps, families, english, subfamily, italic, bold);
        }
        catch
        {
            // A malformed font must not break indexing of the other 700.
            return null;
        }
    }

    private static bool IsTtc(Stream s)
    {
        if (s.Length < 4) return false;
        byte[] tag = ReadBytes(s, 0, 4);
        return tag[0] == 't' && tag[1] == 't' && tag[2] == 'c' && tag[3] == 'f';
    }

    private static string Tag(Stream s, long i) => Encoding.ASCII.GetString(ReadBytes(s, i, 4));
    private static int U16(Stream s, long i) { byte[] b = ReadBytes(s, i, 2); return (b[0] << 8) | b[1]; }
    private static uint U32(Stream s, long i)
    {
        byte[] b = ReadBytes(s, i, 4);
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    private static byte[] ReadBytes(Stream s, long offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > s.Length) throw new EndOfStreamException();
        s.Seek(offset, SeekOrigin.Begin);
        var buf = new byte[length];
        var read = 0;
        while (read < length)
        {
            int n = s.Read(buf, read, length - read);
            if (n <= 0) throw new EndOfStreamException();
            read += n;
        }
        return buf;
    }
}
