using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Content.Operators;

/// <summary>
/// BI...ID...EI - Inline image
/// </summary>
public class InlineImageOperator(PdfDictionary parameters, byte[] imageData) : PdfOperator("BI", [])
{
    /// <summary>
    /// Image parameters (dictionary between BI and ID)
    /// </summary>
    public PdfDictionary Parameters { get; } = parameters;

    /// <summary>
    /// Raw image data (between ID and EI)
    /// </summary>
    public byte[] ImageData { get; } = imageData;

    /// <summary>
    /// Image width (W or Width)
    /// </summary>
    public int Width => GetIntParameter("W", "Width");

    /// <summary>
    /// Image height (H or Height)
    /// </summary>
    public int Height => GetIntParameter("H", "Height");

    /// <summary>
    /// Bits per component (BPC or BitsPerComponent)
    /// </summary>
    public int BitsPerComponent => GetIntParameter("BPC", "BitsPerComponent", 8);

    /// <summary>
    /// Color space (CS or ColorSpace)
    /// </summary>
    public string ColorSpace => GetNameParameter("CS", "ColorSpace", "DeviceGray");

    /// <summary>
    /// Decode array (D or Decode)
    /// </summary>
    public PdfArray? Decode => GetArrayParameter("D", "Decode");

    /// <summary>
    /// Filter (F or Filter)
    /// </summary>
    public string? Filter => GetNameParameter("F", "Filter", null);

    /// <summary>
    /// DecodeParms (DP or DecodeParms)
    /// </summary>
    public PdfObject? DecodeParms => GetParameter("DP", "DecodeParms");

    /// <summary>
    /// ImageMask (IM or ImageMask)
    /// </summary>
    public bool ImageMask => GetBoolParameter("IM", "ImageMask", false);

    /// <summary>
    /// Interpolate (I or Interpolate)
    /// </summary>
    public bool Interpolate => GetBoolParameter("I", "Interpolate", false);

    private int GetIntParameter(string shortName, string longName, int defaultValue = 0)
    {
        if (Parameters.TryGetValue(new PdfName(shortName), out PdfObject val) || Parameters.TryGetValue(new PdfName(longName), out val))
        {
            return val switch
            {
                PdfInteger i => i.Value,
                PdfReal r => (int)r.Value,
                _ => defaultValue
            };
        }
        return defaultValue;
    }

    private string? GetNameParameter(string shortName, string longName, string? defaultValue)
    {
        if (Parameters.TryGetValue(new PdfName(shortName), out PdfObject val) || Parameters.TryGetValue(new PdfName(longName), out val))
        {
            if (val is PdfName name)
            {
                // Handle abbreviated color space names
                return name.Value switch
                {
                    "G" => "DeviceGray",
                    "RGB" => "DeviceRGB",
                    "CMYK" => "DeviceCMYK",
                    "I" => "Indexed",
                    _ => name.Value
                };
            }
            return val.ToString();
        }
        return defaultValue;
    }

    private bool GetBoolParameter(string shortName, string longName, bool defaultValue)
    {
        if (Parameters.TryGetValue(new PdfName(shortName), out PdfObject val) || Parameters.TryGetValue(new PdfName(longName), out val))
        {
            return val switch
            {
                PdfBoolean b => b.Value,
                _ => defaultValue
            };
        }
        return defaultValue;
    }

    private PdfArray? GetArrayParameter(string shortName, string longName)
    {
        if (Parameters.TryGetValue(new PdfName(shortName), out PdfObject val) || Parameters.TryGetValue(new PdfName(longName), out val))
        {
            return val as PdfArray;
        }
        return null;
    }

    private PdfObject? GetParameter(string shortName, string longName)
    {
        if (Parameters.TryGetValue(new PdfName(shortName), out PdfObject val) || Parameters.TryGetValue(new PdfName(longName), out val))
        {
            return val;
        }
        return null;
    }

    public override OperatorCategory Category => OperatorCategory.InlineImage;
}
