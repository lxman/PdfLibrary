using System.Collections.Generic;

namespace ICCSharp.Tags;

/// <summary>
/// Color-lookup-table block as carried inside lutAToBType / lutBToAType (ICC.1:2010 §10.8.4 / §10.9.4).
/// Values are normalized into [0, 1]: 8-bit storage divides by 255, 16-bit storage divides by 65535.
/// </summary>
public sealed class LutClutData
{
    /// <summary>Number of grid points in each input dimension; length == input-channel count.</summary>
    public IReadOnlyList<byte> GridPoints { get; }

    /// <summary>1 = 8-bit values stored, 2 = 16-bit values stored.</summary>
    public int Precision { get; }

    /// <summary>
    /// Output values in raster order: first input dimension varies slowest; for each grid point the
    /// <c>OutputChannels</c> output values are stored consecutively. Normalized to [0, 1].
    /// </summary>
    public IReadOnlyList<double> Values { get; }

    public int OutputChannels { get; }

    public LutClutData(IReadOnlyList<byte> gridPoints, int precision, IReadOnlyList<double> values, int outputChannels)
    {
        GridPoints = gridPoints;
        Precision = precision;
        Values = values;
        OutputChannels = outputChannels;
    }
}
