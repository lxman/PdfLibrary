namespace Jp2Codec.Tests.Jp2File
{
    /// <summary>
    /// Test helper: builds JP2 box hierarchies by hand. Boxes are emitted with
    /// the 8-byte (LBox + TBox) header form; <see cref="BeginBox"/> reserves
    /// the length placeholder which <see cref="EndBox"/> patches.
    /// </summary>
    internal sealed class Jp2Bytes
    {
        private readonly List<byte> _buf = new();

        public byte[] ToArray() => _buf.ToArray();

        public Jp2Bytes U8(int v) { _buf.Add((byte)v); return this; }
        public Jp2Bytes U16(int v) { _buf.Add((byte)((v >> 8) & 0xFF)); _buf.Add((byte)(v & 0xFF)); return this; }
        public Jp2Bytes U32(uint v)
        {
            _buf.Add((byte)((v >> 24) & 0xFF));
            _buf.Add((byte)((v >> 16) & 0xFF));
            _buf.Add((byte)((v >> 8) & 0xFF));
            _buf.Add((byte)(v & 0xFF));
            return this;
        }
        public Jp2Bytes Bytes(params byte[] bs) { _buf.AddRange(bs); return this; }
        public Jp2Bytes Ascii(string s) { foreach (char c in s) _buf.Add((byte)c); return this; }

        public int BeginBox(uint type)
        {
            int lenAt = _buf.Count;
            U32(0);          // placeholder LBox
            U32(type);
            return lenAt;
        }

        public void EndBox(int lenAt)
        {
            int total = _buf.Count - lenAt;
            _buf[lenAt + 0] = (byte)((total >> 24) & 0xFF);
            _buf[lenAt + 1] = (byte)((total >> 16) & 0xFF);
            _buf[lenAt + 2] = (byte)((total >> 8) & 0xFF);
            _buf[lenAt + 3] = (byte)(total & 0xFF);
        }

        // ---- Common box shortcuts ----
        public Jp2Bytes SignatureBox()
        {
            int at = BeginBox(0x6A502020); // 'jP  '
            Bytes(0x0D, 0x0A, 0x87, 0x0A);
            EndBox(at);
            return this;
        }

        public Jp2Bytes FtypBoxWithJp2()
        {
            int at = BeginBox(0x66747970); // 'ftyp'
            U32(0x6A703220); // major brand 'jp2 '
            U32(0);          // minor version
            U32(0x6A703220); // compatibility brand 'jp2 '
            EndBox(at);
            return this;
        }

        public Jp2Bytes Jp2HeaderForSrgb(int width, int height, int components, int bitDepth = 8)
        {
            int superAt = BeginBox(0x6A703268); // 'jp2h'

            // ihdr
            int ihdrAt = BeginBox(0x69686472);
            U32((uint)height).U32((uint)width).U16(components);
            U8(bitDepth - 1);  // BPC (uniform)
            U8(0x07);          // C = wavelet
            U8(0);             // UnkC
            U8(0);             // IPR
            EndBox(ihdrAt);

            // colr (enumerated sRGB)
            int colrAt = BeginBox(0x636F6C72);
            U8(1);   // METH = enumerated
            U8(0);   // PREC
            U8(0);   // APPROX
            U32(16); // EnumCS = sRGB
            EndBox(colrAt);

            EndBox(superAt);
            return this;
        }

        public Jp2Bytes Jp2cBoxWithCodestream(byte[] codestream)
        {
            int at = BeginBox(0x6A703263); // 'jp2c'
            _buf.AddRange(codestream);
            EndBox(at);
            return this;
        }

        /// <summary>
        /// jp2h superbox with ihdr + colr + an 8-bit RGB palette (pclr) +
        /// component mapping (cmap) describing a 1-component indexed image.
        /// </summary>
        public Jp2Bytes Jp2HeaderForIndexedRgb(int width, int height, byte[,] paletteRgb)
        {
            int numEntries = paletteRgb.GetLength(0);
            if (paletteRgb.GetLength(1) != 3)
                throw new System.ArgumentException("Palette must have 3 columns (R, G, B).");

            int superAt = BeginBox(0x6A703268); // 'jp2h'

            // ihdr (single indexed component, 8 bit unsigned)
            int ihdrAt = BeginBox(0x69686472);
            U32((uint)height).U32((uint)width).U16(1);
            U8(7);             // BPC = 8 bit unsigned (0x07 = (8-1) with high bit clear)
            U8(0x07);          // C = wavelet
            U8(0);             // UnkC
            U8(0);             // IPR
            EndBox(ihdrAt);

            // colr — sRGB, since palette outputs RGB.
            int colrAt = BeginBox(0x636F6C72);
            U8(1).U8(0).U8(0).U32(16);
            EndBox(colrAt);

            // pclr
            int pclrAt = BeginBox(0x70636C72);
            U16(numEntries);
            U8(3);                  // NPC
            U8(7).U8(7).U8(7);      // bit depths: 8 unsigned, 8 unsigned, 8 unsigned
            for (var i = 0; i < numEntries; i++)
            {
                _buf.Add(paletteRgb[i, 0]);
                _buf.Add(paletteRgb[i, 1]);
                _buf.Add(paletteRgb[i, 2]);
            }
            EndBox(pclrAt);

            // cmap: 3 channels, each maps codestream component 0 via palette
            // columns 0, 1, 2.
            int cmapAt = BeginBox(0x636D6170);
            for (byte col = 0; col < 3; col++)
            {
                U16(0).U8(1).U8(col); // CMP=0, MTYP=1 (palette), PCOL=col
            }
            EndBox(cmapAt);

            EndBox(superAt);
            return this;
        }
    }
}
