namespace PdfLibrary.Fonts.Embedded.Tables
{
    /// <summary>
    /// Interface for cmap subtables (different format implementations)
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public interface ICmapSubtable
    {
        int Language { get; }

        ushort GetGlyphId(ushort codePoint);
    }
}
