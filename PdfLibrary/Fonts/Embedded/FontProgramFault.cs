namespace PdfLibrary.Fonts.Embedded;

/// <summary>
/// The parse stage a font-program failure happened in. One value per <c>catch</c> block in
/// <see cref="EmbeddedFontMetrics"/>.
/// </summary>
internal enum FontProgramStage
{
    /// <summary>The sfnt table directory (<c>FontParser.SfntFont</c>) — a non-sfnt or malformed program.</summary>
    SfntDirectory,
    Head,
    MaxP,
    Hhea,
    Hmtx,
    Name,
    Cmap,
    /// <summary>The <c>OS/2</c> and Windows Metrics table (optional — CapHeight, weight class, fsType).</summary>
    Os2,
    /// <summary>The <c>post</c> table (optional — ItalicAngle for font-descriptor construction).</summary>
    Post,
    /// <summary>A bare CFF program (no sfnt wrapper), detected by the <c>01 00</c> version prefix.</summary>
    RawCff,
    /// <summary>The <c>CFF </c> table inside an sfnt wrapper (OpenType/CFF).</summary>
    CffTable,
    /// <summary>The lazily-loaded <c>loca</c>/<c>glyf</c> pair. Only ever recorded after a
    /// <see cref="EmbeddedFontMetrics.GetGlyphOutline"/> call has forced the load.</summary>
    GlyfLoca,
    /// <summary>A PostScript Type1 program parsed through the Length1/Length2/Length3 constructor.</summary>
    Type1Program
}

/// <summary>
/// A single swallowed font-program parse failure: which stage went wrong, and a short stable
/// description of what.
/// <para><c>Detail</c> is an exception type name for a fault that threw (<c>ArgumentException</c>),
/// or a short PascalCase tag for one that did not (<c>UnitsPerEmZero</c>) — a program can be
/// unusable without anything throwing. It is deliberately NOT an exception message: these records
/// are compared against a committed corpus baseline, and messages vary across .NET versions and
/// locales. The message still reaches <c>PdfLogger</c>, which is not committed to anything.</para>
/// </summary>
internal readonly record struct FontProgramFault(FontProgramStage Stage, string Detail)
{
    /// <summary>Stable one-line form for the corpus baseline, e.g. <c>CffTable:ArgumentException</c>.</summary>
    public override string ToString() => $"{Stage}:{Detail}";
}
