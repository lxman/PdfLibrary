using System;
using System.IO;

namespace Jp2Codec.Jp2File
{
    /// <summary>
    /// Sniffs JP2-vs-raw-J2K and walks the JP2 box hierarchy to extract
    /// the embedded codestream range plus colorspace metadata.
    ///
    /// Only the subset of boxes required for decode is parsed:
    /// <list type="bullet">
    ///   <item>jP signature (presence check)</item>
    ///   <item>ftyp (compatibility check — must list 'jp2 ')</item>
    ///   <item>jp2h/ihdr (image dimensions + bit depth)</item>
    ///   <item>jp2h/bpcc (per-component bit depths when ihdr.BPC = 0xFF)</item>
    ///   <item>jp2h/colr (colorspace; the first colr wins per I.5.3.3)</item>
    ///   <item>jp2h/pclr (palette table for indexed images)</item>
    ///   <item>jp2h/cmap (output-channel mapping; pairs with pclr)</item>
    ///   <item>jp2c (codestream byte range)</item>
    /// </list>
    /// Other boxes (channel definition, IPR, XML, UUID) are skipped.
    /// </summary>
    internal static class Jp2FileParser
    {
        private static readonly byte[] JpSignatureMagic = { 0x0D, 0x0A, 0x87, 0x0A };

        public static Jp2FileInfo Parse(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            // ---- Sniff ----
            if (LooksLikeJ2kCodestream(data))
            {
                return new Jp2FileInfo(
                    isJp2File: false,
                    height: 0, width: 0, numberOfComponents: 0,
                    bitsPerComponent: Array.Empty<int>(),
                    componentSigned: Array.Empty<bool>(),
                    colorSpace: Jp2ColorSpace.Unspecified,
                    codestreamOffset: 0,
                    codestreamLength: data.Length);
            }

            if (!LooksLikeJp2File(data))
            {
                throw new InvalidDataException(
                    "Input is neither a JP2 file (jP signature box) nor a raw J2K codestream (SOC marker).");
            }

            // ---- Walk top-level boxes ----
            var reader = new BoxReader(data);

            // First box must be the jP signature.
            if (!reader.ReadNext(out BoxHeader jp) || jp.Type != BoxType.JpSignature)
                throw new InvalidDataException("Missing jP signature box at start of JP2 file.");
            ValidateSignatureBox(data, jp);

            // Second box must be ftyp.
            if (!reader.ReadNext(out BoxHeader ftyp) || ftyp.Type != BoxType.FileType)
                throw new InvalidDataException("Missing ftyp box after jP signature.");
            ValidateFtypBox(data, ftyp);

            int width = 0, height = 0, components = 0;
            int[] bpc = Array.Empty<int>();
            bool[] signedFlags = Array.Empty<bool>();
            var cs = Jp2ColorSpace.Unspecified;
            byte[]? iccProfile = null;
            int codestreamOffset = -1, codestreamLength = 0;
            JpPalette? palette = null;
            JpComponentMapping? componentMapping = null;
            JpChannelDefinition? channelDefinition = null;

            while (reader.ReadNext(out BoxHeader box))
            {
                if (box.Type == BoxType.Jp2Header)
                {
                    ParseJp2HeaderSuperbox(reader.OpenChild(box),
                        ref height, ref width, ref components,
                        ref bpc, ref signedFlags, ref cs, ref iccProfile,
                        ref palette, ref componentMapping, ref channelDefinition);
                }
                else if (box.Type == BoxType.ContiguousCodestream)
                {
                    if (codestreamOffset >= 0) continue; // first codestream wins
                    if (box.ContentLength < 0 || box.ContentLength > int.MaxValue)
                        throw new InvalidDataException("jp2c content length out of range.");
                    codestreamOffset = box.ContentStart;
                    codestreamLength = (int)box.ContentLength;
                }
                // All other top-level boxes are skipped (already advanced past by ReadNext).
            }

            if (codestreamOffset < 0)
                throw new InvalidDataException("JP2 file contains no jp2c (codestream) box.");
            if (components == 0)
                throw new InvalidDataException("JP2 file contains no ihdr (image header) box.");

            return new Jp2FileInfo(
                isJp2File: true,
                height: height, width: width, numberOfComponents: components,
                bitsPerComponent: bpc,
                componentSigned: signedFlags,
                colorSpace: cs,
                codestreamOffset: codestreamOffset,
                codestreamLength: codestreamLength,
                palette: palette,
                componentMapping: componentMapping,
                iccProfile: iccProfile,
                channelDefinition: channelDefinition);
        }

