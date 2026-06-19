namespace Jp2Codec.Codestream
{
    /// <summary>
    /// Marker code constants per ISO/IEC 15444-1 Table A-1 (J2K Part 1 only;
    /// Part 2 markers in the 0xFF7x range are not modelled).
    ///
    /// All J2K markers are two-byte big-endian sequences whose high byte is
    /// 0xFF and whose low byte is 0x4F or higher (0x4F..0x7F are Part 1 valid;
    /// 0x80..0xC2 are reserved; 0x90..0x93, 0xD9 are delimiting; 0x4F is SOC;
    /// the rest are header markers).
    /// </summary>
    internal static class MarkerCode
    {
        // ---- Delimiting markers ----
        /// <summary>Start of codestream (required, first marker).</summary>
        public const ushort Soc = 0xFF4F;
        /// <summary>Start of tile-part.</summary>
        public const ushort Sot = 0xFF90;
        /// <summary>Start of data (last marker before packet stream).</summary>
        public const ushort Sod = 0xFF93;
        /// <summary>End of codestream (required, last marker).</summary>
        public const ushort Eoc = 0xFFD9;

        // ---- Fixed-information marker ----
        /// <summary>Image and tile size — required, in main header right after SOC.</summary>
        public const ushort Siz = 0xFF51;

        // ---- Functional markers ----
        /// <summary>Coding style default — required in main header.</summary>
        public const ushort Cod = 0xFF52;
        /// <summary>Coding style component (override of COD).</summary>
        public const ushort Coc = 0xFF53;
        /// <summary>Region of interest.</summary>
        public const ushort Rgn = 0xFF5E;
        /// <summary>Quantization default — required in main header.</summary>
        public const ushort Qcd = 0xFF5C;
        /// <summary>Quantization component (override of QCD).</summary>
        public const ushort Qcc = 0xFF5D;
        /// <summary>Progression order change.</summary>
        public const ushort Poc = 0xFF5F;

        // ---- Pointer markers (optional, for random access) ----
        /// <summary>Tile-part lengths.</summary>
        public const ushort Tlm = 0xFF55;
        /// <summary>Packet length, main header.</summary>
        public const ushort Plm = 0xFF57;
        /// <summary>Packet length, tile-part header.</summary>
        public const ushort Plt = 0xFF58;
        /// <summary>Packed packet headers, main header.</summary>
        public const ushort Ppm = 0xFF60;
        /// <summary>Packed packet headers, tile-part header.</summary>
        public const ushort Ppt = 0xFF61;

        // ---- In-bitstream markers (inside packet stream) ----
        /// <summary>Start of packet (optional).</summary>
        public const ushort Sop = 0xFF91;
        /// <summary>End of packet header (optional).</summary>
        public const ushort Eph = 0xFF92;

        // ---- Informational markers ----
        /// <summary>Component registration (subpixel offset hint).</summary>
        public const ushort Crg = 0xFF63;
        /// <summary>Comment.</summary>
        public const ushort Com = 0xFF64;

        /// <summary>
        /// Markers carrying a 2-byte length-of-segment field (Lxxx) immediately
        /// after the marker code. The delimiting markers (SOC, SOD, EOC, EPH)
        /// stand alone with no payload.
        /// </summary>
        public static bool HasSegmentLength(ushort marker)
        {
            switch (marker)
            {
                case Soc:
                case Sod:
                case Eoc:
                case Eph:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Returns true if the byte pair is a valid Part 1 marker code. Used by
        /// the codestream reader to validate marker boundaries.
        /// </summary>
        public static bool IsValidMarker(ushort code)
        {
            if ((code & 0xFF00) != 0xFF00) return false;
            var low = (byte)(code & 0xFF);
            // Part 1 markers are sparse. Whitelist what we model so a stray 0xFF
            // followed by an arbitrary byte does not silently look like a marker.
            switch (code)
            {
                case Soc:
                case Sot:
                case Sod:
                case Eoc:
                case Siz:
                case Cod:
                case Coc:
                case Rgn:
                case Qcd:
                case Qcc:
                case Poc:
                case Tlm:
                case Plm:
                case Plt:
                case Ppm:
                case Ppt:
                case Sop:
                case Eph:
                case Crg:
                case Com:
                    return true;
                default:
                    // Reserved-for-future markers in the legal range (Part 1
                    // 0x4F..0x6F and 0x90..0x93, 0xD9) — accept them as markers
                    // so the parser can choose to skip-unknown rather than
                    // mistake them for codestream data.
                    return low >= 0x4F;
            }
        }

        /// <summary>Pretty-print a marker code for diagnostic messages.</summary>
        public static string Format(ushort code)
        {
            switch (code)
            {
                case Soc: return "SOC";
                case Sot: return "SOT";
                case Sod: return "SOD";
                case Eoc: return "EOC";
                case Siz: return "SIZ";
                case Cod: return "COD";
                case Coc: return "COC";
                case Rgn: return "RGN";
                case Qcd: return "QCD";
                case Qcc: return "QCC";
                case Poc: return "POC";
                case Tlm: return "TLM";
                case Plm: return "PLM";
                case Plt: return "PLT";
                case Ppm: return "PPM";
                case Ppt: return "PPT";
                case Sop: return "SOP";
                case Eph: return "EPH";
                case Crg: return "CRG";
                case Com: return "COM";
                default: return $"0x{code:X4}";
            }
        }
    }
}
