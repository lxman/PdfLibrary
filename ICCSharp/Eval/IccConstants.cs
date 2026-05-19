namespace ICCSharp.Eval;

/// <summary>Spec-defined numeric constants used throughout PCS encoding.</summary>
public static class IccConstants
{
    /// <summary>
    /// ICC.1:2010 §6.3.4.1 — the maximum encodable PCS XYZ value, 1 + 32767/32768 ≈ 1.99996948.
    /// Storage formats (16-bit, 8-bit) normalize so that the maximum stored value maps to this
    /// real-world XYZ.
    /// </summary>
    public const double IccMaxXyz = 1.0 + 32767.0 / 32768.0;
}
