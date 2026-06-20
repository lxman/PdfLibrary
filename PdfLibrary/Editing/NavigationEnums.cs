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
