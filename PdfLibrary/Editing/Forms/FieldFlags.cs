namespace PdfLibrary.Editing.Forms;

/// <summary>
/// AcroForm field flag bit positions (1-based, per ISO 32000 Table 221 and Table 226/228/230).
/// Use <see cref="Has"/> to test a flag value.
/// </summary>
internal static class FieldFlags
{
    // General flags (Table 221)
    public const int ReadOnly   = 1;
    public const int Required   = 2;
    public const int NoExport   = 3;

    // Text-field flags (Table 228)
    public const int Multiline  = 13;
    public const int Password   = 14;
    public const int Comb       = 25;

    // Button flags (Table 226)
    public const int Radio      = 16;
    public const int Pushbutton = 17;

    // Choice flags (Table 230)
    public const int Combo      = 18;
    public const int Edit       = 19;
    public const int MultiSelect = 22;

    /// <summary>Returns true when the 1-based <paramref name="bit"/> is set in <paramref name="ff"/>.</summary>
    public static bool Has(int ff, int bit) => (ff & (1 << (bit - 1))) != 0;
}
