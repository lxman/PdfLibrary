namespace ImageLibrary.Ccitt;

/// <summary>
/// Specifies the CCITT compression group/algorithm.
/// </summary>
public enum CcittGroup
{
    /// <summary>
    /// Group 3 One-Dimensional (Modified Huffman).
    /// PDF K parameter = 0.
    /// </summary>
    Group3OneDimensional = 0,

    /// <summary>
    /// Group 3 Two-Dimensional (Modified READ).
    /// PDF K parameter &gt; 0 (typically 4).
    /// </summary>
    Group3TwoDimensional = 1,

    /// <summary>
    /// Group 4 (Modified Modified READ / MMR).
    /// PDF K parameter &lt; 0.
    /// </summary>
    Group4 = 2
}

/// <summary>
/// Options for CCITT Fax compression and decompression.
/// These correspond to the PDF CCITTFaxDecode filter parameters.
/// </summary>
public class CcittOptions
{
    /// <summary>
    /// The compression group to use.
    /// Default: Group4 (most common in PDFs).
    /// </summary>
    public CcittGroup Group { get; set; } = CcittGroup.Group4;

    /// <summary>
    /// The K parameter as used in PDF.
    /// K &lt; 0: Group 4 (pure 2D, default -1)
    /// K = 0: Group 3 1D
    /// K &gt; 0: Group 3 2D (K specifies how many 1D lines between 2D lines)
    /// </summary>
    public int K { get; set; } = -1;

    /// <summary>
    /// Width of the image in pixels.
    /// Default: 1728 (standard A4 fax width).
    /// </summary>
    public int Width { get; set; } = CcittConstants.StandardLineWidth;

    /// <summary>
    /// Height of the image in pixels (0 = unknown).
    /// </summary>
    public int Height { get; set; } = 0;

    /// <summary>
    /// True if rows are encoded with byte-alignment (padded to byte boundaries).
    /// PDF parameter: EncodedByteAlign.
    /// Default: false.
    /// </summary>
    public bool EncodedByteAlign { get; set; } = false;

    /// <summary>
    /// True if the end-of-line (EOL) bit pattern is required.
    /// PDF parameter: EndOfLine.
    /// Default: false.
    /// </summary>
    public bool EndOfLine { get; set; }

    /// <summary>
    /// True if end-of-block (EOB) pattern is expected.
    /// For Group 3: RTC (6 consecutive EOLs).
    /// For Group 4: EOFB (2 consecutive EOLs).
    /// PDF parameter: EndOfBlock.
    /// Default: true.
    /// </summary>
    public bool EndOfBlock { get; set; } = true;

    /// <summary>
    /// True if 0 means black and 1 means white (inverted).
    /// PDF parameter: BlackIs1.
    /// Default: false (0 = white, 1 = black, standard for CCITT).
    /// </summary>
    public bool BlackIs1 { get; set; }

    /// <summary>
    /// Number of damaged rows that can be tolerated.
    /// PDF parameter: DamagedRowsBeforeError.
    /// Default: 0.
    /// </summary>
    public int DamagedRowsBeforeError { get; set; } = 0;

    /// <summary>
    /// Creates options from a PDF K parameter value.
    /// </summary>
    /// <param name="k">The K parameter from PDF.</param>
    /// <param name="width">The image width.</param>
    /// <returns>Configured options.</returns>
    public static CcittOptions FromPdfK(int k, int width)
    {
        var options = new CcittOptions
        {
            K = k,
            Width = width
        };

        if (k < 0)
        {
            options.Group = CcittGroup.Group4;
        }
        else if (k == 0)
        {
            options.Group = CcittGroup.Group3OneDimensional;
        }
        else
        {
            options.Group = CcittGroup.Group3TwoDimensional;
        }

        return options;
    }

    /// <summary>
    /// Default PDF options (Group 4, width 1728).
    /// </summary>
    public static CcittOptions PdfDefault => new CcittOptions
    {
        Group = CcittGroup.Group4,
        K = -1,
        Width = CcittConstants.StandardLineWidth,
        EndOfBlock = true,
        BlackIs1 = false
    };

    /// <summary>
    /// Group 3 1D options (Modified Huffman).
    /// </summary>
    public static CcittOptions Group3_1D => new CcittOptions
    {
        Group = CcittGroup.Group3OneDimensional,
        K = 0,
        Width = CcittConstants.StandardLineWidth,
        EndOfLine = true,
        EndOfBlock = true
    };

    /// <summary>
    /// Group 3 2D options (Modified READ).
    /// </summary>
    public static CcittOptions Group3_2D => new CcittOptions
    {
        Group = CcittGroup.Group3TwoDimensional,
        K = 4,
        Width = CcittConstants.StandardLineWidth,
        EndOfLine = true,
        EndOfBlock = true
    };

    /// <summary>
    /// Group 4 options (MMR).
    /// </summary>
    public static CcittOptions Group4_MMR => new CcittOptions
    {
        Group = CcittGroup.Group4,
        K = -1,
        Width = CcittConstants.StandardLineWidth,
        EndOfBlock = true
    };

    /// <summary>
    /// TIFF-compatible Group 4 options.
    /// </summary>
    public static CcittOptions TiffGroup4 => new CcittOptions
    {
        Group = CcittGroup.Group4,
        K = -1,
        EndOfBlock = false, // TIFF doesn't use EOFB
        BlackIs1 = false
    };
}