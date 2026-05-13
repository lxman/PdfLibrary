using System;
using System.Collections.Generic;

namespace Jp2Codec
{
    /// <summary>
    /// Decoded JPEG 2000 image. Samples are returned per component in raster
    /// order (row-major) to match the spec's component grid (ISO/IEC 15444-1
    /// §A.5.1) — components may have different dimensions when subsampling
    /// factors XRsiz/YRsiz differ. The consumer is responsible for upsampling
    /// chroma planes if a uniform RGB raster is required (use
    /// <c>Jp2Codec.Color.ChromaUpsampler</c> /
    /// <c>Jp2Codec.Color.SrgbRenderer</c>).
    /// <para>
    /// The per-component sample arrays are intended to be read-only from the
    /// caller's perspective. They are exposed as <see cref="IReadOnlyList{T}"/>
    /// so the outer list cannot be replaced; the inner buffers are still
    /// physically mutable (a .NET array reference) but callers must not write
    /// to them — future versions may switch to a different backing store.
    /// </para>
    /// </summary>
    public sealed class Jp2DecodeResult
    {
        // Backing storage. Kept as raw arrays for internal hot paths
        // (SrgbRenderer, ChromaUpsampler, ImageAssembler) and exposed
        // publicly through IReadOnlyList views.
        private readonly int[][] _componentData;
        private readonly int[] _componentWidth;
        private readonly int[] _componentHeight;
        private readonly int[] _componentPrecision;
        private readonly bool[] _componentSigned;
        private readonly int[]? _associationToComponent;

        /// <summary>
        /// Reference grid width (Xsiz - XOsiz). For images without subsampling,
        /// every component raster has this width.
        /// </summary>
        public int Width { get; }

        /// <summary>Reference grid height (Ysiz - YOsiz).</summary>
        public int Height { get; }

        /// <summary>Number of components (Csiz, 1..16 384).</summary>
        public int NumberOfComponents => _componentData.Length;

        /// <summary>
        /// Per-component sample arrays. <c>ComponentData[c][y * ComponentWidth[c] + x]</c>
        /// gives the sample at component-grid position (x, y). DC level shift
        /// (signed/unsigned conversion) is already applied per Annex G.1.2.
        /// Treat the returned arrays as read-only.
        /// </summary>
        public IReadOnlyList<int[]> ComponentData { get; }

        /// <summary>Width in samples of each component (after subsampling).</summary>
        public IReadOnlyList<int> ComponentWidth { get; }

        /// <summary>Height in samples of each component (after subsampling).</summary>
        public IReadOnlyList<int> ComponentHeight { get; }

        /// <summary>Bits per sample for each component (Ssiz + 1, 1..38).</summary>
        public IReadOnlyList<int> ComponentPrecision { get; }

        /// <summary>Whether each component's samples are signed (bit 7 of Ssiz).</summary>
        public IReadOnlyList<bool> ComponentSigned { get; }

        /// <summary>Color-space hint from the JP2 wrapper. Unspecified for raw J2K.</summary>
        public Jp2ColorSpace ColorSpace { get; }

        /// <summary>
        /// Raw ICC profile bytes from the JP2 wrapper's colr box when METH=2 or 3
        /// (<see cref="Jp2ColorSpace.RestrictedIcc"/> /
        /// <see cref="Jp2ColorSpace.AnyIcc"/>); null otherwise. Hand directly to
        /// <c>Wacton.Unicolour.IccConfiguration(byte[], ...)</c> for ICC-aware
        /// rendering, or ignore for "raw" applications.
        /// </summary>
        public byte[]? IccProfile { get; }

        /// <summary>
        /// Optional channel-to-association lookup driven by the JP2 cdef box
        /// (ISO/IEC 15444-1 I.5.3.6). Returns the codestream component index
        /// that carries the supplied colour-channel association, or <c>-1</c>
        /// if the cdef box was absent or no entry matches.
        /// <para>
        /// Asoc values: <c>1, 2, 3</c> are sRGB R/G/B or sYCC Y/Cb/Cr; <c>1</c>
        /// for greyscale. When no cdef box is present, callers should assume the
        /// default order (component i is the i-th channel of the declared
        /// colour space).
        /// </para>
        /// </summary>
        public int GetComponentForAssociation(int association)
        {
            if (_associationToComponent is null) return -1;
            for (var i = 0; i < _associationToComponent.Length; i += 2)
            {
                if (_associationToComponent[i] == association)
                    return _associationToComponent[i + 1];
            }
            return -1;
        }

        /// <summary>
        /// Read-only view of component <paramref name="index"/>'s sample buffer.
        /// Preferred over <see cref="ComponentData"/> for new code that only
        /// needs to read samples.
        /// </summary>
        public ReadOnlySpan<int> GetComponentSpan(int index) => _componentData[index];

        /// <summary>Convenience accessor — returns the underlying sample array for component <paramref name="index"/>.</summary>
        public int[] GetComponent(int index) => _componentData[index];

        // Internal fast-path accessors: hand back the raw arrays so hot loops
        // inside Jp2Codec (SrgbRenderer, ChromaUpsampler) don't pay for the
        // IReadOnlyList indexer indirection.
        internal int[][] ComponentArrays => _componentData;
        internal int[] ComponentWidthArray => _componentWidth;
        internal int[] ComponentHeightArray => _componentHeight;
        internal int[] ComponentPrecisionArray => _componentPrecision;

        public Jp2DecodeResult(
            int width,
            int height,
            int[][] componentData,
            int[] componentWidth,
            int[] componentHeight,
            int[] componentPrecision,
            bool[] componentSigned,
            Jp2ColorSpace colorSpace,
            byte[]? iccProfile = null,
            int[]? associationToComponent = null)
        {
            if (componentData is null) throw new ArgumentNullException(nameof(componentData));
            if (componentWidth is null) throw new ArgumentNullException(nameof(componentWidth));
            if (componentHeight is null) throw new ArgumentNullException(nameof(componentHeight));
            if (componentPrecision is null) throw new ArgumentNullException(nameof(componentPrecision));
            if (componentSigned is null) throw new ArgumentNullException(nameof(componentSigned));

            int n = componentData.Length;
            if (componentWidth.Length != n || componentHeight.Length != n
                || componentPrecision.Length != n || componentSigned.Length != n)
            {
                throw new ArgumentException(
                    "Per-component arrays must all have the same length as componentData.");
            }

            Width = width;
            Height = height;
            _componentData = componentData;
            _componentWidth = componentWidth;
            _componentHeight = componentHeight;
            _componentPrecision = componentPrecision;
            _componentSigned = componentSigned;
            ComponentData = componentData;
            ComponentWidth = componentWidth;
            ComponentHeight = componentHeight;
            ComponentPrecision = componentPrecision;
            ComponentSigned = componentSigned;
            ColorSpace = colorSpace;
            IccProfile = iccProfile;
            _associationToComponent = associationToComponent;
        }
    }
}
