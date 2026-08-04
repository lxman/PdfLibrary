using System.Text;

namespace PdfLibrary.Fonts;

/// <summary>Reads a face's identity from sfnt bytes using only the table directory, `name` and
/// `head`. Deliberately does NOT use FontParser.SfntFont: this runs over every installed font at
/// index time, and it must not pay for parsing glyph data it will never look at.</summary>
internal static class SfntNameReader
{
    /// <summary>Number of faces: the `ttcf` header's count for a collection, otherwise 1.</summary>
    public static int FaceCount(byte[] data)
    {
        if (data.Length < 12) return 0;
        if (!IsTtc(data)) return 1;
        var n = (int)U32(data, 8);
        return n is > 0 and < 0x10000 ? n : 0;
    }

    public static FontFaceRecord? ReadFace(byte[] data, int faceIndex, string path)
    {
        try
        {
            long b = 0;
            if (IsTtc(data))
            {
                if (faceIndex >= FaceCount(data)) return null;
                b = U32(data, 12 + faceIndex * 4);
            }
            else if (faceIndex != 0) return null;

            if (b + 12 > data.Length) return null;
            int numTables = U16(data, b + 4);

            long nameOff = 0, headOff = 0;
            for (var i = 0; i < numTables; i++)
            {
                long rec = b + 12 + i * 16;
                if (rec + 16 > data.Length) return null;
                // Table offsets inside a .ttc are FILE-absolute, not face-relative.
                long off = U32(data, rec + 8);
                if (Tag(data, rec) == "name") nameOff = off;
                else if (Tag(data, rec) == "head") headOff = off;
            }
            if (nameOff == 0 || nameOff + 6 > data.Length) return null;

            var macStyle = 0;
            if (headOff > 0 && headOff + 46 <= data.Length) macStyle = U16(data, headOff + 44);

            var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string ps = "", english = "", subfamily = "";
            int count = U16(data, nameOff + 2), storage = U16(data, nameOff + 4);
            for (var i = 0; i < count; i++)
            {
                long r = nameOff + 6 + i * 12;
                if (r + 12 > data.Length) break;
                int pid = U16(data, r), lang = U16(data, r + 4), nid = U16(data, r + 6);
                int len = U16(data, r + 8), off = U16(data, r + 10);
                if (nid is not (1 or 2 or 6 or 16 or 17)) continue;

                long s = nameOff + storage + off;
                if (len == 0 || s + len > data.Length) continue;
                string v = (pid == 3
                    ? Encoding.BigEndianUnicode.GetString(data, (int)s, len)
                    : Encoding.ASCII.GetString(data, (int)s, len)).Trim('\0').Trim();
                if (v.Length == 0) continue;

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

    private static bool IsTtc(byte[] d) =>
        d.Length >= 4 && d[0] == 't' && d[1] == 't' && d[2] == 'c' && d[3] == 'f';

    private static string Tag(byte[] d, long i) => Encoding.ASCII.GetString(d, (int)i, 4);
    private static int U16(byte[] d, long i) => (d[i] << 8) | d[i + 1];
    private static uint U32(byte[] d, long i) =>
        ((uint)d[i] << 24) | ((uint)d[i + 1] << 16) | ((uint)d[i + 2] << 8) | d[i + 3];

    /// <summary>Stream-based twin of <see cref="FaceCount(byte[])"/>: reads a few bytes of a
    /// seekable stream instead of the whole file into memory. Used by <see cref="FontMetadataIndex"/>
    /// so indexing 732 installed fonts touches KBs, not the ~471 MB the files add up to.</summary>
    public static int FaceCount(Stream s)
    {
        if (s.Length < 12) return 0;
        if (!IsTtc(s)) return 1;
        var n = (int)U32(s, 8);
        return n is > 0 and < 0x10000 ? n : 0;
    }

    /// <summary>Stream-based twin of <see cref="ReadFace(byte[], int, string)"/>. Mirrors its logic
    /// exactly, seeking for each field instead of indexing into an in-memory buffer.</summary>
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
                string v = (pid == 3
                    ? Encoding.BigEndianUnicode.GetString(raw)
                    : Encoding.ASCII.GetString(raw)).Trim('\0').Trim();
                if (v.Length == 0) continue;

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
