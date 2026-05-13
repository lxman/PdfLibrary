using System;

namespace Jp2Codec.Color
{
    /// <summary>
    /// Inverse Reversible Component Transform (RCT) per ISO/IEC 15444-1 G.1.2.
    /// Used only with the reversible 5/3 wavelet path. Operates on the first
    /// three components in place; later components (if present) are
    /// untouched.
    ///
    /// <para>
    /// The mapping is (Y_0, Y_1, Y_2) → (I_0, I_1, I_2) where (Y_1, Y_2)
    /// are the encoder's chroma differences (B-G, R-G after a forward RCT
    /// on RGB):
    /// </para>
    /// <list type="bullet">
    /// <item><c>I_1 = Y_0 - floor((Y_1 + Y_2) / 4)</c>  (G.4)</item>
    /// <item><c>I_0 = Y_2 + I_1</c>                     (G.5)</item>
    /// <item><c>I_2 = Y_1 + I_1</c>                     (G.6)</item>
    /// </list>
    ///
    /// <para>
    /// I_1 must be computed before I_0 / I_2 since both depend on it. The
    /// in-place rewrite is therefore: temp = c0 - floor((c1+c2)/4); save it
    /// into c1 after first reading c1 / c2 to derive c0 / c2.
    /// </para>
    /// </summary>
    internal static class InverseRct
    {
        public static void Apply(int[,] component0, int[,] component1, int[,] component2)
        {
            if (component0 is null) throw new ArgumentNullException(nameof(component0));
            if (component1 is null) throw new ArgumentNullException(nameof(component1));
            if (component2 is null) throw new ArgumentNullException(nameof(component2));

            int height = component0.GetLength(0);
            int width = component0.GetLength(1);
            if (component1.GetLength(0) != height || component1.GetLength(1) != width
                || component2.GetLength(0) != height || component2.GetLength(1) != width)
            {
                throw new ArgumentException(
                    "InverseRct requires all three components to share the same dimensions.");
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    int y0 = component0[y, x];
                    int y1 = component1[y, x];
                    int y2 = component2[y, x];

                    // Spec floor division (round toward minus infinity).
                    int sum = y1 + y2;
                    int i1 = y0 - FloorDiv4(sum);
                    int i0 = y2 + i1;
                    int i2 = y1 + i1;

                    component0[y, x] = i0;
                    component1[y, x] = i1;
                    component2[y, x] = i2;
                }
            }
        }

        /// <summary>
        /// Integer floor division by 4. C# '/' truncates toward zero for
        /// signed values, so a naive (a / 4) is wrong for negative dividends
        /// (e.g. -1 / 4 = 0 in C# but floor(-1/4) = -1). The arithmetic
        /// right-shift on a two's-complement int gives the correct floor.
        /// </summary>
        private static int FloorDiv4(int value) => value >> 2;
    }
}