        private static bool LooksLikeJ2kCodestream(byte[] data)
        {
            return data.Length >= 2 && data[0] == 0xFF && data[1] == 0x4F;
        }

        private static bool LooksLikeJp2File(byte[] data)
        {
            // jP signature box is exactly LBox=12 + TBox='jP  ' + 4 magic bytes.
            return data.Length >= 12
                && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x0C
                && data[4] == (byte)'j' && data[5] == (byte)'P'
                && data[6] == 0x20 && data[7] == 0x20;
        }

        private static void ValidateSignatureBox(byte[] data, BoxHeader jp)
        {
            if (jp.ContentLength != JpSignatureMagic.Length)
                throw new InvalidDataException(
                    $"jP signature box has content length {jp.ContentLength}, expected {JpSignatureMagic.Length}.");
            for (var i = 0; i < JpSignatureMagic.Length; i++)
            {
                if (data[jp.ContentStart + i] != JpSignatureMagic[i])
                    throw new InvalidDataException("jP signature box magic bytes mismatch.");
            }
        }

        private static void ValidateFtypBox(byte[] data, BoxHeader ftyp)
        {
            if (ftyp.ContentLength < 8 || ftyp.ContentLength > int.MaxValue)
                throw new InvalidDataException($"ftyp box content length {ftyp.ContentLength} out of range.");
            // Major brand (4) + minor version (4) + 1+ compatibility brands (4 each).
            // We don't strictly require 'jp2 ' to be the major brand (some files
            // use jpx/mjp2 majors) but it MUST appear somewhere in the brand list.
            if (((ftyp.ContentLength - 8) % 4) != 0)
                throw new InvalidDataException($"ftyp compatibility list length {ftyp.ContentLength - 8} not a multiple of 4.");

            int n = (int)(ftyp.ContentLength - 8) / 4;
            var seenJp2 = false;
            for (var i = 0; i < n; i++)
            {
                int at = ftyp.ContentStart + 8 + i * 4;
                uint brand = ((uint)data[at] << 24) | ((uint)data[at + 1] << 16)
                             | ((uint)data[at + 2] << 8) | data[at + 3];
                if (brand == BoxType.Jp2Brand) { seenJp2 = true; break; }
            }
            if (!seenJp2)
                throw new InvalidDataException("ftyp box does not list 'jp2 ' in its compatibility brands.");
        }

