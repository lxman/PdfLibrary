using System.Collections.Generic;

namespace FontParser.Tables.WOFF2.GlyfReconstruct
{
    public interface IGlyphInfo
    {
        ushort InstructionCount { get; set; }

        List<byte> Instructions { get; }
    }
}