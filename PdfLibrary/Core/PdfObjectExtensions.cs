using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Core;

/// <summary>
/// Extension methods for PdfObject to simplify common operations
/// </summary>
internal static class PdfObjectExtensions
{
    /// <summary>
    /// Converts a PdfObject to a double value.
    /// Returns 0 if the object is not a numeric type.
    /// </summary>
    public static double ToDouble(this PdfObject? obj)
    {
        return obj switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => 0
        };
    }

    /// <summary>
    /// Converts a PdfObject to a nullable double value.
    /// Returns null if the object is not a numeric type.
    /// </summary>
    public static double? ToDoubleOrNull(this PdfObject? obj)
    {
        return obj switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => null
        };
    }

    /// <summary>
    /// Converts a PdfObject to an integer value.
    /// Returns 0 if the object is not a numeric type.
    /// </summary>
    public static int ToInt(this PdfObject? obj)
    {
        return obj switch
        {
            PdfInteger i => i.Value,
            PdfReal r => (int)r.Value,
            _ => 0
        };
    }

    /// <summary>
    /// Converts a PdfObject to a nullable integer value.
    /// Returns null if the object is not a numeric type.
    /// </summary>
    public static int? ToIntOrNull(this PdfObject? obj)
    {
        return obj switch
        {
            PdfInteger i => i.Value,
            PdfReal r => (int)r.Value,
            _ => null
        };
    }
}
