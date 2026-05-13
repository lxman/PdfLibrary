using System.Collections.Generic;
using System.IO;

namespace Jp2Codec.Tests.Codestream
{
    /// <summary>
    /// Test helper: builds well-formed J2K codestream bytes by hand so the
    /// parsers can be exercised against known-good inputs. Mirrors the spec
    /// field order so the field offsets match Annex A diagrams when debugging.
    /// </summary>
    internal sealed class HeaderBytes
    {
        private readonly List<byte> _buf = new();

        public byte[] ToArray() => _buf.ToArray();
        public int Length => _buf.Count;

        public HeaderBytes Marker(ushort code) { U16(code); return this; }
        public HeaderBytes U8(int value) { _buf.Add(checked((byte)value)); return this; }
        public HeaderBytes U16(int value)
        {
            _buf.Add((byte)((value >> 8) & 0xFF));
            _buf.Add((byte)(value & 0xFF));
            return this;
        }
        public HeaderBytes U32(uint value)
        {
            _buf.Add((byte)((value >> 24) & 0xFF));
            _buf.Add((byte)((value >> 16) & 0xFF));
            _buf.Add((byte)((value >> 8) & 0xFF));
            _buf.Add((byte)(value & 0xFF));
            return this;
        }
        public HeaderBytes Bytes(params byte[] bytes) { _buf.AddRange(bytes); return this; }

        /// <summary>Begin a marker segment; writes the marker + a placeholder Lxxx that <see cref="EndSegment"/> later patches.</summary>
        public int BeginSegment(ushort marker)
        {
            U16(marker);
            int lenAt = _buf.Count;
            U16(0); // placeholder
            return lenAt;
        }

        /// <summary>Patch the Lxxx placeholder set up by <see cref="BeginSegment"/>.</summary>
        public void EndSegment(int lenAt)
        {
            int segLen = _buf.Count - lenAt;
            if (segLen < 2 || segLen > 0xFFFF)
                throw new InvalidDataException($"Segment length {segLen} not representable as Lxxx.");
            _buf[lenAt] = (byte)((segLen >> 8) & 0xFF);
            _buf[lenAt + 1] = (byte)(segLen & 0xFF);
        }

        /// <summary>Write a minimal valid SIZ segment for an N-component image.</summary>
        public HeaderBytes Siz(
            int width, int height, int components,
            int bitDepth = 8, bool isSigned = false,
            byte xrSiz = 1, byte yrSiz = 1,
            int? tileWidth = null, int? tileHeight = null)
        {
            int xt = tileWidth ?? width;
            int yt = tileHeight ?? height;

            int at = BeginSegment(0xFF51);
            U16(0);              // Rsiz
            U32((uint)width);    // Xsiz
            U32((uint)height);   // Ysiz
            U32(0);              // XOsiz
            U32(0);              // YOsiz
            U32((uint)xt);       // XTsiz
            U32((uint)yt);       // YTsiz
            U32(0);              // XTOsiz
            U32(0);              // YTOsiz
            U16(components);     // Csiz
            byte ssiz = (byte)((isSigned ? 0x80 : 0) | ((bitDepth - 1) & 0x7F));
            for (var c = 0; c < components; c++)
            {
                U8(ssiz);
                U8(xrSiz);
                U8(yrSiz);
            }
            EndSegment(at);
            return this;
        }

        /// <summary>Write a minimal COD segment with default precincts.</summary>
        public HeaderBytes Cod(
            int decompositionLevels = 5,
            int xcbExp = 4, int ycbExp = 4,
            byte progressionOrder = 0,
            ushort layers = 1,
            bool mct = false,
            bool reversibleTransform = true,
            bool sop = false, bool eph = false)
        {
            int at = BeginSegment(0xFF52);
            byte scod = (byte)((sop ? 0x02 : 0) | (eph ? 0x04 : 0));
            U8(scod);
            U8(progressionOrder);
            U16(layers);
            U8(mct ? 1 : 0);
            U8(decompositionLevels);
            U8(xcbExp - 2);
            U8(ycbExp - 2);
            U8(0);          // code-block style
            U8(reversibleTransform ? 1 : 0);
            EndSegment(at);
            return this;
        }

        /// <summary>Write a minimal QCD segment for a reversible (5/3) image.</summary>
        public HeaderBytes QcdReversible(int decompositionLevels = 5, int exponent = 8, int guardBits = 2)
        {
            int subbands = 3 * decompositionLevels + 1;
            int at = BeginSegment(0xFF5C);
            byte sqcd = (byte)((guardBits & 0x07) << 5); // style = 0 (None)
            U8(sqcd);
            for (var i = 0; i < subbands; i++)
                U8((exponent & 0x1F) << 3); // exponent in top 5 bits, low 3 bits unused
            EndSegment(at);
            return this;
        }

        /// <summary>Write a comment segment with ASCII text payload tagged as Latin-9.</summary>
        public HeaderBytes ComText(string text)
        {
            int at = BeginSegment(0xFF64);
            U16(1); // Rcom = Latin-9
            foreach (char ch in text)
                U8((byte)ch);
            EndSegment(at);
            return this;
        }

        /// <summary>Write a SOT segment with the given fields.</summary>
        public HeaderBytes Sot(int tileIndex = 0, uint psot = 14, int tpsot = 0, int tnsot = 1)
        {
            int at = BeginSegment(0xFF90);
            U16(tileIndex);
            U32(psot);
            U8(tpsot);
            U8(tnsot);
            EndSegment(at);
            return this;
        }
    }
}
