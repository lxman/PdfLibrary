using System;
using System.Collections.Generic;
using ImageLibrary.Jp2.Pipeline;

namespace ImageLibrary.Jp2.Tier2;

/// <summary>
/// Tier-2 decoder: parses packets from tile-part bitstream.
/// Extracts code-block bitstreams organized by resolution level and subband.
/// </summary>
internal class Tier2Decoder : ITier2Decoder
{
    private readonly Jp2Codestream _codestream;
    private readonly Jp2Frame _frame;
    private readonly CodingParameters _coding;

    public Tier2Decoder(Jp2Codestream codestream)
    {
        _codestream = codestream;
        _frame = codestream.Frame;
        _coding = codestream.CodingParameters;
    }

    public Tier2Output Process(Jp2TilePart input)
    {
        using var decoder = new TilePartDecoder(_codestream, input);
        return decoder.Decode();
    }

    /// <summary>
    /// Decodes all tile-parts and returns organized code-block data per component.
    /// </summary>
    public Tier2Output[] DecodeAllComponents(Jp2TilePart tilePart)
    {
        using var decoder = new TilePartDecoder(_codestream, tilePart);
        return decoder.DecodeAllComponents();
    }
}

/// <summary>
/// Decodes a single tile-part's packet data.
/// </summary>
internal class TilePartDecoder : IDisposable
{
    private readonly Jp2Codestream _codestream;
    private readonly Jp2TilePart _tilePart;
    private readonly Jp2Frame _frame;
    private readonly CodingParameters _coding;
    private readonly int _numResolutions;
    private readonly int _numComponents;
    private readonly int _numLayers;

    // Per-component, per-resolution, per-subband code-block data
    private readonly List<CodeBlockInfo>[,,][] _codeBlocks;

    // Tag trees for inclusion and zero bit-planes
    private readonly TagTree[,,][] _inclusionTrees;
    private readonly TagTree[,,][] _zeroBitPlaneTrees;

    // Code-block inclusion state (first layer where it's included)
    private readonly int[,,][,] _firstInclusion;

    // Packet counter for header dump
    private int _packetNumber = 0;
    private readonly System.IO.StreamWriter? _packetHeaderLog;
    private readonly System.IO.StreamWriter? _tier1InputLog;

    public TilePartDecoder(Jp2Codestream codestream, Jp2TilePart tilePart)
    {
        _codestream = codestream;
        _tilePart = tilePart;
        _frame = codestream.Frame;
        _coding = codestream.CodingParameters;

        _numResolutions = _coding.DecompositionLevels + 1;
        _numComponents = _frame.ComponentCount;
        _numLayers = _coding.LayerCount;

        // Initialize code-block storage
        // [component][resolution][subband] -> list of code blocks
        _codeBlocks = new List<CodeBlockInfo>[_numComponents, _numResolutions, 4][];
        _inclusionTrees = new TagTree[_numComponents, _numResolutions, 4][];
        _zeroBitPlaneTrees = new TagTree[_numComponents, _numResolutions, 4][];
        _firstInclusion = new int[_numComponents, _numResolutions, 4][,];

        // Initialize packet header log
        try
        {
            _packetHeaderLog = new System.IO.StreamWriter(@"C:\temp\our-packet-headers.txt", false);
            _packetHeaderLog.WriteLine("=== PACKET HEADER DUMP ===");
            _packetHeaderLog.WriteLine($"NumComponents: {_numComponents}");
            _packetHeaderLog.WriteLine($"NumResolutions: {_numResolutions}");
            _packetHeaderLog.WriteLine($"NumLayers: {_numLayers}");
            _packetHeaderLog.WriteLine($"Progression: {_coding.Progression}");
            _packetHeaderLog.WriteLine();
        }
        catch
        {
            _packetHeaderLog = null;
        }

        // Initialize Tier-1 input log (assembled code-blocks)
        try
        {
            _tier1InputLog = new System.IO.StreamWriter(@"C:\temp\our-tier1-inputs.txt", false);
            _tier1InputLog.WriteLine("=== TIER-1 INPUT CODE-BLOCKS ===");
            _tier1InputLog.WriteLine($"NumComponents: {_numComponents}");
            _tier1InputLog.WriteLine($"NumResolutions: {_numResolutions}");
            _tier1InputLog.WriteLine();
        }
        catch
        {
            _tier1InputLog = null;
        }

        InitializeCodeBlockStructure();
    }

