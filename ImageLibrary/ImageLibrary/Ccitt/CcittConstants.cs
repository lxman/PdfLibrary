namespace ImageLibrary.Ccitt;

/// <summary>
/// Constants for CCITT Fax compression (Group 3 and Group 4).
/// </summary>
public static class CcittConstants
{
    /// <summary>
    /// End of Line marker for Group 3: 000000000001 (12 bits).
    /// </summary>
    public const int EolCode = 0x001;
    public const int EolBits = 12;

    /// <summary>
    /// End of Facsimile Block (EOFB) for Group 4: two consecutive EOL codes.
    /// </summary>
    public const int EofbBits = 24;

    /// <summary>
    /// Return to Control (RTC) for Group 3: six consecutive EOL codes.
    /// </summary>
    public const int RtcEolCount = 6;

    /// <summary>
    /// Standard fax line width (A4 at 200 dpi).
    /// </summary>
    public const int StandardLineWidth = 1728;

    /// <summary>
    /// Maximum run length that can be encoded with a single terminating code.
    /// </summary>
    public const int MaxTerminatingRunLength = 63;

    /// <summary>
    /// Makeup code increment (each makeup code represents this many pixels).
    /// </summary>
    public const int MakeupCodeIncrement = 64;

    /// <summary>
    /// Maximum run length with standard makeup codes (1728).
    /// </summary>
    public const int MaxStandardMakeupRunLength = 1728;

    /// <summary>
    /// Maximum run length with extended makeup codes (2560).
    /// </summary>
    public const int MaxExtendedMakeupRunLength = 2560;
}