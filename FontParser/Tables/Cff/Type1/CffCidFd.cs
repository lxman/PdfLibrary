using System.Collections.Generic;

namespace FontParser.Tables.Cff.Type1
{
    /// <summary>
    /// Raw per-font-dict data for a CID-keyed CFF (one entry of the FDArray), retained for verbatim
    /// re-emit by the CFF subsetter. Index in <see cref="Type1Table.CidFds"/> equals the FD index that
    /// FDSelect maps glyphs to.
    /// </summary>
    public sealed class CffCidFd
    {
        /// <summary>Raw bytes of this FD's font DICT (one entry of the FDArray INDEX).</summary>
        public byte[] RawFontDict { get; }

        /// <summary>Raw bytes of this FD's Private DICT.</summary>
        public byte[] RawPrivateDict { get; }

        /// <summary>This FD's Local Subr INDEX entries (raw); empty if the FD has no local subrs.</summary>
        public List<List<byte>> LocalSubrs { get; }

        public CffCidFd(byte[] rawFontDict, byte[] rawPrivateDict, List<List<byte>> localSubrs)
        {
            RawFontDict = rawFontDict;
            RawPrivateDict = rawPrivateDict;
            LocalSubrs = localSubrs;
        }
    }
}
