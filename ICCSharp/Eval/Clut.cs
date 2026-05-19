using System;
using ICCSharp.Tags;

namespace ICCSharp.Eval;

/// <summary>
/// Factory that picks the appropriate <see cref="IClut"/> implementation for the input
/// dimensionality: <see cref="Clut3D"/> (tetrahedral) for 3-channel inputs, <see cref="ClutND"/>
/// (multilinear) for everything else. Matches lcms2's default dispatch.
/// </summary>
public static class Clut
{
    public static IClut FromTag(LutClutData clut)
    {
        if (clut is null) throw new ArgumentNullException(nameof(clut));
        return clut.GridPoints.Count == 3 ? new Clut3D(clut) : new ClutND(clut);
    }
}
