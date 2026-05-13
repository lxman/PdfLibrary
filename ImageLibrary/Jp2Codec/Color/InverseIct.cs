using System;

namespace Jp2Codec.Color
{
    /// <summary>
    /// Inverse Irreversible Component Transform (ICT) per ISO/IEC 15444-1
    /// G.2.2. Used only with the irreversible 9/7 wavelet path. Operates on
    /// the first three components in place; later components (if present)
    /// are untouched.
    ///
    /// <para>
    /// The transform is the standard ITU-R BT.601 YCbCr → R'G'B' matrix:
    /// </para>
    /// <list type="bullet">
    /// <item><c>I_0 = Y_0 + 1.402 · Y_2</c>                          (G.10)</item>
    /// <item><c>I_1 = Y_0 - 0.34413 · Y_1 - 0.71414 · Y_2</c>        (G.11)</item>
    /// <item><c>I_2 = Y_0 + 1.772 · Y_1</c>                          (G.12)</item>
    /// </list>
    /// where Y_0 = luma, Y_1 = Cb, Y_2 = Cr (chroma components are
    /// centered around zero by the encoder's level-shift / forward ICT).
    /// </summary>
    internal static class InverseIct
    {
        // Constants pulled directly from G.10..G.12. They round-trip the
        // forward ICT to within float precision; matching them exactly to
        // the spec keeps decoded output bit-comparable with reference
        // implementations.
        private const float CrToR = 1.402f;
        private const float CbToG = -0.34413f;
        private const float CrToG = -0.71414f;
        private const float CbToB = 1.772f;

        public static void Apply(float[,] component0, float[,] component1, float[,] component2)
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
                    "InverseIct requires all three components to share the same dimensions.");
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    float y0 = component0[y, x];
                    float y1 = component1[y, x];
                    float y2 = component2[y, x];

                    float i0 = y0 + CrToR * y2;
                    float i1 = y0 + CbToG * y1 + CrToG * y2;
                    float i2 = y0 + CbToB * y1;

                    component0[y, x] = i0;
                    component1[y, x] = i1;
                    component2[y, x] = i2;
                }
            }
        }
    }
}
