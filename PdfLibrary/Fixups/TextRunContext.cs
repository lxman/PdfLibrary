using PdfLibrary.Content;

namespace PdfLibrary.Fixups;

/// <summary>
/// Context information for text run fixups.
/// Contains all information about a text operation that fixups can modify.
/// </summary>
public class TextRunContext
{
    /// <summary>
    /// The text being rendered.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// The X position where text will be rendered.
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// The Y position where text will be rendered.
    /// </summary>
    public float Y { get; set; }

    /// <summary>
    /// The font size.
    /// </summary>
    public float FontSize { get; set; }

    /// <summary>
    /// The font name (e.g., "Helvetica", "Arial").
    /// </summary>
    public string FontName { get; set; }

    /// <summary>
    /// Whether this is using a fallback font (i.e., not the original embedded font).
    /// </summary>
    public bool IsFallbackFont { get; set; }

    /// <summary>
    /// The width of the text as specified in the PDF (before any fixups).
    /// </summary>
    public float IntendedWidth { get; set; }

    /// <summary>
    /// The actual width of the text with the current font.
    /// </summary>
    public float ActualWidth { get; set; }

    /// <summary>
    /// The current graphics state.
    /// </summary>
    public PdfGraphicsState GraphicsState { get; }

    /// <summary>
    /// Custom data that can be used by fixups to store state across multiple text runs.
    /// </summary>
    public Dictionary<string, object> CustomData { get; } = new();

    /// <summary>
    /// Whether this text run should be skipped (not rendered).
    /// Fixups can set this to true to suppress rendering.
    /// </summary>
    public bool ShouldSkip { get; set; }

    /// <summary>
    /// Creates a new text run context.
    /// </summary>
    public TextRunContext(
        string text,
        float x,
        float y,
        float fontSize,
        string fontName,
        bool isFallbackFont,
        float intendedWidth,
        float actualWidth,
        PdfGraphicsState graphicsState)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        X = x;
        Y = y;
        FontSize = fontSize;
        FontName = fontName ?? throw new ArgumentNullException(nameof(fontName));
        IsFallbackFont = isFallbackFont;
        IntendedWidth = intendedWidth;
        ActualWidth = actualWidth;
        GraphicsState = graphicsState ?? throw new ArgumentNullException(nameof(graphicsState));
    }
}