    private void InitializeCodeBlockStructure()
    {
        // Calculate tile dimensions
        int tileIdx = _tilePart.TileIndex;
        int numTilesX = _frame.NumTilesX;
        int tileX = tileIdx % numTilesX;
        int tileY = tileIdx / numTilesX;

        int tileStartX = tileX * _frame.TileWidth + _frame.TileXOffset;
        int tileStartY = tileY * _frame.TileHeight + _frame.TileYOffset;
        int tileEndX = Math.Min(tileStartX + _frame.TileWidth, _frame.Width);
        int tileEndY = Math.Min(tileStartY + _frame.TileHeight, _frame.Height);
        int tileWidth = tileEndX - tileStartX;
        int tileHeight = tileEndY - tileStartY;

        for (var c = 0; c < _numComponents; c++)
        {
            Jp2Component comp = _frame.Components[c];
            int compTileWidth = (tileWidth + comp.XSubsampling - 1) / comp.XSubsampling;
            int compTileHeight = (tileHeight + comp.YSubsampling - 1) / comp.YSubsampling;

            for (var r = 0; r < _numResolutions; r++)
            {
                // Calculate subband dimensions at this resolution using ceiling division
                int shift = _numResolutions - 1 - r;
                int resWidth = (compTileWidth + (1 << shift) - 1) >> shift;
                int resHeight = (compTileHeight + (1 << shift) - 1) >> shift;
                if (resWidth == 0) resWidth = 1;
                if (resHeight == 0) resHeight = 1;

                // Number of subbands at this resolution
                // Resolution 0: subband 0 (LL)
                // Resolution > 0: subbands 1, 2, 3 (HL, LH, HH)
                int startSubband = (r == 0) ? 0 : 1;
                int numSubbands = (r == 0) ? 1 : 4;

                Console.WriteLine($"[INIT] c={c} r={r} startSubband={startSubband} numSubbands={numSubbands}");
                for (var s = startSubband; s < numSubbands; s++)
                {
                    int subbandWidth, subbandHeight;
                    if (r == 0)
                    {
                        // LL subband at lowest resolution
                        subbandWidth = resWidth;
                        subbandHeight = resHeight;
                    }
                    else
                    {
                        // Subband dimensions depend on orientation:
                        // Subband 1 (HL): high horizontal (floor), low vertical (ceil)
                        // Subband 2 (LH): low horizontal (ceil), high vertical (floor)
                        // Subband 3 (HH): high both (floor)
                        switch (s)
                        {
                            case 1: // HL
                                subbandWidth = resWidth / 2;
                                subbandHeight = (resHeight + 1) / 2;
                                break;
                            case 2: // LH
                                subbandWidth = (resWidth + 1) / 2;
                                subbandHeight = resHeight / 2;
                                break;
                            default: // HH (case 3)
                                subbandWidth = resWidth / 2;
                                subbandHeight = resHeight / 2;
                                break;
                        }
                    }

                    // Calculate number of code-blocks
                    int cbWidth = _coding.CodeBlockWidth;
                    int cbHeight = _coding.CodeBlockHeight;
                    int numCbX = (subbandWidth + cbWidth - 1) / cbWidth;
                    int numCbY = (subbandHeight + cbHeight - 1) / cbHeight;
                    if (numCbX == 0) numCbX = 1;
                    if (numCbY == 0) numCbY = 1;

                    Console.WriteLine($"[GRID] c={c} r={r} s={s}: subband={subbandWidth}x{subbandHeight}, cb={cbWidth}x{cbHeight}, grid={numCbX}x{numCbY} ({numCbX * numCbY} blocks)");

                    _codeBlocks[c, r, s] = new List<CodeBlockInfo>[numCbY * numCbX];
                    _inclusionTrees[c, r, s] = [new TagTree(numCbX, numCbY)];
                    _zeroBitPlaneTrees[c, r, s] = [new TagTree(numCbX, numCbY)];
                    _firstInclusion[c, r, s] = new int[numCbY, numCbX];

                    for (var by = 0; by < numCbY; by++)
                    {
                        for (var bx = 0; bx < numCbX; bx++)
                        {
                            int idx = by * numCbX + bx;
                            _codeBlocks[c, r, s][idx] = [];
                            _firstInclusion[c, r, s][by, bx] = -1; // Not yet included
                        }
                    }
                }
            }
        }
    }

