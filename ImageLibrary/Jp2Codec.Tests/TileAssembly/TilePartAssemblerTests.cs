using Jp2Codec.Codestream;
using Jp2Codec.TileAssembly;
using Jp2Codec.Tests.Codestream;

namespace Jp2Codec.Tests.TileAssembly;

public class TilePartAssemblerTests
{
    // Build a codestream that already has its main header consumed by the
    // caller — i.e. a tile-part series starting at SOT and ending with EOC.
    private static byte[] BuildTilePart(int tileIndex, int tpsot, int tnsot, byte[] body)
    {
        var b = new HeaderBytes();
        // SOT marker segment is 12 bytes (FF90 + Lsot=10 + 8-byte payload).
        // Add 2-byte SOD then the body. Psot = total tile-part length, measured
        // from the first byte of the SOT marker.
        uint psot = (uint)(12 + 2 + body.Length);
        b.Sot(tileIndex: tileIndex, psot: psot, tpsot: tpsot, tnsot: tnsot);
        b.Marker(0xFF93); // SOD
        b.Bytes(body);
        return b.ToArray();
    }

    private static CodestreamReader ReaderOver(params byte[][] parts)
    {
        int total = 0;
        foreach (byte[] p in parts) total += p.Length;
        // Append EOC at the end to mirror a real codestream.
        var buf = new byte[total + 2];
        int o = 0;
        foreach (byte[] p in parts)
        {
            Buffer.BlockCopy(p, 0, buf, o, p.Length);
            o += p.Length;
        }
        buf[o] = 0xFF;
        buf[o + 1] = 0xD9; // EOC
        return new CodestreamReader(buf);
    }

    [Fact]
    public void Assemble_SingleTilePart_ProducesOneTileWithBody()
    {
        byte[] body = [0x10, 0x20, 0x30, 0x40];
        byte[] tp = BuildTilePart(tileIndex: 0, tpsot: 0, tnsot: 1, body);
        CodestreamReader r = ReaderOver(tp);

        IReadOnlyList<AssembledTile> tiles = TilePartAssembler.Assemble(r, numberOfComponents: 1);
        Assert.Single(tiles);
        Assert.Equal(0, tiles[0].TileIndex);
        Assert.Equal(body, tiles[0].PacketBody);
        Assert.Null(tiles[0].CodOverride);
        Assert.Null(tiles[0].QcdOverride);
    }

    [Fact]
    public void Assemble_MultipleTilePartsOfOneTile_ConcatenatesInTpsotOrder()
    {
        byte[] body0 = [0xAA, 0xBB];
        byte[] body1 = [0xCC, 0xDD, 0xEE];
        byte[] body2 = [0x11];
        byte[] tp0 = BuildTilePart(0, 0, 3, body0);
        byte[] tp1 = BuildTilePart(0, 1, 3, body1);
        byte[] tp2 = BuildTilePart(0, 2, 3, body2);
        CodestreamReader r = ReaderOver(tp0, tp1, tp2);

        IReadOnlyList<AssembledTile> tiles = TilePartAssembler.Assemble(r, numberOfComponents: 1);
        Assert.Single(tiles);
        Assert.Equal([0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0x11], tiles[0].PacketBody);
    }

    [Fact]
    public void Assemble_MultipleTiles_GroupsAndOrdersByTileIndex()
    {
        byte[] body0 = [0x01];
        byte[] body1 = [0x02];
        byte[] tp0 = BuildTilePart(1, 0, 1, body0);
        byte[] tp1 = BuildTilePart(0, 0, 1, body1);
        // Order in codestream is tile 1 first, then tile 0. Result must sort by Isot.
        CodestreamReader r = ReaderOver(tp0, tp1);

        IReadOnlyList<AssembledTile> tiles = TilePartAssembler.Assemble(r, numberOfComponents: 1);
        Assert.Equal(2, tiles.Count);
        Assert.Equal(0, tiles[0].TileIndex);
        Assert.Equal([0x02], tiles[0].PacketBody);
        Assert.Equal(1, tiles[1].TileIndex);
        Assert.Equal([0x01], tiles[1].PacketBody);
    }

    [Fact]
    public void Assemble_OutOfOrderTilePartIndices_StillConcatenatesInOrder()
    {
        byte[] body0 = [0x10];
        byte[] body1 = [0x20];
        // TPsot signalling order: 1 first then 0. SortedDictionary should re-sort.
        byte[] tp1 = BuildTilePart(0, 1, 2, body1);
        byte[] tp0 = BuildTilePart(0, 0, 2, body0);
        CodestreamReader r = ReaderOver(tp1, tp0);

        IReadOnlyList<AssembledTile> tiles = TilePartAssembler.Assemble(r, numberOfComponents: 1);
        Assert.Equal([0x10, 0x20], tiles[0].PacketBody);
    }

    [Fact]
    public void Assemble_DuplicateTpsot_Throws()
    {
        byte[] body = [0x00];
        byte[] tp1 = BuildTilePart(0, 0, 2, body);
        byte[] tp2 = BuildTilePart(0, 0, 2, body);
        CodestreamReader r = ReaderOver(tp1, tp2);

        Assert.Throws<InvalidDataException>(() =>
            TilePartAssembler.Assemble(r, numberOfComponents: 1));
    }

    [Fact]
    public void Assemble_PsotZero_BodyRunsToNextSot()
    {
        // First tile-part has Psot=0 → assembler scans for the next SOT/EOC.
        byte[] body0 = [0x10, 0x20, 0x30];
        byte[] body1 = [0x40, 0x50];
        // Build with Psot=0 manually so we can write a body that the scanner
        // must locate. The HeaderBytes.Sot helper just stuffs psot verbatim.
        var bA = new HeaderBytes();
        bA.Sot(tileIndex: 0, psot: 0u, tpsot: 0, tnsot: 1);
        bA.Marker(0xFF93);
        bA.Bytes(body0);
        byte[] tpA = bA.ToArray();
        byte[] tpB = BuildTilePart(1, 0, 1, body1);
        CodestreamReader r = ReaderOver(tpA, tpB);

        IReadOnlyList<AssembledTile> tiles = TilePartAssembler.Assemble(r, numberOfComponents: 1);
        Assert.Equal(2, tiles.Count);
        Assert.Equal(body0, tiles[0].PacketBody);
        Assert.Equal(body1, tiles[1].PacketBody);
    }
}
