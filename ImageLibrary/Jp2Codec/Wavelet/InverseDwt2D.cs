using System;

namespace Jp2Codec.Wavelet
{
    /// <summary>
    /// 2D inverse DWT — the 2D_SR procedure from ISO/IEC 15444-1 F.3.2.
    /// Combines the four subbands (LL, HL, LH, HH) at decomposition
    /// level <c>lev</c> into the LL subband at level <c>lev-1</c>.
    ///
    /// <para>
    /// Pipeline per F.3.2:
    /// </para>
    /// <list type="number">
    /// <item><description>2D_INTERLEAVE — place the four subbands into a single parent grid.</description></item>
    /// <item><description>HOR_SR — apply the 1D inverse lifting to every row.</description></item>
    /// <item><description>VER_SR — apply the 1D inverse lifting to every column.</description></item>
    /// </list>
    ///
    /// <para>
    /// The parities <paramref name="u0Parity"/> / <paramref name="v0Parity"/>
    /// (parent canvas start-of-row / start-of-column parities) determine
    /// which subband occupies which parity of position in the parent grid.
    /// </para>
    /// </summary>
    internal static class InverseDwt2D
    {
        public static int[,] Reverse53(
            int[,] ll, int[,] hl, int[,] lh, int[,] hh,
            int u0Parity, int v0Parity)
        {
            if (ll is null) throw new ArgumentNullException(nameof(ll));
            if (hl is null) throw new ArgumentNullException(nameof(hl));
            if (lh is null) throw new ArgumentNullException(nameof(lh));
            if (hh is null) throw new ArgumentNullException(nameof(hh));
            ValidateParities(u0Parity, v0Parity);
            ValidateShapes(
                ll.GetLength(0), ll.GetLength(1),
                hl.GetLength(0), hl.GetLength(1),
                lh.GetLength(0), lh.GetLength(1),
                hh.GetLength(0), hh.GetLength(1));

            int parentHeight = ll.GetLength(0) + lh.GetLength(0);
            int parentWidth = ll.GetLength(1) + hl.GetLength(1);
            var a = new int[parentHeight, parentWidth];

            // 2D_INTERLEAVE. Each parent (row, col) belongs to LL / HL / LH / HH
            // based on the canvas parity of its row and column. Subband indices
            // are just (row/2, col/2): for any parityOffset the count of
            // same-parity locals at indices < k equals k/2 (integer div).
            for (var row = 0; row < parentHeight; row++)
            {
                int vCanvasParity = (row + v0Parity) & 1;
                int subRow = row / 2;
                for (var col = 0; col < parentWidth; col++)
                {
                    int hCanvasParity = (col + u0Parity) & 1;
                    int subCol = col / 2;

                    a[row, col] = (hCanvasParity, vCanvasParity) switch
                    {
                        (0, 0) => ll[subRow, subCol],
                        (1, 0) => hl[subRow, subCol],
                        (0, 1) => lh[subRow, subCol],
                        _ => hh[subRow, subCol],
                    };
                }
            }

            // HOR_SR: 1D inverse lifting per row, parity = u0Parity.
            var rowBuf = new int[parentWidth];
            for (var row = 0; row < parentHeight; row++)
            {
                for (var col = 0; col < parentWidth; col++) rowBuf[col] = a[row, col];
                int[] rowResult = InverseLifting53.Apply(rowBuf, u0Parity);
                for (var col = 0; col < parentWidth; col++) a[row, col] = rowResult[col];
            }

            // VER_SR: 1D inverse lifting per column, parity = v0Parity.
            var colBuf = new int[parentHeight];
            for (var col = 0; col < parentWidth; col++)
            {
                for (var row = 0; row < parentHeight; row++) colBuf[row] = a[row, col];
                int[] colResult = InverseLifting53.Apply(colBuf, v0Parity);
                for (var row = 0; row < parentHeight; row++) a[row, col] = colResult[row];
            }

            return a;
        }

        public static float[,] Reverse97(
            float[,] ll, float[,] hl, float[,] lh, float[,] hh,
            int u0Parity, int v0Parity)
        {
            if (ll is null) throw new ArgumentNullException(nameof(ll));
            if (hl is null) throw new ArgumentNullException(nameof(hl));
            if (lh is null) throw new ArgumentNullException(nameof(lh));
            if (hh is null) throw new ArgumentNullException(nameof(hh));
            ValidateParities(u0Parity, v0Parity);
            ValidateShapes(
                ll.GetLength(0), ll.GetLength(1),
                hl.GetLength(0), hl.GetLength(1),
                lh.GetLength(0), lh.GetLength(1),
                hh.GetLength(0), hh.GetLength(1));

            int parentHeight = ll.GetLength(0) + lh.GetLength(0);
            int parentWidth = ll.GetLength(1) + hl.GetLength(1);
            var a = new float[parentHeight, parentWidth];

            for (var row = 0; row < parentHeight; row++)
            {
                int vCanvasParity = (row + v0Parity) & 1;
                int subRow = row / 2;
                for (var col = 0; col < parentWidth; col++)
                {
                    int hCanvasParity = (col + u0Parity) & 1;
                    int subCol = col / 2;

                    a[row, col] = (hCanvasParity, vCanvasParity) switch
                    {
                        (0, 0) => ll[subRow, subCol],
                        (1, 0) => hl[subRow, subCol],
                        (0, 1) => lh[subRow, subCol],
                        _ => hh[subRow, subCol],
                    };
                }
            }

            var rowBuf = new float[parentWidth];
            for (var row = 0; row < parentHeight; row++)
            {
                for (var col = 0; col < parentWidth; col++) rowBuf[col] = a[row, col];
                float[] rowResult = InverseLifting97.Apply(rowBuf, u0Parity);
                for (var col = 0; col < parentWidth; col++) a[row, col] = rowResult[col];
            }

            var colBuf = new float[parentHeight];
            for (var col = 0; col < parentWidth; col++)
            {
                for (var row = 0; row < parentHeight; row++) colBuf[row] = a[row, col];
                float[] colResult = InverseLifting97.Apply(colBuf, v0Parity);
                for (var row = 0; row < parentHeight; row++) a[row, col] = colResult[row];
            }

            return a;
        }

        private static void ValidateParities(int u0Parity, int v0Parity)
        {
            if (u0Parity != 0 && u0Parity != 1)
                throw new ArgumentOutOfRangeException(nameof(u0Parity), u0Parity, "Must be 0 or 1.");
            if (v0Parity != 0 && v0Parity != 1)
                throw new ArgumentOutOfRangeException(nameof(v0Parity), v0Parity, "Must be 0 or 1.");
        }

        private static void ValidateShapes(
            int llH, int llW, int hlH, int hlW, int lhH, int lhW, int hhH, int hhW)
        {
            // LL and HL share row count (vertical low-pass rows).
            // LL and LH share column count (horizontal low-pass cols).
            // HH = (LH rows) × (HL cols).
            if (hlH != llH)
                throw new ArgumentException($"HL row count {hlH} ≠ LL row count {llH}.");
            if (lhW != llW)
                throw new ArgumentException($"LH column count {lhW} ≠ LL column count {llW}.");
            if (hhH != lhH || hhW != hlW)
                throw new ArgumentException(
                    $"HH dimensions ({hhH}×{hhW}) must equal LH-rows × HL-cols ({lhH}×{hlW}).");
        }
    }
}