    public Tier2Output Decode()
    {
        // Decode first component only
        return DecodeComponent(0);
    }

    public Tier2Output[] DecodeAllComponents()
    {
        var results = new Tier2Output[_numComponents];

        // Parse all packets first
        ParseAllPackets();

        // Then build output for each component
        for (var c = 0; c < _numComponents; c++)
        {
            results[c] = BuildComponentOutput(c);
        }

        return results;
    }

    private void ParseAllPackets()
    {
        if (_tilePart.BitstreamData == null || _tilePart.BitstreamData.Length == 0)
            return;

        var reader = new BitReader(_tilePart.BitstreamData);

        // Parse packets according to progression order
        switch (_coding.Progression)
        {
            case ProgressionOrder.LRCP:
                ParseLRCP(reader);
                break;
            case ProgressionOrder.RLCP:
                ParseRLCP(reader);
                break;
            case ProgressionOrder.RPCL:
                ParseRPCL(reader);
                break;
            case ProgressionOrder.PCRL:
                ParsePCRL(reader);
                break;
            case ProgressionOrder.CPRL:
                ParseCPRL(reader);
                break;
        }
    }

    private void ParseLRCP(BitReader reader)
    {
        for (var l = 0; l < _numLayers; l++)
        {
            for (var r = 0; r < _numResolutions; r++)
            {
                for (var c = 0; c < _numComponents; c++)
                {
                    // For each precinct (assuming 1 precinct per resolution for now)
                    ParsePacket(reader, c, r, l);
                }
            }
        }
    }

    private void ParseRLCP(BitReader reader)
    {
        for (var r = 0; r < _numResolutions; r++)
        {
            for (var l = 0; l < _numLayers; l++)
            {
                for (var c = 0; c < _numComponents; c++)
                {
                    ParsePacket(reader, c, r, l);
                }
            }
        }
    }

    private void ParseRPCL(BitReader reader)
    {
        for (var r = 0; r < _numResolutions; r++)
        {
            // Single precinct assumed
            for (var c = 0; c < _numComponents; c++)
            {
                for (var l = 0; l < _numLayers; l++)
                {
                    ParsePacket(reader, c, r, l);
                }
            }
        }
    }

    private void ParsePCRL(BitReader reader)
    {
        // Single precinct assumed
        for (var c = 0; c < _numComponents; c++)
        {
            for (var r = 0; r < _numResolutions; r++)
            {
                for (var l = 0; l < _numLayers; l++)
                {
                    ParsePacket(reader, c, r, l);
                }
            }
        }
    }

    private void ParseCPRL(BitReader reader)
    {
        for (var c = 0; c < _numComponents; c++)
        {
            // Single precinct assumed
            for (var r = 0; r < _numResolutions; r++)
            {
                for (var l = 0; l < _numLayers; l++)
                {
                    ParsePacket(reader, c, r, l);
                }
            }
        }
    }

