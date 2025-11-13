using System.Numerics;
using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;

namespace PdfLibrary.Content;

/// <summary>
/// Extracts text content from PDF pages with position information
/// </summary>
public class PdfTextExtractor : PdfContentProcessor
{
    private readonly StringBuilder _textBuilder = new();
    private readonly List<TextFragment> _fragments = [];
    private readonly PdfResources? _resources;
    private bool _inTextObject;
    private Vector2 _lastPosition;
    private const double SpaceThreshold = 1.0; // Threshold for detecting word spacing

    /// <summary>
    /// Creates a text extractor with optional resources for font resolution
    /// </summary>
    public PdfTextExtractor(PdfResources? resources = null)
    {
        _resources = resources;
    }

    /// <summary>
    /// Gets the extracted text as a single string
    /// </summary>
    public string GetText() => _textBuilder.ToString();

    /// <summary>
    /// Gets the extracted text fragments with position information
    /// </summary>
    public List<TextFragment> GetTextFragments() => [.._fragments];

    /// <summary>
    /// Extracts text from a content stream
    /// </summary>
    public static string ExtractText(byte[] contentData, PdfResources? resources = null)
    {
        List<PdfOperator> operators = PdfContentParser.Parse(contentData);
        var extractor = new PdfTextExtractor(resources);
        extractor.ProcessOperators(operators);
        return extractor.GetText();
    }

    /// <summary>
    /// Extracts text with fragments from a content stream
    /// </summary>
    public static (string Text, List<TextFragment> Fragments) ExtractTextWithFragments(byte[] contentData, PdfResources? resources = null)
    {
        List<PdfOperator> operators = PdfContentParser.Parse(contentData);
        var extractor = new PdfTextExtractor(resources);
        extractor.ProcessOperators(operators);
        return (extractor.GetText(), extractor.GetTextFragments());
    }

    protected override void OnBeginText()
    {
        _inTextObject = true;
        _lastPosition = CurrentState.GetTextPosition();
    }

    protected override void OnEndText()
    {
        _inTextObject = false;
    }

    protected override void OnTextPositionChanged()
    {
        if (!_inTextObject) return;

        Vector2 currentPosition = CurrentState.GetTextPosition();
        float distance = Vector2.Distance(_lastPosition, currentPosition);

        // If significant movement, add space or newline
        if (distance > SpaceThreshold * CurrentState.FontSize)
        {
            // Vertical movement suggests new line
            if (Math.Abs(currentPosition.Y - _lastPosition.Y) > CurrentState.FontSize * 0.5)
            {
                _textBuilder.AppendLine();
            }
            // Horizontal movement suggests space
            else if (Math.Abs(currentPosition.X - _lastPosition.X) > SpaceThreshold * CurrentState.FontSize)
            {
                _textBuilder.Append(' ');
            }
        }

        _lastPosition = currentPosition;
    }

    protected override void OnShowText(PdfString text)
    {
        if (!_inTextObject) return;

        Vector2 position = CurrentState.GetTextPosition();

        // Get current font object
        PdfFont? font = null;
        if (_resources != null && !string.IsNullOrEmpty(CurrentState.FontName))
        {
            font = _resources.GetFontObject(CurrentState.FontName);
        }

        // Decode text using font
        string decodedText = DecodeText(text, font);

        _textBuilder.Append(decodedText);
        _fragments.Add(new TextFragment
        {
            Text = decodedText,
            X = position.X,
            Y = position.Y,
            FontName = CurrentState.FontName,
            FontSize = CurrentState.FontSize
        });

        // Calculate actual text advance using font metrics
        double advance = CalculateTextWidth(text.Bytes, font, CurrentState.FontSize);
        _lastPosition = position with { X = position.X + (float)advance };
    }

    protected override void OnShowTextWithPositioning(PdfArray array)
    {
        if (!_inTextObject) return;

        foreach (PdfObject item in array)
        {
            switch (item)
            {
                case PdfString str:
                    OnShowText(str);
                    break;
                case PdfInteger intVal:
                {
                    // Negative values increase spacing (move text position)
                    // Values are in thousandths of a unit of text space
                    double adjustment = -intVal.Value / 1000.0 * CurrentState.FontSize;
                    Vector2 position = CurrentState.GetTextPosition();
                    _lastPosition = position with { X = position.X + (float)adjustment };
                    break;
                }
                case PdfReal realVal:
                {
                    double adjustment = -realVal.Value / 1000.0 * CurrentState.FontSize;
                    Vector2 position = CurrentState.GetTextPosition();
                    _lastPosition = position with { X = position.X + (float)adjustment };
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Decodes PDF string to readable text using font information
    /// </summary>
    private static string DecodeText(PdfString pdfString, PdfFont? font)
    {
        byte[] bytes = pdfString.Bytes;

        // Check for UTF-16BE BOM (used in some PDFs)
        if (bytes is [0xFE, 0xFF, ..])
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        // Use font for decoding if available
        if (font != null)
        {
            var sb = new StringBuilder();
            foreach (byte b in bytes)
            {
                string decoded = font.DecodeCharacter(b);
                sb.Append(decoded);
            }
            return sb.ToString();
        }

        // Fall back to Latin-1/PDFDocEncoding (similar to Windows-1252)
        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>
    /// Calculates the width of text in user space units
    /// </summary>
    private static double CalculateTextWidth(byte[] bytes, PdfFont? font, double fontSize)
    {
        if (font == null)
        {
            // Fall back to rough estimate
            return bytes.Length * fontSize * 0.5;
        }

        double totalWidth = 0;
        foreach (byte b in bytes)
        {
            // Get character width in glyph space (typically 1000 units)
            double glyphWidth = font.GetCharacterWidth(b);

            // Convert to text space: width * fontSize / 1000
            totalWidth += glyphWidth * fontSize / 1000.0;
        }

        return totalWidth;
    }
}

/// <summary>
/// Represents a text fragment with position and formatting information
/// </summary>
public class TextFragment
{
    public string Text { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public string? FontName { get; init; }
    public double FontSize { get; init; }

    public override string ToString() => $"{Text} at ({X:F2}, {Y:F2}) {FontName} {FontSize}pt";
}
