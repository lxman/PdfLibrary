using System;

namespace Jp2Codec.Wavelet
{
    /// <summary>
    /// Whole-sample (periodic) symmetric extension used by the JPEG 2000 1D
    /// IDWT (ISO/IEC 15444-1 F.3.7, "1D_EXTR procedure", Figure F.15).
    ///
    /// <para>
    /// For a signal indexed at [0..length-1], extension produces values for
    /// indices below 0 and at/above length by reflecting WITHOUT repeating
    /// the boundary samples — i.e., the pattern is
    /// <c>... C B A B C D E F G F E D C ...</c>
    /// where the data is <c>A B C D E F G</c>. Multi-bounce extension
    /// (where the index lies more than one signal-length outside the
    /// data) is handled via modular arithmetic with period
    /// <c>2·(length-1)</c>.
    /// </para>
    ///
    /// <para>
    /// Used by the inverse 5/3 and 9/7 lifting steps to fetch neighbor
    /// samples at the boundaries (per Tables F.2 / F.3, the lifting needs
    /// up to 2 samples of extension for 5/3 and up to 4 for 9/7).
    /// </para>
    /// </summary>
    internal static class SymmetricExtension
    {
        /// <summary>
        /// Returns the in-range local index that <paramref name="index"/> maps
        /// to under whole-sample symmetric extension. For
        /// <paramref name="length"/> equal to 1, the only valid sample is
        /// index 0 and any extension returns 0.
        /// </summary>
        public static int Reflect(int index, int length)
        {
            if (length < 1)
                throw new ArgumentOutOfRangeException(nameof(length), length, null);
            if (length == 1) return 0;

            int period = 2 * (length - 1);
            // Bring `index` into [0..period). C# % can return negatives; the
            // double-mod idiom normalises to a non-negative residue.
            int r = ((index % period) + period) % period;
            return r >= length ? period - r : r;
        }

        /// <summary>
        /// Fills the left padding [0..dataStart) and the right padding
        /// [dataStart+dataLength..buffer.Length) of <paramref name="buffer"/>
        /// using whole-sample symmetric reflection of the data at
        /// [dataStart..dataStart+dataLength).
        /// </summary>
        public static void Fill(int[] buffer, int dataStart, int dataLength)
        {
            ValidateFillArgs(buffer?.Length ?? 0, dataStart, dataLength);
            for (var i = 0; i < dataStart; i++)
            {
                int reflected = Reflect(i - dataStart, dataLength);
                buffer![i] = buffer[dataStart + reflected];
            }
            for (int i = dataStart + dataLength; i < buffer!.Length; i++)
            {
                int reflected = Reflect(i - dataStart, dataLength);
                buffer[i] = buffer[dataStart + reflected];
            }
        }

        /// <summary>
        /// Float overload of <see cref="Fill(int[], int, int)"/>.
        /// </summary>
        public static void Fill(float[] buffer, int dataStart, int dataLength)
        {
            ValidateFillArgs(buffer?.Length ?? 0, dataStart, dataLength);
            for (var i = 0; i < dataStart; i++)
            {
                int reflected = Reflect(i - dataStart, dataLength);
                buffer![i] = buffer[dataStart + reflected];
            }
            for (int i = dataStart + dataLength; i < buffer!.Length; i++)
            {
                int reflected = Reflect(i - dataStart, dataLength);
                buffer[i] = buffer[dataStart + reflected];
            }
        }

        private static void ValidateFillArgs(int bufferLength, int dataStart, int dataLength)
        {
            if (dataStart < 0)
                throw new ArgumentOutOfRangeException(nameof(dataStart), dataStart, null);
            if (dataLength < 1)
                throw new ArgumentOutOfRangeException(nameof(dataLength), dataLength, null);
            if (dataStart + dataLength > bufferLength)
                throw new ArgumentException(
                    $"Data window [{dataStart}..{dataStart + dataLength}) exceeds buffer length {bufferLength}.");
        }
    }
}