    private void ParsePacket(BitReader reader, int component, int resolution, int layer)
    {
        if (!reader.HasMoreData)
            return;

        _packetHeaderLog?.WriteLine($"--- Packet {_packetNumber} ---");
        _packetHeaderLog?.WriteLine($"Component: {component}, Resolution: {resolution}, Layer: {layer}");

        // Check for SOP marker if enabled
        if ((_coding.Style & CodingStyle.SopMarkers) != 0)
        {
            if (reader.PeekMarker())
            {
                reader.AlignToByte();
                // Skip SOP marker (6 bytes: FF91 + 4 byte length + 2 byte Nsop)
                reader.Seek(reader.BytePosition + 6);
            }
        }

        // Read packet header
        // First bit: packet empty flag (0 = empty, 1 = non-empty)
        int packetPresent = reader.ReadBit();

        _packetHeaderLog?.WriteLine($"PacketPresent: {packetPresent}");

        if (packetPresent == 0)
        {
            // Empty packet - skip to EPH if present
            _packetHeaderLog?.WriteLine("Empty packet");
            _packetHeaderLog?.WriteLine();
            _packetNumber++;
            if ((_coding.Style & CodingStyle.EphMarkers) != 0)
            {
                reader.AlignToByte();
                // Skip EPH marker (FF92)
                if (reader.BytePosition + 1 < reader.Length)
                {
                    reader.Seek(reader.BytePosition + 2);
                }
            }
            return;
        }

        // Parse code-block contributions
        // At resolution 0: subband 0 (LL)
        // At resolution > 0: subbands 1, 2, 3 (HL, LH, HH detail subbands)
        int startSubband = (resolution == 0) ? 0 : 1;
        int numSubbands = (resolution == 0) ? 1 : 4;

        var contributions = new List<(int cbx, int cby, int subband, int passes, int length, int zeroBitPlanes)>();

        for (var s = startSubband; s < numSubbands; s++)
        {
            List<CodeBlockInfo>[]? blockList = _codeBlocks[component, resolution, s];
            if (blockList == null || blockList.Length == 0)
                continue;

            TagTree inclusionTree = _inclusionTrees[component, resolution, s][0];
            TagTree zeroBpTree = _zeroBitPlaneTrees[component, resolution, s][0];
            int[,] firstIncl = _firstInclusion[component, resolution, s];

            int numCbX = GetNumCodeBlocksX(component, resolution, s);
            int numCbY = GetNumCodeBlocksY(component, resolution, s);

            for (var cby = 0; cby < numCbY; cby++)
            {
                for (var cbx = 0; cbx < numCbX; cbx++)
                {
                    bool included;

                    if (firstIncl[cby, cbx] < 0)
                    {
                        // First inclusion - use tag tree
                        included = inclusionTree.Decode(reader, cbx, cby, layer);
                        if (included)
                        {
                            firstIncl[cby, cbx] = layer;

                            // Decode zero bit-planes using tag tree
                            int zeroBp = zeroBpTree.DecodeValue(reader, cbx, cby);

                            // Debug: log zero bitplane values
                            Console.WriteLine($"[TIER2] c={component} r={resolution} s={s} cb=({cbx},{cby}) layer={layer} zeroBp={zeroBp}");

                            // Decode number of coding passes
                            int passes = DecodeNumPasses(reader);

                            // Decode length
                            int length = DecodeLength(reader, passes);

                            contributions.Add((cbx, cby, s, passes, length, zeroBp));

                            _packetHeaderLog?.WriteLine($"  CB({cbx},{cby}) subband={s} FIRST_INCL zeroBp={zeroBp}");
                            _packetHeaderLog?.WriteLine($"  Passes={passes} Length={length}");
                        }
                    }
                    else
                    {
                        // Already included before - single bit for inclusion
                        included = reader.ReadBit() == 1;
                        if (included)
                        {
                            int passes = DecodeNumPasses(reader);
                            int length = DecodeLength(reader, passes);

                            // Zero bit-planes already known from first inclusion
                            contributions.Add((cbx, cby, s, passes, length, -1));

                            _packetHeaderLog?.WriteLine($"  CB({cbx},{cby}) subband={s} INCL");
                            _packetHeaderLog?.WriteLine($"  Passes={passes} Length={length}");
                        }
                    }
                }
            }
        }

        // Check for EPH marker if enabled
        if ((_coding.Style & CodingStyle.EphMarkers) != 0)
        {
            reader.AlignToByte();
            if (reader.BytePosition + 1 < reader.Length &&
                reader.BytePosition >= 0)
            {
                // Skip EPH marker (FF92)
                reader.Seek(reader.BytePosition + 2);
            }
        }

        // Byte align before reading code-block data
        reader.AlignToByte();

        // Read code-block data
        foreach ((int cbx, int cby, int s, int passes, int length, int zeroBp) in contributions)
        {
            byte[] data = reader.ReadBytes(length);

            // Log compressed data
            if (_packetHeaderLog != null)
            {
                _packetHeaderLog.WriteLine($"  CB({cbx},{cby}) subband={s} DATA[{length} bytes]:");

                // Output hex dump of compressed data
                const int bytesPerLine = 16;
                for (int i = 0; i < data.Length; i += bytesPerLine)
                {
                    int lineLength = Math.Min(bytesPerLine, data.Length - i);
                    string hex = BitConverter.ToString(data, i, lineLength).Replace("-", " ");
                    _packetHeaderLog.WriteLine($"    {i:X4}: {hex}");
                }
            }

            int idx = cby * GetNumCodeBlocksX(component, resolution, s) + cbx;
            _codeBlocks[component, resolution, s][idx].Add(new CodeBlockInfo
            {
                Layer = layer,
                CodingPasses = passes,
                ZeroBitPlanes = zeroBp >= 0 ? zeroBp : GetStoredZeroBitPlanes(component, resolution, s, cbx, cby),
                Data = data,
            });
        }

        _packetHeaderLog?.WriteLine();
        _packetNumber++;
    }

