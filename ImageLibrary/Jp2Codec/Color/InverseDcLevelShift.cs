using System;

namespace Jp2Codec.Color
{
    /// <summary>
    /// Inverse DC level shifting per ISO/IEC 15444-1 F.3.9. The encoder
    /// recenters unsigned components around zero before the FDWT by
    /// subtracting <c>2^(Ssiz_c - 1)</c>; the decoder undoes that step
    /// after the IDWT and any inverse multi-component transform.
    ///
    /// <para>
    /// Per the spec: the shift applies only when the MSB of Ssiz is zero
    /// (component is unsigned). Signed components pass through unchanged.
    /// The shifted-back values may exceed the original dynamic range when
    /// the path is lossy — clamping is the caller's responsibility.
    /// </para>
    /// </summary>
    internal static class InverseDcLevelShift
    {
        /// <summary>Integer in-place level shift for the reversible (5/3) path.</summary>
        public static void Apply(int[,] component, int precision, bool isSigned)
        {
            if (component is null) throw new ArgumentNullException(nameof(component));
            if (precision < 1 || precision > 38)
                throw new ArgumentOutOfRangeException(nameof(precision), precision,
                    "Component precision must lie in [1, 38] per SIZ Ssiz.");

            if (isSigned) return;

            int shift = 1 << (precision - 1);
            int height = component.GetLength(0);
            int width = component.GetLength(1);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    component[y, x] += shift;
                }
            }
        }

        /// <summary>Float in-place level shift for the irreversible (9/7) path.</summary>
        public static void Apply(float[,] component, int precision, bool isSigned)
        {
            if (component is null) throw new ArgumentNullException(nameof(component));
            if (precision < 1 || precision > 38)
                throw new ArgumentOutOfRangeException(nameof(precision), precision,
                    "Component precision must lie in [1, 38] per SIZ Ssiz.");

            if (isSigned) return;

            float shift = 1 << (precision - 1);
            int height = component.GetLength(0);
            int width = component.GetLength(1);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    component[y, x] += shift;
                }
            }
        }
    }
}
