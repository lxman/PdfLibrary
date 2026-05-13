namespace Jp2Codec.Tier1
{
    /// <summary>
    /// Subband orientation. Decides which Annex D context table the
    /// zero-coding pass uses (LL and LH share Table D-1; HL uses D-2;
    /// HH uses D-3) and feeds into sign-coding-context construction.
    /// Numeric values match OpenJPEG's convention.
    /// </summary>
    internal enum SubbandOrientation
    {
        LL = 0,
        HL = 1,
        LH = 2,
        HH = 3,
    }
}