    private int DecodeNumPasses(BitReader reader)
    {
        // Decode number of coding passes using incremental scheme
        // (matches Melville/JJ2000 implementation)
        // Start with 1 pass (caller already counted this)
        // 0: no additional passes (total 1)
        // 1,0: +1 (total 2)
        // 1,1,xx: +1+1+xx (total 3+xx, range 3-6)
        // 1,1,11,xxxxx: +1+1+3+xxxxx (total 6+xxxxx, range 6-37)
        // 1,1,11,11111,xxxxxxx: +1+1+3+31+xxxxxxx (total 37+xxxxxxx)

        var passes = 1;

        // Read first bit: if 0, done with 1 pass
        if (reader.ReadBit() == 0)
            return passes;

        // First bit was 1, add one pass
        passes++;

        // Read second bit: if 0, done with 2 passes
        if (reader.ReadBit() == 0)
            return passes;

        // Second bit was 1, add one more pass and read 2 more bits
        passes++;
        int tmp = reader.ReadBits(2);
        passes += tmp;

        // If 2-bit value wasn't 11 (3), done
        if (tmp != 3)
            return passes;

        // Read 5 more bits
        tmp = reader.ReadBits(5);
        passes += tmp;

        // If 5-bit value wasn't 11111 (31), done
        if (tmp != 31)
            return passes;

        // Read 7 more bits
        passes += reader.ReadBits(7);

        return passes;
    }

    private int DecodeLength(BitReader reader, int passes)
    {
        // Decode code-block data length per JPEG2000 spec
        // lblock starts at 3 and increments for each leading 1 bit
        var lblock = 3;

        // Count leading 1s to increase lblock
        while (reader.ReadBit() == 1)
        {
            lblock++;
        }

        // Total bits for length = lblock + floor(log2(passes))
        // For passes=1, add 0; for passes=2, add 1; for passes=3-4, add 2; etc.
        var passAdd = 0;
        if (passes > 1)
        {
            passAdd = (int)Math.Floor(Math.Log(passes, 2));
        }

        int lengthBits = lblock + passAdd;

        // Read length value
        return lengthBits > 0
            ? reader.ReadBits(lengthBits)
            : 0;
    }

    private int GetNumCodeBlocksX(int component, int resolution, int subband)
    {
        List<CodeBlockInfo>[]? blocks = _codeBlocks[component, resolution, subband];
        if (blocks == null) return 0;

        // Infer from first inclusion array dimensions
        int[,]? firstIncl = _firstInclusion[component, resolution, subband];
        if (firstIncl == null) return 0;
        return firstIncl.GetLength(1);
    }