        private static void ParseJp2HeaderSuperbox(
            BoxReader child,
            ref int height, ref int width, ref int components,
            ref int[] bpc, ref bool[] signedFlags,
            ref Jp2ColorSpace cs,
            ref byte[]? iccProfile,
            ref JpPalette? palette,
            ref JpComponentMapping? componentMapping,
            ref JpChannelDefinition? channelDefinition)
        {
            var sawIhdr = false;
            int ihdrBpc = -1;
            var sawColr = false;

            while (child.ReadNext(out BoxHeader box))
            {
                switch (box.Type)
                {
                    case BoxType.ImageHeader:
                        if (sawIhdr) throw new InvalidDataException("Duplicate ihdr box.");
                        if (box.ContentLength != 14)
                            throw new InvalidDataException($"ihdr content length {box.ContentLength} != 14.");
                        height = (int)ReadUInt32BE(child.Buffer, box.ContentStart);
                        width = (int)ReadUInt32BE(child.Buffer, box.ContentStart + 4);
                        components = ReadUInt16BE(child.Buffer, box.ContentStart + 8);
                        ihdrBpc = child.Buffer[box.ContentStart + 10];
                        // child.Buffer[box.ContentStart + 11] = compression type (0x07 = wavelet)
                        // child.Buffer[box.ContentStart + 12] = UnkC
                        // child.Buffer[box.ContentStart + 13] = IPR
                        if (ihdrBpc != 0xFF)
                        {
                            // Uniform bit depth for all components.
                            int depth = (ihdrBpc & 0x7F) + 1;
                            bool isSigned = (ihdrBpc & 0x80) != 0;
                            bpc = new int[1] { depth };
                            signedFlags = new bool[1] { isSigned };
                        }
                        sawIhdr = true;
                        break;

                    case BoxType.BitsPerComponent:
                        if (!sawIhdr)
                            throw new InvalidDataException("bpcc box appeared before ihdr.");
                        if (ihdrBpc != 0xFF)
                            throw new InvalidDataException("bpcc present but ihdr.BPC != 0xFF.");
                        if (box.ContentLength != components)
                            throw new InvalidDataException(
                                $"bpcc content length {box.ContentLength} != NC ({components}).");
                        bpc = new int[components];
                        signedFlags = new bool[components];
                        for (var i = 0; i < components; i++)
                        {
                            byte b = child.Buffer[box.ContentStart + i];
                            bpc[i] = (b & 0x7F) + 1;
                            signedFlags[i] = (b & 0x80) != 0;
                        }
                        break;

                    case BoxType.ColourSpecification:
                        if (sawColr) continue; // first colr wins per I.5.3.3
                        if (box.ContentLength < 3)
                            throw new InvalidDataException($"colr content length {box.ContentLength} < 3.");
                        byte meth = child.Buffer[box.ContentStart];
                        if (meth == 1)
                        {
                            if (box.ContentLength < 7)
                                throw new InvalidDataException("colr: METH=1 requires 4-byte EnumCS field.");
                            uint enumCs = ReadUInt32BE(child.Buffer, box.ContentStart + 3);
                            cs = enumCs switch
                            {
                                16 => Jp2ColorSpace.Srgb,
                                17 => Jp2ColorSpace.Greyscale,
                                18 => Jp2ColorSpace.SrgbYcc,
                                _ => Jp2ColorSpace.Unspecified,
                            };
                        }
                        else if (meth == 2 || meth == 3)
                        {
                            cs = meth == 2 ? Jp2ColorSpace.RestrictedIcc : Jp2ColorSpace.AnyIcc;
                            // ICC profile bytes follow the 3-byte (METH/PREC/APPROX)
                            // prefix (I.5.3.3, Figure I.10).
                            var profileLength = (int)(box.ContentLength - 3);
                            if (profileLength > 0)
                            {
                                iccProfile = new byte[profileLength];
                                Array.Copy(child.Buffer, box.ContentStart + 3,
                                    iccProfile, 0, profileLength);
                            }
                        }
                        else
                        {
                            cs = Jp2ColorSpace.Unspecified;
                        }
                        sawColr = true;
                        break;

                    case BoxType.Palette:
                        if (palette is not null)
                            throw new InvalidDataException("Duplicate pclr box in jp2h superbox.");
                        palette = ParsePaletteBox(child.Buffer, box);
                        break;

                    case BoxType.ComponentMapping:
                        if (componentMapping is not null)
                            throw new InvalidDataException("Duplicate cmap box in jp2h superbox.");
                        componentMapping = ParseComponentMappingBox(child.Buffer, box);
                        break;

                    case BoxType.ChannelDefinition:
                        if (channelDefinition is not null)
                            throw new InvalidDataException("Duplicate cdef box in jp2h superbox.");
                        channelDefinition = ParseChannelDefinitionBox(child.Buffer, box);
                        break;

                    default:
                        // Resolution boxes — skipped.
                        break;
                }
            }

            if (!sawIhdr)
                throw new InvalidDataException("jp2h superbox missing required ihdr child.");
            if (!sawColr)
                throw new InvalidDataException("jp2h superbox missing required colr child.");
            if (ihdrBpc == 0xFF && bpc.Length != components)
                throw new InvalidDataException("ihdr.BPC = 0xFF but no bpcc box was found.");
            if ((palette is null) ^ (componentMapping is null))
                throw new InvalidDataException(
                    "pclr and cmap boxes must appear together: a palette without a component mapping (or vice versa) is invalid per ISO/IEC 15444-1 I.5.3.4–5.");
        }

