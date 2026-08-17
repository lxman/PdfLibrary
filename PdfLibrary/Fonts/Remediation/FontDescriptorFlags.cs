using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Fonts.Remediation;

/// <summary>Minimal honest /FontDescriptor /Flags for a replacement text face (spec §3 step 6):
/// FixedPitch (bit 1) from the post table, Nonsymbolic (bit 6) always — the resolution ladder
/// only yields Latin text faces — Italic (bit 7) from head.macStyle. Everything else 0: no
/// evidence, no claim.</summary>
internal static class FontDescriptorFlags
{
    public static int Compute(EmbeddedFontMetrics metrics) =>
        (metrics.Post?.IsFixedPitch == true ? 1 : 0) | 32 | (metrics.IsItalic ? 64 : 0);
}
