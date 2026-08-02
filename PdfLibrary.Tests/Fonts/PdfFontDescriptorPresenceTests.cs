using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>P-4: the embedded-font presence probe must answer from the raw stream object — never
/// by decoding the font program (the decode cost ran once per ShowText operator and was ~30% of
/// all busy CPU on the ISO 32000-2 cold open). The corrupt-stream case doubles as the proof no
/// decode happens: decoding that fixture throws, so a true answer without a throw means the bytes
/// were never touched.</summary>
public class PdfFontDescriptorPresenceTests
{
    private static PdfFontDescriptor Descriptor(params (string key, PdfObject value)[] entries)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("FontDescriptor"),
            [new PdfName("FontName")] = new PdfName("Test"),
        };
        foreach ((string key, PdfObject value) in entries)
            dict[new PdfName(key)] = value;
        return new PdfFontDescriptor(dict);
    }

    private static PdfStream Stream(byte[] data) => new(new PdfDictionary(), data);

    [Theory]
    [InlineData("FontFile")]
    [InlineData("FontFile2")]
    [InlineData("FontFile3")]
    public void PresentStream_UnderAnyKey_IsTrue(string key)
    {
        Assert.True(Descriptor((key, Stream("font-bytes"u8.ToArray()))).HasEmbeddedFontProgram);
    }

    [Fact]
    public void NoFontFileKeys_IsFalse()
    {
        Assert.False(Descriptor().HasEmbeddedFontProgram);
    }

    [Fact]
    public void NonStreamValue_IsFalse()
    {
        Assert.False(Descriptor(("FontFile2", new PdfName("NotAStream"))).HasEmbeddedFontProgram);
    }

    [Fact]
    public void CorruptStream_IsTrue_SameAnswerTheDecodingProbeGave()
    {
        // Garbage under a /FlateDecode filter. Execution finding (2026-08-02): FlateDecodeFilter
        // NEVER throws — on total decompression failure it logs and returns the raw bytes as-is
        // (FlateDecodeFilter.cs "Returning raw data (uncompressed fallback)") — so the OLD
        // decoding probe also answered true here, after paying three failed inflate attempts.
        // The presence probe gives the IDENTICAL answer for free: the P-4 change is pure
        // work-elimination with no observable semantic delta at all. (No black-box no-decode
        // assertion is possible without instrumentation; the acceptance re-profile is the
        // authoritative proof the decode work is gone.)
        var dict = new PdfDictionary { [PdfName.Filter] = new PdfName("FlateDecode") };
        var corrupt = new PdfStream(dict, Encoding.ASCII.GetBytes("this is not zlib data"));
        PdfFontDescriptor d = Descriptor(("FontFile2", corrupt));

        Assert.True(d.HasEmbeddedFontProgram);
        // Pin the filter's degrade-don't-throw behavior this equivalence rests on: the decoding
        // accessor returns the raw bytes rather than throwing.
        Assert.Equal(Encoding.ASCII.GetBytes("this is not zlib data"), d.GetFontFile2());
    }
}