        private static JpPalette ParsePaletteBox(byte[] buffer, BoxHeader box)
        {
            // I.5.3.4: NE (uint16) + NPC (uint8) + NPC × (Bi: int8 sign|depth)
            // + NE × Σ ceil(Bi/8) bytes of palette entries.
            if (box.ContentLength < 3)
                throw new InvalidDataException($"pclr content length {box.ContentLength} < 3.");
            int cursor = box.ContentStart;
            int numEntries = (buffer[cursor] << 8) | buffer[cursor + 1];
            int numColumns = buffer[cursor + 2];
            cursor += 3;
            long expectedHeader = 3 + numColumns;
            if (box.ContentLength < expectedHeader)
                throw new InvalidDataException(
                    $"pclr content length {box.ContentLength} too short for NE={numEntries}, NPC={numColumns}.");

            var bitDepths = new int[numColumns];
            var signed = new bool[numColumns];
            var totalEntryBytes = 0;
            for (var j = 0; j < numColumns; j++)
            {
                byte b = buffer[cursor + j];
                bitDepths[j] = (b & 0x7F) + 1;
                signed[j] = (b & 0x80) != 0;
                int bytesPerSample = (bitDepths[j] + 7) / 8;
                if (bytesPerSample > 4)
                    throw new InvalidDataException(
                        $"pclr column {j}: bit depth {bitDepths[j]} produces >4-byte samples (not supported).");
                totalEntryBytes += bytesPerSample;
            }
            cursor += numColumns;

            long dataBytes = (long)numEntries * totalEntryBytes;
            if (box.ContentLength < expectedHeader + dataBytes)
                throw new InvalidDataException(
                    $"pclr content length {box.ContentLength} too short for entry table ({dataBytes} bytes needed).");

            var entries = new int[numEntries, numColumns];
            for (var i = 0; i < numEntries; i++)
            {
                for (var j = 0; j < numColumns; j++)
                {
                    int bd = bitDepths[j];
                    int bytes = (bd + 7) / 8;
                    var raw = 0;
                    for (var k = 0; k < bytes; k++)
                    {
                        raw = (raw << 8) | buffer[cursor];
                        cursor++;
                    }
                    int mask = bd >= 32 ? -1 : (1 << bd) - 1;
                    raw &= mask;
                    if (signed[j] && bd < 32 && (raw & (1 << (bd - 1))) != 0)
                    {
                        // Sign-extend.
                        raw |= ~mask;
                    }
                    entries[i, j] = raw;
                }
            }

            return new JpPalette(numEntries, numColumns, bitDepths, signed, entries);
        }

        private static JpComponentMapping ParseComponentMappingBox(byte[] buffer, BoxHeader box)
        {
            // I.5.3.5: a stream of 4-byte tuples (CMP: uint16, MTYP: uint8, PCOL: uint8).
            if (box.ContentLength % 4 != 0)
                throw new InvalidDataException($"cmap content length {box.ContentLength} not a multiple of 4.");
            var numChannels = (int)(box.ContentLength / 4);
            var cmp = new int[numChannels];
            var mtyp = new byte[numChannels];
            var pcol = new byte[numChannels];
            int cursor = box.ContentStart;
            for (var i = 0; i < numChannels; i++)
            {
                cmp[i] = (buffer[cursor] << 8) | buffer[cursor + 1];
                mtyp[i] = buffer[cursor + 2];
                pcol[i] = buffer[cursor + 3];
                if (mtyp[i] > 1)
                    throw new InvalidDataException(
                        $"cmap channel {i}: MTYP {mtyp[i]} unsupported (only 0=direct, 1=palette are defined).");
                cursor += 4;
            }
            return new JpComponentMapping(cmp, mtyp, pcol);
        }

        private static JpChannelDefinition ParseChannelDefinitionBox(byte[] buffer, BoxHeader box)
        {
            // I.5.3.6: uint16 N (entry count), then N × {Cn: uint16, Typ: uint16, Asoc: uint16}.
            if (box.ContentLength < 2)
                throw new InvalidDataException($"cdef content length {box.ContentLength} < 2.");
            int cursor = box.ContentStart;
            int n = ReadUInt16BE(buffer, cursor);
            cursor += 2;
            long expected = 2L + (long)n * 6;
            if (box.ContentLength != expected)
                throw new InvalidDataException(
                    $"cdef content length {box.ContentLength} != 2 + 6·N (N={n}).");

            var cn = new ushort[n];
            var typ = new ushort[n];
            var asoc = new ushort[n];
            for (var i = 0; i < n; i++)
            {
                cn[i]   = ReadUInt16BE(buffer, cursor);
                typ[i]  = ReadUInt16BE(buffer, cursor + 2);
                asoc[i] = ReadUInt16BE(buffer, cursor + 4);
                cursor += 6;
            }
            return new JpChannelDefinition(cn, typ, asoc);
        }

        private static uint ReadUInt32BE(byte[] buf, int at)
        {
            return ((uint)buf[at] << 24) | ((uint)buf[at + 1] << 16)
                 | ((uint)buf[at + 2] << 8) | buf[at + 3];
        }

        private static ushort ReadUInt16BE(byte[] buf, int at)
        {
            return (ushort)((buf[at] << 8) | buf[at + 1]);
        }
    }
}
