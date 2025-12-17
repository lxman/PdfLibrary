using Xunit;
using Xunit.Abstractions;

namespace JpegCodec.Tests;

/// <summary>
/// Test if using decode-order storage matches ImageSharp.
/// </summary>
public class TestDecodeOrderStorage
{
    private readonly ITestOutputHelper _output;

    public TestDecodeOrderStorage(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void AnalyzeStorageMapping()
    {
        // Create a mapping from decode order to spatial storage index
        _output.WriteLine("Decode order to spatial storage index mapping for first 80 positions:");
        _output.WriteLine("DecodePos | MCU(x,y) | Sub(x,y) | SpatialIdx | PixelStart");
        _output.WriteLine("----------|----------|----------|------------|----------");

        var blocksPerRow = 38;
        var mcuCountX = 19;

        for (var decodePos = 0; decodePos < 80; decodePos++)
        {
            int mcuIdx = decodePos / 4;
            int subIdx = decodePos % 4;

            int mcuX = mcuIdx % mcuCountX;
            int mcuY = mcuIdx / mcuCountX;

            // Our sub-block iteration order is (0,0), (1,0), (0,1), (1,1)
            // subIdx 0 = (0,0), 1 = (1,0), 2 = (0,1), 3 = (1,1)
            int subX = subIdx % 2;
            int subY = subIdx / 2;

            int globalBlockX = mcuX * 2 + subX;
            int globalBlockY = mcuY * 2 + subY;
            int spatialIdx = globalBlockY * blocksPerRow + globalBlockX;

            int pixelX = globalBlockX * 8;
            int pixelY = globalBlockY * 8;

            if (decodePos is < 20 or 40 or 41 or 6)
            {
                _output.WriteLine($"    {decodePos,5} | ({mcuX,2},{mcuY,2}) | ({subX},{subY})    |       {spatialIdx,4} | ({pixelX,3},{pixelY,3})");
            }
        }
    }
}