    private int GetNumCodeBlocksY(int component, int resolution, int subband)
    {
        int[,]? firstIncl = _firstInclusion[component, resolution, subband];
        if (firstIncl == null) return 0;
        return firstIncl.GetLength(0);
    }

    private int GetStoredZeroBitPlanes(int component, int resolution, int subband, int cbx, int cby)
    {
        int idx = cby * GetNumCodeBlocksX(component, resolution, subband) + cbx;
        List<CodeBlockInfo> list = _codeBlocks[component, resolution, subband][idx];
        if (list.Count > 0)
            return list[0].ZeroBitPlanes;
        return 0;
    }

    private Tier2Output DecodeComponent(int component)
    {
        ParseAllPackets();
        return BuildComponentOutput(component);
    }

    private Tier2Output BuildComponentOutput(int component)
    {
        var resolutionBlocks = new CodeBlockBitstream[_numResolutions][][];

        // Calculate tile dimensions
        int tileIdx = _tilePart.TileIndex;
        int numTilesX = _frame.NumTilesX;
        int tileX = tileIdx % numTilesX;
        int tileY = tileIdx / numTilesX;

        int tileStartX = tileX * _frame.TileWidth + _frame.TileXOffset;
        int tileStartY = tileY * _frame.TileHeight + _frame.TileYOffset;
        int tileEndX = Math.Min(tileStartX + _frame.TileWidth, _frame.Width);
        int tileEndY = Math.Min(tileStartY + _frame.TileHeight, _frame.Height);
        int tileWidth = tileEndX - tileStartX;
        int tileHeight = tileEndY - tileStartY;

        // Apply component subsampling
        Jp2Component comp = _frame.Components[component];
        int compTileWidth = (tileWidth + comp.XSubsampling - 1) / comp.XSubsampling;
        int compTileHeight = (tileHeight + comp.YSubsampling - 1) / comp.YSubsampling;

        for (var r = 0; r < _numResolutions; r++)
        {
            // Number of subbands at this resolution
            // Resolution 0: subband 0 (LL)
            // Resolution > 0: subbands 1, 2, 3 (HL, LH, HH)
            int startSubband = (r == 0) ? 0 : 1;
            int numSubbands = (r == 0) ? 1 : 4;
            resolutionBlocks[r] = new CodeBlockBitstream[numSubbands][];

            // Calculate subband dimensions at this resolution using ceiling division
            // This matches JPEG2000 spec for subband dimensions
            int shift = _numResolutions - 1 - r;
            int resWidth = (compTileWidth + (1 << shift) - 1) >> shift;
            int resHeight = (compTileHeight + (1 << shift) - 1) >> shift;
            if (resWidth == 0) resWidth = 1;
            if (resHeight == 0) resHeight = 1;

            for (var s = startSubband; s < numSubbands; s++)
            {
                List<CodeBlockInfo>[]? blockList = _codeBlocks[component, r, s];
                if (blockList == null)
                {
                    resolutionBlocks[r][s] = [];
                    continue;
                }

                // Calculate actual subband dimensions
                int subbandWidth, subbandHeight;
                if (r == 0)
                {
                    subbandWidth = resWidth;
                    subbandHeight = resHeight;
                }
                else
                {
                    // Subband dimensions depend on orientation:
                    // Subband 1 (HL): high horizontal (floor), low vertical (ceil)
                    // Subband 2 (LH): low horizontal (ceil), high vertical (floor)
                    // Subband 3 (HH): high both (floor)
                    switch (s)
                    {
                        case 1: // HL
                            subbandWidth = resWidth / 2;
                            subbandHeight = (resHeight + 1) / 2;
                            break;
                        case 2: // LH
                            subbandWidth = (resWidth + 1) / 2;
                            subbandHeight = resHeight / 2;
                            break;
                        default: // HH (case 3)
                            subbandWidth = resWidth / 2;
                            subbandHeight = resHeight / 2;
                            break;
                    }
                }

                int numCbX = GetNumCodeBlocksX(component, r, s);
                int numCbY = GetNumCodeBlocksY(component, r, s);
                int maxCbWidth = _coding.CodeBlockWidth;
                int maxCbHeight = _coding.CodeBlockHeight;

                var subbandBlocks = new List<CodeBlockBitstream>();

                for (var cby = 0; cby < numCbY; cby++)
                {
                    for (var cbx = 0; cbx < numCbX; cbx++)
                    {
                        int idx = cby * numCbX + cbx;
                        List<CodeBlockInfo>? layerData = blockList[idx];

                        // Calculate actual code-block dimensions (may be smaller at edges)
                        int startX = cbx * maxCbWidth;
                        int startY = cby * maxCbHeight;
                        int actualWidth = Math.Min(maxCbWidth, subbandWidth - startX);
                        int actualHeight = Math.Min(maxCbHeight, subbandHeight - startY);
                        if (actualWidth <= 0 || actualHeight <= 0) continue;

                        // Merge all layer contributions
                        var totalPasses = 0;
                        var zeroBp = 0;
                        var allData = new List<byte>();

                        if (layerData != null)
                        {
                            foreach (CodeBlockInfo info in layerData)
                            {
                                totalPasses += info.CodingPasses;
                                // ZeroBitPlanes can legitimately be 0 (all bits significant)
                                // Only -1 indicates "use previously stored value"
                                if (info.ZeroBitPlanes >= 0)
                                    zeroBp = info.ZeroBitPlanes;
                                allData.AddRange(info.Data);
                            }
                        }

                        // Always include code-block even if it has no data
                        // Zero-data blocks are valid in JPEG2000 (represents all-zero coefficients)
                        {
                            // Determine subband type
                            SubbandType subbandType;
                            if (r == 0)
                            {
                                subbandType = SubbandType.LL;
                            }
                            else
                            {
                                subbandType = s switch { 1 => SubbandType.HL, 2 => SubbandType.LH, _ => SubbandType.HH };
                            }

                            // Log assembled code-block input for Tier-1 decoder verification
                            _tier1InputLog?.WriteLine($"=== CODE-BLOCK ===");
                            _tier1InputLog?.WriteLine($"Resolution={r}, Subband={s}, CB({cbx},{cby})");
                            _tier1InputLog?.WriteLine($"Dimensions: {actualWidth}x{actualHeight}");
                            _tier1InputLog?.WriteLine($"Total Passes: {totalPasses}");
                            _tier1InputLog?.WriteLine($"Zero Bit-Planes: {zeroBp}");
                            _tier1InputLog?.WriteLine($"Data Length: {allData.Count} bytes");
                            if (allData.Count > 0)
                            {
                                int displayBytes = Math.Min(32, allData.Count);
                                string hex = BitConverter.ToString(allData.ToArray(), 0, displayBytes).Replace("-", " ");
                                _tier1InputLog?.WriteLine($"First {displayBytes} bytes: {hex}");
                            }
                            _tier1InputLog?.WriteLine();

                            subbandBlocks.Add(new CodeBlockBitstream
                            {
                                BlockX = cbx,
                                BlockY = cby,
                                Width = actualWidth,
                                Height = actualHeight,
                                CodingPasses = totalPasses,
                                ZeroBitPlanes = zeroBp,
                                Data = allData.ToArray(),
                                SubbandType = subbandType,
                            });
                        }
                    }
                }

                resolutionBlocks[r][s] = subbandBlocks.ToArray();
            }
        }

        return new Tier2Output
        {
            TileIndex = _tilePart.TileIndex,
            ComponentIndex = component,
            ResolutionLevels = _numResolutions,
            CodeBlocks = resolutionBlocks,
        };
    }

    public void Dispose()
    {
        _packetHeaderLog?.Flush();
        _packetHeaderLog?.Close();
        _packetHeaderLog?.Dispose();

        _tier1InputLog?.Flush();
        _tier1InputLog?.Close();
        _tier1InputLog?.Dispose();
    }
}

/// <summary>
/// Information about a code-block's contribution in a single layer.
/// </summary>
internal class CodeBlockInfo
{
    public int Layer { get; set; }
    public int CodingPasses { get; set; }
    public int ZeroBitPlanes { get; set; }
    public byte[] Data { get; set; } = [];
}