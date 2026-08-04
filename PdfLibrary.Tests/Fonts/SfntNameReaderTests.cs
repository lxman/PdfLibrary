using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class SfntNameReaderTests
{
    [Fact]
    public void Reads_postscript_name_family_and_style()
    {
        byte[] data = SfntFixtures.Sfnt(0x0002,
            (3, 0x409, 1, "Test Family"),
            (3, 0x409, 2, "Italic"),
            (3, 0x409, 6, "TestFamily-Italic"));

        FontFaceRecord? face = SfntNameReader.ReadFace(data, 0, "test.ttf");

        Assert.NotNull(face);
        Assert.Equal("TestFamily-Italic", face!.PostScriptName);
        Assert.Equal("Test Family", face.EnglishFamily);
        Assert.True(face.Italic);
        Assert.False(face.Bold);
    }

    [Fact]
    public void Indexes_every_localized_family_not_just_english()
    {
        byte[] data = SfntFixtures.Sfnt(0,
            (3, 0x409, 1, "Hiragino Mincho ProN"),
            (3, 0x411, 1, "ヒラギノ明朝 ProN"),
            (3, 0x409, 6, "HiraMinProN-W3"));

        FontFaceRecord? face = SfntNameReader.ReadFace(data, 0, "test.ttf");

        Assert.NotNull(face);
        Assert.Contains("ヒラギノ明朝 ProN", face!.Families);
        Assert.Contains("Hiragino Mincho ProN", face.Families);
        Assert.Equal("Hiragino Mincho ProN", face.EnglishFamily);
    }

    [Fact]
    public void English_family_wins_regardless_of_record_order()
    {
        // The Spanish record comes FIRST. Taking "the first ID 1" would canonicalise to it and make
        // the index locale-dependent across machines — observed on a real box as "Times New Roman
        // cursiva".
        byte[] data = SfntFixtures.Sfnt(0,
            (3, 0x0C0A, 1, "Times New Roman cursiva"),
            (3, 0x409, 1, "Times New Roman"),
            (3, 0x409, 6, "TimesNewRomanPSMT"));

        FontFaceRecord? face = SfntNameReader.ReadFace(data, 0, "test.ttf");

        Assert.Equal("Times New Roman", face!.EnglishFamily);
    }

    [Fact]
    public void FaceCount_is_one_for_a_bare_sfnt()
    {
        Assert.Equal(1, SfntNameReader.FaceCount(SfntFixtures.Sfnt(0, (3, 0x409, 6, "X"))));
    }

    [Fact]
    public void Malformed_data_returns_null_rather_than_throwing()
    {
        Assert.Null(SfntNameReader.ReadFace([0x00, 0x01], 0, "truncated.ttf"));
        Assert.Null(SfntNameReader.ReadFace([], 0, "empty.ttf"));
    }

    [Fact]
    public void Ttc_FaceCount_matches_the_number_of_wrapped_faces()
    {
        byte[] face0 = SfntFixtures.Sfnt(0, (3, 0x409, 6, "FaceA-Regular"));
        byte[] face1 = SfntFixtures.Sfnt(0x0002, (3, 0x409, 6, "FaceB-Italic"));
        byte[] data = SfntFixtures.Ttc(face0, face1);

        Assert.Equal(2, SfntNameReader.FaceCount(data));
    }

    [Fact]
    public void Ttc_Each_face_reads_back_its_own_identity_not_a_neighbours()
    {
        byte[] face0 = SfntFixtures.Sfnt(0,
            (3, 0x409, 1, "Face Regular"),
            (3, 0x409, 6, "FaceA-Regular"));
        byte[] face1 = SfntFixtures.Sfnt(0x0002,
            (3, 0x409, 1, "Face Italic"),
            (3, 0x409, 2, "Italic"),
            (3, 0x409, 6, "FaceB-Italic"));
        byte[] data = SfntFixtures.Ttc(face0, face1);

        FontFaceRecord? read0 = SfntNameReader.ReadFace(data, 0, "test.ttc");
        FontFaceRecord? read1 = SfntNameReader.ReadFace(data, 1, "test.ttc");

        Assert.NotNull(read0);
        Assert.Equal("FaceA-Regular", read0!.PostScriptName);
        Assert.Equal("Face Regular", read0.EnglishFamily);
        Assert.False(read0.Italic);

        Assert.NotNull(read1);
        Assert.Equal("FaceB-Italic", read1!.PostScriptName);
        Assert.Equal("Face Italic", read1.EnglishFamily);
        Assert.True(read1.Italic);
    }

    [Fact]
    public void Ttc_Face_index_beyond_FaceCount_returns_null_rather_than_throwing()
    {
        byte[] face0 = SfntFixtures.Sfnt(0, (3, 0x409, 6, "FaceA-Regular"));
        byte[] face1 = SfntFixtures.Sfnt(0, (3, 0x409, 6, "FaceB-Regular"));
        byte[] data = SfntFixtures.Ttc(face0, face1);

        Assert.Null(SfntNameReader.ReadFace(data, 2, "test.ttc"));
    }

    /// <summary>Since the collapse the byte[] overload IS a MemoryStream wrapper over the Stream one,
    /// so this can no longer fail for a decode reason — it guards the wrapper: that it still forwards
    /// every argument and hands back what the Stream overload produced. Kept, not deleted, because a
    /// future re-divergence of the two entry points is exactly what it would catch.</summary>
    [Fact]
    public void Byte_array_overload_delegates_to_the_stream_overload_for_a_bare_sfnt()
    {
        byte[] data = SfntFixtures.Sfnt(0x0002,
            (3, 0x409, 1, "Test Family"),
            (3, 0x409, 2, "Italic"),
            (3, 0x409, 6, "TestFamily-Italic"));

        using var stream = new MemoryStream(data);

        Assert.Equal(SfntNameReader.FaceCount(data), SfntNameReader.FaceCount(stream));

        FontFaceRecord? fromBytes = SfntNameReader.ReadFace(data, 0, "test.ttf");
        FontFaceRecord? fromStream = SfntNameReader.ReadFace(stream, 0, "test.ttf");

        Assert.NotNull(fromBytes);
        Assert.NotNull(fromStream);
        Assert.Equal(fromBytes!.PostScriptName, fromStream!.PostScriptName);
        Assert.Equal(fromBytes.EnglishFamily, fromStream.EnglishFamily);
        Assert.Equal(fromBytes.Families, fromStream.Families);
        Assert.Equal(fromBytes.Italic, fromStream.Italic);
        Assert.Equal(fromBytes.Bold, fromStream.Bold);
    }

    /// <summary>Wrapper guard, as above — plus the face-index argument, the one the wrapper actually
    /// has to forward rather than merely pass through.</summary>
    [Fact]
    public void Byte_array_overload_delegates_to_the_stream_overload_for_each_face_of_a_ttc()
    {
        byte[] face0 = SfntFixtures.Sfnt(0,
            (3, 0x409, 1, "Face Regular"),
            (3, 0x409, 6, "FaceA-Regular"));
        byte[] face1 = SfntFixtures.Sfnt(0x0002,
            (3, 0x409, 1, "Face Italic"),
            (3, 0x409, 2, "Italic"),
            (3, 0x409, 6, "FaceB-Italic"));
        byte[] data = SfntFixtures.Ttc(face0, face1);

        using var stream = new MemoryStream(data);

        Assert.Equal(SfntNameReader.FaceCount(data), SfntNameReader.FaceCount(stream));

        for (var i = 0; i < 2; i++)
        {
            FontFaceRecord? fromBytes = SfntNameReader.ReadFace(data, i, "test.ttc");
            FontFaceRecord? fromStream = SfntNameReader.ReadFace(stream, i, "test.ttc");

            Assert.NotNull(fromBytes);
            Assert.NotNull(fromStream);
            Assert.Equal(fromBytes!.PostScriptName, fromStream!.PostScriptName);
            Assert.Equal(fromBytes.EnglishFamily, fromStream.EnglishFamily);
            Assert.Equal(fromBytes.Families, fromStream.Families);
            Assert.Equal(fromBytes.Italic, fromStream.Italic);
            Assert.Equal(fromBytes.Bold, fromStream.Bold);
        }
    }

    /// <summary>Platform 0 (Unicode) records are UTF-16BE just like platform 3. A font whose ONLY
    /// name records are platform 0 is spec-legal and is emitted by some OTF/CJK toolchains; decoding
    /// those bytes as ASCII yields "T\0e\0s\0t\0..." which Trim('\0') cannot clean up because the
    /// NULs are interior, and the face lands in the index under garbage keys.</summary>
    [Fact]
    public void Platform0_only_font_is_indexed_with_clean_strings()
    {
        byte[] data = SfntFixtures.Sfnt(0x0002,
            (0, 0, 1, "Unicode Family"),
            (0, 0, 2, "Italic"),
            (0, 0, 6, "UnicodeFamily-Italic"));

        FontFaceRecord? face = SfntNameReader.ReadFace(data, 0, "unicode-only.otf");

        Assert.NotNull(face);
        Assert.Equal("UnicodeFamily-Italic", face!.PostScriptName);
        Assert.Equal("Unicode Family", face.EnglishFamily);
        Assert.Contains("Unicode Family", face.Families);
        Assert.True(face.Italic);
    }

    /// <summary>Guards the interior NUL directly: "looks right at the ends" is exactly what the ASCII
    /// decode produced, since Trim('\0') strips only the trailing NUL of the last code unit.</summary>
    [Fact]
    public void Platform0_strings_contain_no_nul_anywhere()
    {
        byte[] data = SfntFixtures.Sfnt(0,
            (0, 0, 1, "Unicode Family"),
            (0, 0, 2, "Regular"),
            (0, 0, 6, "UnicodeFamily-Regular"));

        FontFaceRecord? face = SfntNameReader.ReadFace(data, 0, "unicode-only.otf");

        Assert.NotNull(face);
        Assert.DoesNotContain('\0', face!.PostScriptName);
        Assert.DoesNotContain('\0', face.EnglishFamily);
        foreach (string f in face.Families) Assert.DoesNotContain('\0', f);
    }

    /// <summary>Wrapper guard, as above. The platform-0 assertions are not redundant with it: they
    /// pin the decode itself, which one shared implementation now has to get right for both.</summary>
    [Fact]
    public void Byte_array_overload_delegates_to_the_stream_overload_for_a_platform0_only_font()
    {
        byte[] data = SfntFixtures.Sfnt(0x0002,
            (0, 0, 1, "Unicode Family"),
            (0, 0, 2, "Italic"),
            (0, 0, 6, "UnicodeFamily-Italic"));

        using var stream = new MemoryStream(data);

        FontFaceRecord? fromBytes = SfntNameReader.ReadFace(data, 0, "unicode-only.otf");
        FontFaceRecord? fromStream = SfntNameReader.ReadFace(stream, 0, "unicode-only.otf");

        Assert.NotNull(fromBytes);
        Assert.NotNull(fromStream);
        Assert.Equal("UnicodeFamily-Italic", fromStream!.PostScriptName);
        Assert.Equal(fromBytes!.PostScriptName, fromStream.PostScriptName);
        Assert.Equal(fromBytes.EnglishFamily, fromStream.EnglishFamily);
        Assert.Equal(fromBytes.Families, fromStream.Families);
        Assert.Equal(fromBytes.Italic, fromStream.Italic);
        Assert.Equal(fromBytes.Bold, fromStream.Bold);
        Assert.DoesNotContain('\0', fromStream.PostScriptName);
        Assert.DoesNotContain('\0', fromStream.EnglishFamily);
    }

    /// <summary>Pins the null contract that survived the byte[]-onto-Stream collapse: the old
    /// ReadFace(byte[]) dereferenced data.Length inside its OWN try/catch, so a null array was caught
    /// and turned into null. The wrapper now has to guard explicitly, since the try/catch it delegates
    /// to lives in the Stream overload and never sees the null — it's the MemoryStream constructor
    /// that would throw first if the guard were missing.</summary>
    [Fact]
    public void ReadFace_byte_array_returns_null_for_a_null_array_rather_than_throwing()
    {
        Assert.Null(SfntNameReader.ReadFace((byte[])null!, 0, "null.ttf"));
    }

    /// <summary>FaceCount(byte[]) never had a try/catch, so a null array threw before the collapse
    /// (NullReferenceException, from the `data.Length` on the first line of the old FaceCount(byte[])
    /// — IsTtc was never reached) and still throws after it
    /// (ArgumentNullException, from the MemoryStream constructor). This is a deliberate non-goal: the
    /// contract was already "throws on null", only the exception TYPE changed, and no caller today
    /// passes null (FontMetadataIndex.PickFaceIndex always has non-null bytes).</summary>
    [Fact]
    public void FaceCount_byte_array_throws_ArgumentNullException_for_a_null_array()
    {
        Assert.Throws<ArgumentNullException>(() => SfntNameReader.FaceCount((byte[])null!));
    }

    [Fact]
    public void Stream_overload_returns_null_for_malformed_data_rather_than_throwing()
    {
        using var truncated = new MemoryStream([0x00, 0x01]);
        using var empty = new MemoryStream([]);

        Assert.Null(SfntNameReader.ReadFace(truncated, 0, "truncated.ttf"));
        Assert.Null(SfntNameReader.ReadFace(empty, 0, "empty.ttf"));
        Assert.Equal(0, SfntNameReader.FaceCount(truncated));
        Assert.Equal(0, SfntNameReader.FaceCount(empty));
    }
}

