namespace ImageLibrary.Tiff;

/// <summary>
/// TIFF photometric interpretation values.
/// </summary>
internal enum TiffPhotometricInterpretation : ushort
{
    /// <summary>0 is imaged as white.</summary>
    WhiteIsZero = 0,

    /// <summary>0 is imaged as black.</summary>
    BlackIsZero = 1,

    /// <summary>RGB color model.</summary>
    Rgb = 2,

    /// <summary>Palette color (color map).</summary>
    Palette = 3,

    /// <summary>Transparency mask.</summary>
    TransparencyMask = 4,

    /// <summary>CMYK color model.</summary>
    Cmyk = 5,

    /// <summary>YCbCr color model.</summary>
    YCbCr = 6,

    /// <summary>CIE L*a*b* color model.</summary>
    CieLab = 8
}
