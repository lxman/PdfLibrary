namespace PdfLibrary.Editing;

/// <summary>PDF /PageMode values (ISO 32000 §12.2 Table 28).</summary>
public enum PdfPageMode
{
    UseNone,
    UseOutlines,
    UseThumbs,
    FullScreen,
    UseOC,
    UseAttachments
}

/// <summary>PDF /PageLayout values (ISO 32000 §12.2 Table 28).</summary>
public enum PdfPageLayout
{
    SinglePage,
    OneColumn,
    TwoColumnLeft,
    TwoColumnRight,
    TwoPageLeft,
    TwoPageRight
}

/// <summary>PDF /ViewerPreferences /Direction values (ISO 32000 §12.2 Table 28).</summary>
public enum PdfReadingDirection
{
    /// <summary>Left to right (<c>L2R</c>).</summary>
    LeftToRight,
    /// <summary>Right to left (<c>R2L</c>).</summary>
    RightToLeft
}

/// <summary>PDF /ViewerPreferences /PrintScaling values (ISO 32000 §12.2 Table 28).</summary>
public enum PdfPrintScaling
{
    /// <summary>The reader's default print scaling (<c>AppDefault</c>).</summary>
    AppDefault,
    /// <summary>No scaling — print at actual size (<c>None</c>).</summary>
    None
}

/// <summary>PDF /ViewerPreferences /Duplex values (ISO 32000 §12.2 Table 28).</summary>
public enum PdfDuplex
{
    /// <summary>Single-sided printing.</summary>
    Simplex,
    /// <summary>Duplex, flipping on the short edge.</summary>
    DuplexFlipShortEdge,
    /// <summary>Duplex, flipping on the long edge.</summary>
    DuplexFlipLongEdge
}
