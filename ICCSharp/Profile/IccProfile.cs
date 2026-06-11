using System;
using System.Collections.Generic;
using ICCSharp.IO;
using ICCSharp.Tags;

namespace ICCSharp.Profile;

/// <summary>
/// A fully parsed ICC profile: header + tag table + every tag element decoded. Aliased tags
/// (multiple signatures sharing the same offset, e.g. rTRC/gTRC/bTRC) share a single parsed
/// <see cref="TagElement"/> instance.
/// </summary>
public sealed class IccProfile
{
    public ProfileHeader Header { get; }
    public TagTable TagTable { get; }

    private readonly Dictionary<uint, TagElement> _bySignature;

    /// <summary>Raw profile bytes; preserved so consumers can re-hash or inspect unknown tags.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    private IccProfile(
        ProfileHeader header, TagTable tagTable,
        Dictionary<uint, TagElement> bySignature, ReadOnlyMemory<byte> bytes)
    {
        Header = header;
        TagTable = tagTable;
        _bySignature = bySignature;
        Bytes = bytes;
    }

    /// <summary>Parses an entire profile from <paramref name="data"/>.</summary>
    public static IccProfile Parse(ReadOnlyMemory<byte> data)
    {
        ProfileHeader header = ProfileHeader.Parse(data);
        if (header.ProfileSize > data.Length)
            throw new IccParseException(
                $"Header declares profile size {header.ProfileSize} but only {data.Length} bytes supplied.");

        IccBinaryReader reader = new(data);
        reader.Position = ProfileHeader.Size;
        TagTable table = TagTable.Parse(reader, header.ProfileSize);

        // Cache by (offset,size) so aliased tags share a parsed instance.
        Dictionary<long, TagElement> byOffset = new();
        Dictionary<uint, TagElement> bySignature = new();

        foreach (TagDirectoryEntry entry in table.Entries)
        {
            long key = ((long)entry.Offset << 32) | entry.Size;
            if (!byOffset.TryGetValue(key, out TagElement? el))
            {
                ReadOnlyMemory<byte> slice = data.Slice((int)entry.Offset, (int)entry.Size);
                el = TagElementReader.Parse(slice);
                byOffset[key] = el;
            }
            // First signature wins (spec disallows duplicates anyway).
            if (!bySignature.ContainsKey(entry.Signature.Value))
                bySignature.Add(entry.Signature.Value, el);
        }

        return new IccProfile(header, table, bySignature, data);
    }

    /// <summary>Returns the parsed tag, or null if absent.</summary>
    public TagElement? GetTag(IccSignature signature)
        => _bySignature.TryGetValue(signature.Value, out TagElement? el) ? el : null;

    /// <summary>Returns the parsed tag cast to <typeparamref name="T"/>, or null if absent or the wrong type.</summary>
    public T? GetTag<T>(IccSignature signature) where T : TagElement
        => GetTag(signature) as T;

    public bool Has(IccSignature signature) => _bySignature.ContainsKey(signature.Value);

    // ---- Convenience accessors -----------------------------------------

    /// <summary>'wtpt' — media white point as an <see cref="XyzNumber"/>, or null if absent.</summary>
    public XyzNumber? WhitePoint => FirstXyz(IccTagSignatures.MediaWhitePoint);

    /// <summary>'bkpt' — media black point as an <see cref="XyzNumber"/>, or null if absent.</summary>
    public XyzNumber? BlackPoint => FirstXyz(IccTagSignatures.MediaBlackPoint);

    public XyzNumber? RedColorant   => FirstXyz(IccTagSignatures.RedColorant);
    public XyzNumber? GreenColorant => FirstXyz(IccTagSignatures.GreenColorant);
    public XyzNumber? BlueColorant  => FirstXyz(IccTagSignatures.BlueColorant);
    public XyzNumber? Luminance     => FirstXyz(IccTagSignatures.Luminance);

    /// <summary>'rTRC' — red tone reproduction curve (curv or para).</summary>
    public TagElement? RedTrc       => GetTag(IccTagSignatures.RedTrc);
    public TagElement? GreenTrc     => GetTag(IccTagSignatures.GreenTrc);
    public TagElement? BlueTrc      => GetTag(IccTagSignatures.BlueTrc);
    public TagElement? GrayTrc      => GetTag(IccTagSignatures.GrayTrc);

    /// <summary>'A2B0' — perceptual A-to-B LUT.</summary>
    public TagElement? AToB0 => GetTag(IccTagSignatures.AToB0);
    public TagElement? AToB1 => GetTag(IccTagSignatures.AToB1);
    public TagElement? AToB2 => GetTag(IccTagSignatures.AToB2);
    public TagElement? BToA0 => GetTag(IccTagSignatures.BToA0);
    public TagElement? BToA1 => GetTag(IccTagSignatures.BToA1);
    public TagElement? BToA2 => GetTag(IccTagSignatures.BToA2);
    public TagElement? Gamut => GetTag(IccTagSignatures.Gamut);

    /// <summary>
    /// 'desc' — profile description. Handles both v2 ('desc') and v4 ('mluc') encodings, returning
    /// the first available text. Null if the tag is absent.
    /// </summary>
    public string? Description => GetDescriptiveText(IccTagSignatures.ProfileDescription);

    /// <summary>'cprt' — copyright text. Same dual-encoding handling as <see cref="Description"/>.</summary>
    public string? Copyright => GetDescriptiveText(IccTagSignatures.Copyright);

    public ChromaticityTagElement?      Chromaticity      => GetTag<ChromaticityTagElement>(IccTagSignatures.Chromaticity);
    public CicpTagElement?              Cicp              => GetTag<CicpTagElement>(IccTagSignatures.Cicp);
    public MeasurementTagElement?       Measurement       => GetTag<MeasurementTagElement>(IccTagSignatures.Measurement);
    public ViewingConditionsTagElement? ViewingConditions => GetTag<ViewingConditionsTagElement>(IccTagSignatures.ViewingConditions);
    public ColorantTableTagElement?     ColorantTable     => GetTag<ColorantTableTagElement>(IccTagSignatures.ColorantTable);
    public ColorantOrderTagElement?     ColorantOrder     => GetTag<ColorantOrderTagElement>(IccTagSignatures.ColorantOrder);
    public NamedColor2TagElement?       NamedColors       => GetTag<NamedColor2TagElement>(IccTagSignatures.NamedColor2);

    /// <summary>
    /// True iff the profile carries the v2 matrix/TRC tag family (rXYZ/gXYZ/bXYZ + rTRC/gTRC/bTRC).
    /// </summary>
    public bool HasMatrixTrc =>
        Has(IccTagSignatures.RedColorant) && Has(IccTagSignatures.GreenColorant) && Has(IccTagSignatures.BlueColorant) &&
        Has(IccTagSignatures.RedTrc)      && Has(IccTagSignatures.GreenTrc)      && Has(IccTagSignatures.BlueTrc);

    /// <summary>
    /// True iff the profile carries at least one A-to-B LUT tag (any rendering intent).
    /// </summary>
    public bool HasAToB => Has(IccTagSignatures.AToB0) || Has(IccTagSignatures.AToB1) || Has(IccTagSignatures.AToB2);

    /// <summary>
    /// True iff the profile is a monochrome GRAY profile carrying a kTRC curve — the 1-channel
    /// analog of the matrix/TRC family. Such profiles have neither an A2B LUT nor RGB colorants.
    /// </summary>
    public bool HasGrayTrc =>
        Header.DataColorSpace == ColorSpaceSignatures.Gray && Has(IccTagSignatures.GrayTrc);

    // ---- Helpers --------------------------------------------------------

    private XyzNumber? FirstXyz(IccSignature sig)
    {
        XyzTagElement? t = GetTag<XyzTagElement>(sig);
        return t is null || t.Values.Count == 0 ? null : t.Values[0];
    }

    private string? GetDescriptiveText(IccSignature sig)
    {
        TagElement? el = GetTag(sig);
        return el switch
        {
            MultiLocalizedUnicodeTagElement mluc => mluc.FirstText,
            TextDescriptionTagElement desc       => desc.AsciiDescription,
            TextTagElement text                  => text.Value,
            _ => null,
        };
    }
}
