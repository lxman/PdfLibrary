using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// A substituted font that lives in a TrueType Collection must be opened at the face matching the
/// REQUESTED style, not at face 0.
///
/// <para>Found on macOS 2026-08-04: <c>/BaseFont /NewCenturySchlbk-Italic</c> with no embedded program
/// resolves through the italic serif candidate list, whose first four entries do not exist there. The
/// fifth, <c>"Times"</c>, matches <c>/System/Library/Fonts/Times.ttc</c> — a collection whose four faces
/// are Regular / Bold / Italic / Bold Italic. The resolver opened the right FILE and took the Regular
/// face, so the italic was silently dropped and the GWG 9.0 Font Support patch rendered upright against
/// its own embedded reference row.</para>
/// </summary>
public class SubstituteFontFaceSelectionTests
{
    private static byte[] BareFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    /// <summary>Offset of <c>macStyle</c> within the <c>head</c> table (bit 0 Bold, bit 1 Italic).</summary>
    private const int MacStyleOffset = 44;

    /// <summary>Builds a 2-face .ttc holding two independent copies of <paramref name="font"/>, with the
    /// copy at <paramref name="italicFace"/> patched to macStyle=Italic. Two separate copies (rather than
    /// the shared directory <c>SfntFontFaceSelectionTests</c> uses) are the point: the faces must differ
    /// in style, which is exactly what the resolver has to discriminate on.</summary>
    private static byte[] TwoFaceTtc(byte[] font, int italicFace)
    {
        const int headerSize = 20;
        var ttc = new byte[headerSize + font.Length * 2];
        ttc[0] = 0x74; ttc[1] = 0x74; ttc[2] = 0x63; ttc[3] = 0x66;   // 'ttcf'
        ttc[4] = 0x00; ttc[5] = 0x01;                                  // major
        ttc[6] = 0x00; ttc[7] = 0x00;                                  // minor
        ttc[8] = 0; ttc[9] = 0; ttc[10] = 0; ttc[11] = 0x02;           // numFonts = 2

        int[] bases = [headerSize, headerSize + font.Length];
        for (var f = 0; f < 2; f++)
        {
            WriteU32(ttc, 12 + f * 4, (uint)bases[f]);                 // offset[f]
            Array.Copy(font, 0, ttc, bases[f], font.Length);

            // Table-directory offsets in a .ttc are file-absolute, so each copy's must be rebased.
            int numTables = (ttc[bases[f] + 4] << 8) | ttc[bases[f] + 5];
            for (var i = 0; i < numTables; i++)
            {
                int rec = bases[f] + 12 + i * 16;
                uint off = ReadU32(ttc, rec + 8) + (uint)bases[f];
                WriteU32(ttc, rec + 8, off);
                if (ttc[rec] == 'h' && ttc[rec + 1] == 'e' && ttc[rec + 2] == 'a' && ttc[rec + 3] == 'd'
                    && f == italicFace)
                {
                    ttc[off + MacStyleOffset] = 0x00;
                    ttc[off + MacStyleOffset + 1] = 0x02;              // Italic
                }
            }
        }
        return ttc;
    }

    private static uint ReadU32(byte[] b, int i)
        => ((uint)b[i] << 24) | ((uint)b[i + 1] << 16) | ((uint)b[i + 2] << 8) | b[i + 3];

    private static void WriteU32(byte[] b, int i, uint v)
    {
        b[i] = (byte)(v >> 24); b[i + 1] = (byte)(v >> 16);
        b[i + 2] = (byte)(v >> 8); b[i + 3] = (byte)v;
    }

    /// <summary>Returns the same collection for every request, so the test isolates FACE selection from
    /// candidate-name selection — the resolver has exactly one file to work with and must still land on
    /// the correct face inside it.</summary>
    private sealed class OneCollectionProvider(byte[] ttc) : ISystemFontProvider
    {
        public byte[]? GetFontData(string baseFontName) => ttc;
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => ["Times"];
        public bool IsFontAvailable(string familyName) => true;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => candidates.FirstOrDefault();
        public void RefreshCache() { }
    }

    [Fact]
    public void Italic_request_selects_the_italic_face_of_a_collection()
    {
        var resolver = new SubstituteFontResolver(new OneCollectionProvider(TwoFaceTtc(BareFont(), 1)));

        EmbeddedFontMetrics? metrics = resolver.Resolve("NewCenturySchlbk-Italic", null);

        Assert.NotNull(metrics);
        Assert.True(metrics!.IsItalic,
            "italic was requested and the collection contains an italic face, but the resolver returned "
            + "a face whose head.macStyle has no Italic bit — it took face 0.");
    }

    [Fact]
    public void Upright_request_does_not_select_the_italic_face()
    {
        var resolver = new SubstituteFontResolver(new OneCollectionProvider(TwoFaceTtc(BareFont(), 1)));

        EmbeddedFontMetrics? metrics = resolver.Resolve("NewCenturySchlbk-Roman", null);

        Assert.NotNull(metrics);
        Assert.False(metrics!.IsItalic,
            "no italic was requested, so the upright face must be chosen even though the collection "
            + "also contains an italic one.");
    }
}
