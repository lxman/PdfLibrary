using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Content;

/// <summary>
/// Represents a soft mask for transparency operations (ISO 32000-1:2008 section 11.6.5.2)
/// Soft masks define how the alpha channel of painted content is modified.
/// </summary>
public record PdfSoftMask
{
    /// <summary>
    /// The subtype of the soft mask: "Alpha" or "Luminosity"
    /// - Alpha: uses the alpha channel of the group
    /// - Luminosity: uses the luminosity of the group as alpha
    /// </summary>
    public string Subtype { get; init; } = "Alpha";

    /// <summary>
    /// The transparency group XObject (Form XObject with /Group dictionary)
    /// This is the mask content that will be rendered to create the mask
    /// </summary>
    internal PdfStream? Group { get; init; }

    /// <summary>
    /// Backdrop color for the transparency group (BC entry)
    /// Used when compositing the group onto the backdrop
    /// </summary>
    public double[]? BackdropColor { get; init; }

    /// <summary>
    /// Transfer function (TR entry) - maps luminosity/alpha values
    /// Can be a function dictionary or /Identity
    /// </summary>
    internal PdfObject? TransferFunction { get; init; }
}
