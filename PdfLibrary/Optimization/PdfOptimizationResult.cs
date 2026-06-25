using System.IO;
using PdfLibrary.Structure;

namespace PdfLibrary.Optimization;

/// <summary>
/// Statistics describing what a <see cref="PdfOptimizer.Optimize(PdfDocument, Stream, PdfOptimizationOptions)"/>
/// pass did. Per-pass counts reflect only the passes enabled in <see cref="PdfOptimizationOptions"/>.
/// </summary>
public sealed class PdfOptimizationResult
{
    /// <summary>In-use objects present before optimization (after lazy materialization).</summary>
    public int ObjectsBefore { get; init; }

    /// <summary>
    /// Objects written after the unreachable-object collection pass. Equal to
    /// <see cref="ObjectsBefore"/> when <see cref="PdfOptimizationOptions.RemoveUnusedObjects"/> is off.
    /// </summary>
    public int ObjectsAfter { get; init; }

    /// <summary>Objects dropped by the garbage-collection pass (<see cref="ObjectsBefore"/> − <see cref="ObjectsAfter"/>).</summary>
    public int ObjectsRemoved => ObjectsBefore - ObjectsAfter;

    /// <summary>Total bytes written to the output (0 if the output stream is not seekable).</summary>
    public long OutputBytes { get; init; }

    /// <summary>Streams Flate-compressed by the lossless compression pass.</summary>
    public int StreamsCompressed { get; init; }

    /// <summary>Image XObjects re-compressed (0 unless <see cref="PdfOptimizationOptions.RecompressImages"/> is on).</summary>
    public int ImagesRecompressed { get; init; }

    /// <summary>Embedded font programs subsetted (0 unless <see cref="PdfOptimizationOptions.SubsetFonts"/> is on).</summary>
    public int FontsSubsetted { get; init; }
}
