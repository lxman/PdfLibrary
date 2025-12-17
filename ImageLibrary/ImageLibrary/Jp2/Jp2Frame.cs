namespace ImageLibrary.Jp2;

/// <summary>
/// Represents the main JPEG2000 frame parameters extracted from the SIZ marker.
/// This is the entry point for all decoder stages.
/// </summary>
public class Jp2Frame
{
    /// <summary>Reference grid width (Xsiz).</summary>
    public int Width { get; set; }

    /// <summary>Reference grid height (Ysiz).</summary>
    public int Height { get; set; }

    /// <summary>Horizontal offset from grid origin to left edge of image (XOsiz).</summary>
    public int XOffset { get; set; }

    /// <summary>Vertical offset from grid origin to top edge of image (YOsiz).</summary>
    public int YOffset { get; set; }

    /// <summary>Tile width (XTsiz).</summary>
    public int TileWidth { get; set; }

    /// <summary>Tile height (YTsiz).</summary>
    public int TileHeight { get; set; }

    /// <summary>Horizontal offset of first tile (XTOsiz).</summary>
    public int TileXOffset { get; set; }

    /// <summary>Vertical offset of first tile (YTOsiz).</summary>
    public int TileYOffset { get; set; }

    /// <summary>Number of components (Csiz).</summary>
    public int ComponentCount { get; set; }

    /// <summary>Component parameters.</summary>
    public Jp2Component[] Components { get; set; } = [];

    /// <summary>Number of tiles in horizontal direction.</summary>
    public int NumTilesX => (Width - TileXOffset + TileWidth - 1) / TileWidth;

    /// <summary>Number of tiles in vertical direction.</summary>
    public int NumTilesY => (Height - TileYOffset + TileHeight - 1) / TileHeight;

    /// <summary>Total number of tiles.</summary>
    public int TileCount => NumTilesX * NumTilesY;
}

/// <summary>
/// Component parameters from SIZ marker.
/// </summary>
public class Jp2Component
{
    /// <summary>Bit depth (Ssiz &amp; 0x7F), 0-based (actual bits = BitDepth + 1).</summary>
    public int BitDepth { get; set; }

    /// <summary>Whether the component is signed (Ssiz &amp; 0x80).</summary>
    public bool IsSigned { get; set; }

    /// <summary>Horizontal subsampling factor (XRsiz).</summary>
    public int XSubsampling { get; set; }

    /// <summary>Vertical subsampling factor (YRsiz).</summary>
    public int YSubsampling { get; set; }

    /// <summary>Actual bit depth (BitDepth + 1).</summary>
    public int Precision => BitDepth + 1;
}
