using System.Numerics;
using System.Text;
using Logging;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;

namespace PdfLibrary.Content;

/// <summary>
/// Extracts text content from PDF pages with position information
/// </summary>
internal class PdfTextExtractor : PdfContentProcessor
{
    private readonly StringBuilder _textBuilder = new();
    private readonly List<TextFragment> _fragments = [];
    private readonly PdfResources? _resources;
    private readonly PdfDocument? _document;
    private bool _inTextObject;
    private Vector2 _lastPosition;

    // True pen position: starts at the text-matrix position when a positioning operator fires,
    // then advances with each shown run's width and each TJ kerning adjustment. The text matrix
    // itself does NOT advance on show-text in this processor, so GetTextPosition() alone is stale
    // for every run after the first — the cursor is the fragment-start source of truth.
    private Vector2 _cursor;
    private bool _cursorValid;
    private const double SpaceThreshold = 0.2; // Threshold for detecting word spacing (as fraction of font size)

    /// <summary>
    /// Creates a text extractor with optional resources for font resolution
    /// </summary>
    internal PdfTextExtractor(PdfResources? resources = null, PdfDocument? document = null)
    {
        _resources = resources;
        _document = document;
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
    /// Extracts text from a content stream. The <paramref name="document"/> must be threaded through
    /// so nested Form XObjects can be decrypted and their fonts (indirect ToUnicode/encoding/descriptor
    /// streams) resolved — without it, encrypted form content is dropped and form fonts fail to resolve.
    /// </summary>
    internal static string ExtractText(byte[] contentData, PdfResources? resources = null, PdfDocument? document = null)
    {
        List<PdfOperator> operators = PdfContentParser.Parse(contentData);
        var extractor = new PdfTextExtractor(resources, document);
        extractor.ProcessOperators(operators);
        return extractor.GetText();
    }

    /// <summary>
    /// Extracts text with fragments from a content stream. See <see cref="ExtractText"/> for why
    /// <paramref name="document"/> must be threaded through.
    /// </summary>
    internal static (string Text, List<TextFragment> Fragments) ExtractTextWithFragments(byte[] contentData, PdfResources? resources = null, PdfDocument? document = null)
    {
        List<PdfOperator> operators = PdfContentParser.Parse(contentData);
        var extractor = new PdfTextExtractor(resources, document);
        extractor.ProcessOperators(operators);
        return (extractor.GetText(), extractor.GetTextFragments());
    }

    protected override void OnBeginText()
    {
        _inTextObject = true;
        // Don't reset _lastPosition here - we want to track position across text blocks
        // Only initialize it if this is the very first text block (when _lastPosition is default)
        if (_lastPosition == default)
        {
            _lastPosition = CurrentState.GetTextPosition();
        }
    }

    protected override void OnEndText()
    {
        _inTextObject = false;
    }

    protected override void OnTextPositionChanged()
    {
        if (!_inTextObject) return;

        _cursorValid = false;   // matrix moved: next show-text restarts the cursor from the matrix

        Vector2 currentPosition = CurrentState.GetTextPosition();
        float distance = Vector2.Distance(_lastPosition, currentPosition);
        var threshold = (float)(SpaceThreshold * CurrentState.FontSize);

        // If significant movement, add space or newline
        if (distance <= threshold) return;
        // Vertical movement suggests new line
        if (Math.Abs(currentPosition.Y - _lastPosition.Y) > CurrentState.FontSize * 0.5)
        {
            _textBuilder.Append('\n');
        }
        // Horizontal movement suggests space
        else if (Math.Abs(currentPosition.X - _lastPosition.X) > threshold)
        {
            _textBuilder.Append(' ');
        }

        // Don't update _lastPosition here - let OnShowText() update it to the END position
        // after calculating text width. This ensures we're always measuring gaps between
        // the END of one text and the START of the next.
    }

    private protected override void OnShowText(PdfString text)
    {
        if (!_inTextObject) return;

        Vector2 position = CurrentState.GetTextPosition();
        if (!_cursorValid)
        {
            _cursor = position;
            _cursorValid = true;
        }

        // Get current font object
        PdfFont? font = null;
        if (_resources is not null && !string.IsNullOrEmpty(CurrentState.FontName))
        {
            font = _resources.GetFontObject(CurrentState.FontName);
        }

        // Decode text using font
        string decodedText = DecodeText(text, font);

        // Calculate effective font size by extracting scale from TextMatrix
        // The TextMatrix contains scaling factors that affect the actual rendered size
        // Extract Y-scale from the second column vector: sqrt(M12^2 + M22^2)
        double scaleY = Math.Sqrt(
            CurrentState.TextMatrix.M12 * CurrentState.TextMatrix.M12 +
            CurrentState.TextMatrix.M22 * CurrentState.TextMatrix.M22
        );
        double effectiveFontSize = CurrentState.FontSize * scaleY;

        // Calculate actual text advance using font metrics (also stored on the fragment so
        // consumers can build highlight rectangles without re-resolving font metrics).
        // The advance is in TEXT space — it must carry the text matrix's horizontal scale into
        // user space, exactly as effectiveFontSize carries the vertical one. Documents that set
        // "/F1 1 Tf" with the real size in Tm (e.g. "28 0 0 28 x y Tm") otherwise get advances
        // 28× too small: every fragment of a line stacks near the line start, and highlight
        // boxes / hit-testing built from X/Width compress into the first glyph (2026-07-04).
        double advance = CalculateTextWidth(text.Bytes, font, CurrentState.FontSize,
            CurrentState.CharacterSpacing, CurrentState.WordSpacing, CurrentState.HorizontalScaling) * TextMatrixScaleX();

        int textOffset = _textBuilder.Length;
        _textBuilder.Append(decodedText);
        _fragments.Add(new TextFragment
        {
            Text = decodedText,
            X = _cursor.X,
            Y = _cursor.Y,
            FontName = CurrentState.FontName,
            FontSize = effectiveFontSize,  // Use effective (scaled) font size
            Width = advance,
            TextOffset = textOffset
        });

        _cursor = _cursor with { X = _cursor.X + (float)advance };
        _lastPosition = _cursor;
    }

    private protected override void OnShowTextWithPositioning(PdfArray array)
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
                    // Values are in thousandths of a unit of text space — scaled to user space
                    // by font size AND the text matrix's horizontal scale (like OnShowText).
                    double adjustment = -intVal.Value / 1000.0 * CurrentState.FontSize
                        * (CurrentState.HorizontalScaling / 100.0) * TextMatrixScaleX();
                    if (_cursorValid)
                        _cursor = _cursor with { X = _cursor.X + (float)adjustment };
                    _lastPosition = _cursorValid ? _cursor : CurrentState.GetTextPosition();
                    break;
                }
                case PdfReal realVal:
                {
                    double adjustment = -realVal.Value / 1000.0 * CurrentState.FontSize
                        * (CurrentState.HorizontalScaling / 100.0) * TextMatrixScaleX();
                    if (_cursorValid)
                        _cursor = _cursor with { X = _cursor.X + (float)adjustment };
                    _lastPosition = _cursorValid ? _cursor : CurrentState.GetTextPosition();
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Handles XObject invocation (Do operator) to extract text from Form XObjects
    /// </summary>
    protected override void OnInvokeXObject(string name)
    {
        PdfLogger.Log(LogCategory.Text, $"[DEBUG] OnInvokeXObject called for: {name}");

        // Get the XObject from resources
        if (_resources is null)
        {
            PdfLogger.Log(LogCategory.Text, "[DEBUG] No resources available");
            return;
        }

        PdfStream? xobject = _resources.GetXObject(name);
        if (xobject is null)
        {
            PdfLogger.Log(LogCategory.Text, $"[DEBUG] XObject '{name}' not found in resources");
            return;
        }

        // Skip image XObjects - we only care about Form XObjects
        if (PdfImage.IsImageXObject(xobject))
        {
            PdfLogger.Log(LogCategory.Text, $"[DEBUG] XObject '{name}' is an image, skipping");
            return;
        }

        // Check if this is a Form XObject
        if (!IsFormXObject(xobject))
        {
            PdfLogger.Log(LogCategory.Text, $"[DEBUG] XObject '{name}' is not a Form XObject");
            return;
        }

        PdfLogger.Log(LogCategory.Text, $"[DEBUG] Extracting text from Form XObject '{name}'");

        // Extract text from the Form XObject
        ExtractTextFromFormXObject(xobject);
    }

    /// <summary>
    /// Checks if a stream is a Form XObject
    /// </summary>
    private static bool IsFormXObject(PdfStream stream)
    {
        if (!stream.Dictionary.TryGetValue(new PdfName("Subtype"), out PdfObject obj))
            return false;
        return obj is PdfName { Value: "Form" };
    }

    /// <summary>
    /// Recursively extracts text from a Form XObject
    /// </summary>
    private void ExtractTextFromFormXObject(PdfStream formStream)
    {
        // Get the Form XObject's content data
        byte[] contentData = formStream.GetDecodedData(_document?.Decryptor);

        // Get the Form's Resources dictionary if present. Pass the document so the form's own fonts
        // (typically indirect references) and their ToUnicode/encoding/descriptor streams resolve.
        PdfResources? formResources = _resources;
        if (formStream.Dictionary.TryGetValue(new PdfName("Resources"), out PdfObject resourcesObj))
        {
            if (resourcesObj is PdfDictionary resourcesDict)
            {
                formResources = new PdfResources(resourcesDict, _document);
            }
        }

        // Create a new extractor for the form content
        var formExtractor = new PdfTextExtractor(formResources ?? _resources, _document);

        // Parse and process the Form XObject's content stream
        List<PdfOperator> operators = PdfContentParser.Parse(contentData);
        formExtractor.ProcessOperators(operators);

        // Placement transform: a fragment in the form's local space maps to the invoker's space via
        // the form's /Matrix then the CTM at Do time (row-vector convention, matching the `matrix *
        // Ctm` concat above). Page-level extraction starts at CTM == identity, so ctmAtDo IS the
        // page-relative placement — consistent with outer fragments, which never apply the CTM.
        Matrix3x2 placement = ReadFormMatrix(formStream) * CurrentState.Ctm;
        double hScale = Math.Sqrt(placement.M11 * placement.M11 + placement.M21 * placement.M21);
        double vScale = Math.Sqrt(placement.M12 * placement.M12 + placement.M22 * placement.M22);

        // Seam separator: without it, outer text glues directly onto form-hosted text ("OuterInside")
        // and a query straddling the seam can spuriously match (deferral from the search slice, now
        // closed). The separator belongs to no fragment, like every other heuristic separator here.
        string formText = formExtractor.GetText();
        if (_textBuilder.Length > 0 && formText.Length > 0 && !char.IsWhiteSpace(_textBuilder[^1]))
            _textBuilder.Append(' ');

        int baseOffset = _textBuilder.Length;
        _textBuilder.Append(formText);
        foreach (TextFragment fragment in formExtractor.GetTextFragments())
        {
            var mapped = Vector2.Transform(new Vector2((float)fragment.X, (float)fragment.Y), placement);
            _fragments.Add(new TextFragment
            {
                Text = fragment.Text,
                X = mapped.X,
                Y = mapped.Y,
                FontName = fragment.FontName,
                FontSize = fragment.FontSize * vScale,
                Width = fragment.Width * hScale,
                TextOffset = baseOffset + fragment.TextOffset
            });
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
        if (font is null) return Encoding.Latin1.GetString(bytes);
        var sb = new StringBuilder();

        // Type0 fonts use multibyte character codes (typically 2 bytes)
        if (font.FontType == PdfFontType.Type0)
        {
            // Read 2 bytes at a time for Type0 fonts
            for (var i = 0; i < bytes.Length - 1; i += 2)
            {
                int charCode = (bytes[i] << 8) | bytes[i + 1];
                string decoded = font.DecodeCharacter(charCode);
                sb.Append(decoded);
            }

            // Handle odd byte at the end (shouldn't happen in well-formed PDFs)
            if (bytes.Length % 2 != 1) return sb.ToString();
            {
                string decoded = font.DecodeCharacter(bytes[^1]);
                sb.Append(decoded);
            }
        }
        else
        {
            // Type1, Type3, TrueType fonts use single-byte character codes
            foreach (byte b in bytes)
            {
                string decoded = font.DecodeCharacter(b);
                sb.Append(decoded);
            }
        }

        return sb.ToString();

        // Fall back to Latin-1/PDFDocEncoding (similar to Windows-1252)
    }

    /// <summary>Horizontal scale of the current text matrix — length of its first column vector
    /// (sqrt(M11² + M21²)), the X-axis analogue of the scaleY used for effectiveFontSize.
    /// 1.0 for the common identity/translation-only Tm.</summary>
    private double TextMatrixScaleX() => Math.Sqrt(
        CurrentState.TextMatrix.M11 * CurrentState.TextMatrix.M11 +
        CurrentState.TextMatrix.M21 * CurrentState.TextMatrix.M21);

    /// <summary>Per-code displacement per ISO 32000-1 §9.4.4: tx = (w0×Tfs + Tc + Tw)×Th, where
    /// Tw applies only to single-byte code 32 (never to 2-byte Type0 codes). Without a font the
    /// per-code base width falls back to fontSize×0.5 per byte (unchanged), but spacing and
    /// scaling still apply — Tc/Tw/Th displacement is font-independent.
    ///
    /// Consume CODES, not bytes: Type0 fonts use 2-byte big-endian codes (same loop as the
    /// renderer's show-text path). Summing per-byte widths looked up garbage codes — mostly
    /// the 1000-unit CID default — inflating every Type0 run's advance, which dragged the
    /// fragment map right of the rendered glyphs (2026-07-05 bullet-line highlight gap).</summary>
    private static double CalculateTextWidth(byte[] bytes, PdfFont? font, double fontSize,
        double charSpacing, double wordSpacing, double horizontalScaling)
    {
        bool isType0 = font is { FontType: PdfFontType.Type0 };
        double total = 0;
        var i = 0;
        while (i < bytes.Length)
        {
            int code;
            if (isType0 && i + 1 < bytes.Length)
            {
                code = (bytes[i] << 8) | bytes[i + 1];
                i += 2;
            }
            else
            {
                code = bytes[i];
                i++;
            }
            double baseWidth = font is not null ? font.GetCharacterWidth(code) * fontSize / 1000.0
                                                : fontSize * 0.5;
            double advance = baseWidth + charSpacing;
            if (!isType0 && code == 32) advance += wordSpacing;
            total += advance;
        }
        return total * horizontalScaling / 100.0;
    }

    /// <summary>The form's /Matrix (default identity): six numbers [a b c d e f].</summary>
    private static Matrix3x2 ReadFormMatrix(PdfStream formStream)
    {
        if (!formStream.Dictionary.TryGetValue(new PdfName("Matrix"), out PdfObject? obj) ||
            obj is not PdfArray { Count: 6 } m)
            return Matrix3x2.Identity;
        float N(PdfObject o) => o switch
        {
            PdfInteger i => i.Value,
            PdfReal r => (float)r.Value,
            _ => 0f,
        };
        return new Matrix3x2(N(m[0]), N(m[1]), N(m[2]), N(m[3]), N(m[4]), N(m[5]));
    }
}