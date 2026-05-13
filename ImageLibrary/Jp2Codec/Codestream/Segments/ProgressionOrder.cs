namespace Jp2Codec.Codestream.Segments
{
    /// <summary>
    /// Progression order constants per ISO/IEC 15444-1 Table A-16.
    /// The order in which packets appear inside a tile-part body is determined
    /// by these axes (l = layer, r = resolution, c = component, p = position).
    /// </summary>
    internal enum ProgressionOrder : byte
    {
        /// <summary>Layer-resolution-component-position (most common; quality progressive).</summary>
        Lrcp = 0,
        /// <summary>Resolution-layer-component-position (resolution progressive).</summary>
        Rlcp = 1,
        /// <summary>Resolution-position-component-layer.</summary>
        Rpcl = 2,
        /// <summary>Position-component-resolution-layer (spatial progressive).</summary>
        Pcrl = 3,
        /// <summary>Component-position-resolution-layer.</summary>
        Cprl = 4,
    }

    /// <summary>
    /// Wavelet transformation kernel per ISO/IEC 15444-1 Table A-20.
    /// </summary>
    internal enum WaveletTransform : byte
    {
        /// <summary>9/7 floating-point lifting (lossy, Annex F.4.8.2).</summary>
        Irreversible9x7 = 0,
        /// <summary>5/3 integer lifting (lossless, Annex F.4.8.1).</summary>
        Reversible5x3 = 1,
    }
}
