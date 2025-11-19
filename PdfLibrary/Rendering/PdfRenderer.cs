using System.Numerics;
using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering;

/// <summary>
/// Renders PDF content streams to a platform-specific rendering target
/// Extends PdfContentProcessor to handle all PDF operators and maintain graphics state
/// </summary>
public class PdfRenderer : PdfContentProcessor
{
    private readonly IRenderTarget _target;
    private readonly PdfResources? _resources;
    private readonly IPathBuilder _currentPath;
    private readonly OptionalContentManager? _optionalContentManager;
    private readonly PdfDocument? _document;

    /// <summary>
    /// Creates a new PDF renderer
    /// </summary>
    /// <param name="target">The rendering target (WPF, Skia, etc.)</param>
    /// <param name="resources">Page resources for fonts, images, etc.</param>
    /// <param name="optionalContentManager">Optional content manager for layer visibility</param>
    /// <param name="document">The PDF document (for resolving indirect references in images)</param>
    public PdfRenderer(IRenderTarget target, PdfResources? resources = null, OptionalContentManager? optionalContentManager = null, PdfDocument? document = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _resources = resources;
        _currentPath = new PathBuilder();
        _optionalContentManager = optionalContentManager;
        _document = document;
    }

    /// <summary>
    /// Renders a PDF page with full lifecycle management.
    /// Calls BeginPage/EndPage on the rendering target automatically.
    /// </summary>
    /// <param name="page">The PDF page to render</param>
    /// <param name="pageNumber">1-based page number (default: 1)</param>
    public void RenderPage(PdfPage page, int pageNumber = 1)
    {
        // Get page dimensions
        PdfRectangle mediaBox = page.GetMediaBox();
        double width = mediaBox.Width;
        double height = mediaBox.Height;

        // Begin page lifecycle
        _target.BeginPage(pageNumber, width, height);

        try
        {
            var resources = page.GetResources();
            var contents = page.GetContents();

            // Parse and process all content streams
            foreach (var stream in contents)
            {
                var decodedData = stream.GetDecodedData();
                var operators = PdfContentParser.Parse(decodedData);
                ProcessOperators(operators);
            }
        }
        finally
        {
            // Always end page, even if exception occurs
            _target.EndPage();
        }
    }

    // ==================== Graphics State ====================

    protected override void OnMatrixChanged()
    {
        // Matrix is already updated in CurrentState
        // Rendering targets typically apply transformations when drawing
    }

    // ==================== Path Construction ====================

    protected override void OnMoveTo(double x, double y)
    {
        // Transform point to device space
        var transformed = Vector2.Transform(new Vector2((float)x, (float)y), CurrentState.Ctm);
        _currentPath.MoveTo(transformed.X, transformed.Y);
    }

    protected override void OnLineTo(double x, double y)
    {
        var transformed = Vector2.Transform(new Vector2((float)x, (float)y), CurrentState.Ctm);
        _currentPath.LineTo(transformed.X, transformed.Y);
    }

    protected override void OnCurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        var p1 = Vector2.Transform(new Vector2((float)x1, (float)y1), CurrentState.Ctm);
        var p2 = Vector2.Transform(new Vector2((float)x2, (float)y2), CurrentState.Ctm);
        var p3 = Vector2.Transform(new Vector2((float)x3, (float)y3), CurrentState.Ctm);
        _currentPath.CurveTo(p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y);
    }

    protected override void OnRectangle(double x, double y, double width, double height)
    {
        System.Diagnostics.Debug.WriteLine($"OnRectangle: ({x}, {y}) size ({width}, {height})");
        System.Diagnostics.Debug.WriteLine($"  CTM: [{CurrentState.Ctm.M11}, {CurrentState.Ctm.M12}, {CurrentState.Ctm.M21}, {CurrentState.Ctm.M22}, {CurrentState.Ctm.M31}, {CurrentState.Ctm.M32}]");

        // Transform rectangle corners
        var p1 = Vector2.Transform(new Vector2((float)x, (float)y), CurrentState.Ctm);
        var p2 = Vector2.Transform(new Vector2((float)(x + width), (float)y), CurrentState.Ctm);
        var p3 = Vector2.Transform(new Vector2((float)(x + width), (float)(y + height)), CurrentState.Ctm);
        var p4 = Vector2.Transform(new Vector2((float)x, (float)(y + height)), CurrentState.Ctm);

        System.Diagnostics.Debug.WriteLine($"  Transformed: ({p1.X}, {p1.Y}) ({p2.X}, {p2.Y}) ({p3.X}, {p3.Y}) ({p4.X}, {p4.Y})");

        // Build rectangle path
        _currentPath.MoveTo(p1.X, p1.Y);
        _currentPath.LineTo(p2.X, p2.Y);
        _currentPath.LineTo(p3.X, p3.Y);
        _currentPath.LineTo(p4.X, p4.Y);
        _currentPath.ClosePath();
    }

    protected override void OnClosePath()
    {
        _currentPath.ClosePath();
    }

    // ==================== Path Painting ====================

    protected override void OnStroke()
    {
        if (!_currentPath.IsEmpty)
        {
            _target.StrokePath(_currentPath, CurrentState);
            _currentPath.Clear();
        }
    }

    protected override void OnFill(bool evenOdd)
    {
        if (!_currentPath.IsEmpty)
        {
            _target.FillPath(_currentPath, CurrentState, evenOdd);
            _currentPath.Clear();
        }
    }

    protected override void OnFillAndStroke()
    {
        if (!_currentPath.IsEmpty)
        {
            _target.FillAndStrokePath(_currentPath, CurrentState, evenOdd: false);
            _currentPath.Clear();
        }
    }

    protected override void OnEndPath()
    {
        // End path without painting (used for clipping)
        if (!_currentPath.IsEmpty)
        {
            _target.SetClippingPath(_currentPath, CurrentState, evenOdd: false);
            _currentPath.Clear();
        }
    }

    // ==================== Text Rendering ====================

    protected override void OnShowText(PdfString text)
    {
        System.Diagnostics.Debug.WriteLine($"PdfRenderer.OnShowText called: {text.Bytes.Length} bytes");

        if (_resources == null || CurrentState.FontName == null)
        {
            System.Diagnostics.Debug.WriteLine($"  SKIPPED: _resources={_resources != null}, FontName={CurrentState.FontName}");
            return;
        }

        // Get the font
        PdfFont? font = _resources.GetFontObject(CurrentState.FontName);
        if (font == null)
        {
            System.Diagnostics.Debug.WriteLine($"  SKIPPED: Font '{CurrentState.FontName}' not found in resources");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"  Font: {CurrentState.FontName}, Size: {CurrentState.FontSize}");

        // Decode the text - handle multi-byte encodings for Type0 fonts
        byte[] bytes = text.Bytes;
        var decodedText = new StringBuilder();
        var glyphWidths = new List<double>();
        var charCodes = new List<int>();

        // Type0 fonts use multi-byte character codes (typically 2 bytes)
        bool isType0 = font.FontType == PdfFontType.Type0;
        int bytesPerChar = isType0 ? 2 : 1;

        int i = 0;
        while (i < bytes.Length)
        {
            int charCode;

            if (isType0 && i + 1 < bytes.Length)
            {
                // Read 2 bytes for Type0 fonts (big-endian)
                charCode = (bytes[i] << 8) | bytes[i + 1];
                i += 2;
            }
            else
            {
                // Read single byte for simple fonts
                charCode = bytes[i];
                i++;
            }

            // Decode character
            string decoded = font.DecodeCharacter(charCode);

            // Debug logging for specific character codes
            if (charCode == 0x03 || charCode == 0x0003 || charCode == 0x0766 || charCode is >= 0x0700 and <= 0x0800)
            {
                System.Diagnostics.Debug.WriteLine($"  DEBUG: charCode=0x{charCode:X4} → '{decoded}' (U+{((int)decoded[0]):X4})");
            }

            decodedText.Append(decoded);
            charCodes.Add(charCode);

            // Get character width in text space (1000-unit system) and convert to USER SPACE
            // The width is in the font's coordinate system (typically 1000 units = 1 em)
            // We need to scale by fontSize to get user space units
            double glyphWidth = font.GetCharacterWidth(charCode);
            double advance = glyphWidth * CurrentState.FontSize / 1000.0;

            // Apply horizontal scaling
            advance *= CurrentState.HorizontalScaling / 100.0;

            // Add character spacing (already in user space)
            if (CurrentState.CharacterSpacing != 0)
                advance += CurrentState.CharacterSpacing;

            // Add word spacing for spaces (already in user space)
            if (decoded == " " && CurrentState.WordSpacing != 0)
                advance += CurrentState.WordSpacing;

            glyphWidths.Add(advance);
        }

        // Render the text
        string textToRender = decodedText.ToString();
        _target.DrawText(textToRender, glyphWidths, CurrentState, font, charCodes);

        // Advance text position by the total width
        double totalAdvance = glyphWidths.Sum();
        CurrentState.AdvanceTextMatrix(totalAdvance, 0);
    }

    protected override void OnColorChanged()
    {
        // Resolve named color spaces to device color spaces
        var fillCs = CurrentState.FillColorSpace;
        var fillColor = CurrentState.FillColor;
        ResolveColorSpace(ref fillCs, ref fillColor);
        CurrentState.FillColorSpace = fillCs;
        CurrentState.FillColor = fillColor;

        var strokeCs = CurrentState.StrokeColorSpace;
        var strokeColor = CurrentState.StrokeColor;
        ResolveColorSpace(ref strokeCs, ref strokeColor);
        CurrentState.StrokeColorSpace = strokeCs;
        CurrentState.StrokeColor = strokeColor;
    }

    private void ResolveColorSpace(ref string? colorSpaceName, ref List<double>? color)
    {
        if (string.IsNullOrEmpty(colorSpaceName))
            return;

        // Ensure color list exists
        color ??= new List<double>();

        // Skip device color spaces - they don't need resolution
        if (colorSpaceName == "DeviceGray" || colorSpaceName == "DeviceRGB" || colorSpaceName == "DeviceCMYK")
            return;

        // Try to resolve named color space from resources

        var colorSpaces = _resources?.GetColorSpaces();
        if (colorSpaces == null)
            return;

        if (!colorSpaces.TryGetValue(new PdfName(colorSpaceName), out PdfObject? csObj))
            return;

        // Resolve indirect reference
        if (csObj is PdfIndirectReference reference && _document != null)
            csObj = _document.ResolveReference(reference);

        // Parse color space array
        // Can be: [/ICCBased stream] or [/Separation name alternateSpace tintTransform]
        if (csObj is PdfArray { Count: >= 2 } csArray)
        {
            if (csArray[0] is not PdfName csType)
                return;

            // Handle ICCBased color space: [/ICCBased stream]
            if (csType.Value == "ICCBased" && csArray.Count >= 2)
            {
                // Get the ICC profile stream
                PdfObject? streamObj = csArray[1];
                if (streamObj is PdfIndirectReference streamRef && _document != null)
                    streamObj = _document.ResolveReference(streamRef);

                if (streamObj is PdfStream iccStream)
                {
                    // Get stream dictionary to find alternate color space and number of components
                    var streamDict = iccStream.Dictionary;

                    // Get /N (number of components): 1=Gray, 3=RGB, 4=CMYK
                    int numComponents = 1;
                    if (streamDict.TryGetValue(new PdfName("N"), out PdfObject? nObj) && nObj is PdfInteger nNum)
                    {
                        numComponents = (int)nNum.Value;
                    }

                    Console.WriteLine($"[RESOLVE] ICCBased '{colorSpaceName}': N={numComponents}, current color has {color.Count} components, color=[{string.Join(", ", color.Select(c => c.ToString("F2")))}]");

                    // Get /Alternate color space
                    string? alternateSpace = null;
                    if (streamDict.TryGetValue(new PdfName("Alternate"), out PdfObject? altObj))
                    {
                        if (altObj is PdfName altName)
                        {
                            alternateSpace = altName.Value;
                        }
                    }

                    // For now, use the alternate color space directly with the color values
                    // This is a simplification - ideally we'd process the ICC profile
                    if (alternateSpace != null)
                    {
                        colorSpaceName = alternateSpace;
                    }
                    else
                    {
                        // No alternate specified, infer from component count
                        colorSpaceName = numComponents switch
                        {
                            1 => "DeviceGray",
                            3 => "DeviceRGB",
                            4 => "DeviceCMYK",
                            _ => "DeviceGray"
                        };
                    }

                    // Initialize default colors if component count doesn't match
                    if (color.Count != numComponents)
                    {
                        color = numComponents switch
                        {
                            1 => [0.0],
                            3 => [0.0, 0.0, 0.0],
                            4 => [0.0, 0.0, 0.0, 1.0],
                            _ => [0.0]
                        };
                    }
                }
            }
            // Handle Separation color space: [/Separation name alternateSpace tintTransform]
            else if (csType.Value == "Separation" && csArray is [_, _, PdfName alternateName, ..])
                // Get alternate color space (usually /DeviceRGB or /DeviceCMYK)
            {
                string altSpace = alternateName.Value;

                // For now, simple heuristic: if alternate is DeviceRGB, map tint to grayscale in that space
                // A tint of 0 typically means "no ink" (white) and 1 means "full ink"
                // But Separation colors are usually inverted: 0 = full color, 1 = no color
                if (color.Count == 1)
                {
                    double tint = color[0];

                    // Most Separation spaces use tint where 0 = full color, 1 = no color
                    // The tint transform function would normally handle this, but as a simple
                    // approximation, we'll use: output = 1 - tint for each component
                    if (altSpace == "DeviceRGB")
                    {
                        // For a typical spot color, full tint (0) produces the spot color
                        // We need the actual tint transform, but as an approximation:
                        // Assume the separation is a spot color that maps to a pure hue
                        // For now, just convert to grayscale: 0 = black, 1 = white
                        double value = 1.0 - tint; // Invert: 0 becomes 1 (white), 1 becomes 0 (black)
                        color = [value, value, value];
                        colorSpaceName = "DeviceRGB";
                    }
                    else if (altSpace == "DeviceGray")
                    {
                        double value = 1.0 - tint;
                        color = [value];
                        colorSpaceName = "DeviceGray";
                    }
                }
            }
        }
    }

    protected override void OnShowTextWithPositioning(PdfArray array)
    {
        // TJ operator: combine all strings and adjustments into a single DrawText call
        if (_resources == null || CurrentState.FontName == null)
            return;

        PdfFont? font = _resources.GetFontObject(CurrentState.FontName);
        if (font == null) return;

        bool isType0 = font.FontType == PdfFontType.Type0;
        var combinedText = new StringBuilder();
        var combinedWidths = new List<double>();
        var combinedCharCodes = new List<int>();

        // Process all items in the TJ array
        foreach (PdfObject item in array)
        {
            switch (item)
            {
                case PdfString str:
                {
                    // Decode characters from this string segment
                    byte[] bytes = str.Bytes;
                    int i = 0;
                    while (i < bytes.Length)
                    {
                        int charCode;
                        if (isType0 && i + 1 < bytes.Length)
                        {
                            charCode = (bytes[i] << 8) | bytes[i + 1];
                            i += 2;
                        }
                        else
                        {
                            charCode = bytes[i];
                            i++;
                        }

                        string decoded = font.DecodeCharacter(charCode);
                        combinedText.Append(decoded);
                        combinedCharCodes.Add(charCode);

                        // Get character width and convert to USER SPACE
                        double glyphWidth = font.GetCharacterWidth(charCode);
                        double advance = glyphWidth * CurrentState.FontSize / 1000.0;
                        advance *= CurrentState.HorizontalScaling / 100.0;

                        // Add character spacing
                        if (CurrentState.CharacterSpacing != 0)
                            advance += CurrentState.CharacterSpacing;

                        // Add word spacing for spaces
                        if (decoded == " " && CurrentState.WordSpacing != 0)
                            advance += CurrentState.WordSpacing;

                        combinedWidths.Add(advance);
                    }
                    break;
                }
                case PdfInteger intVal:
                {
                    // Kerning adjustment: modify the width of the previous character
                    // Adjustment is in thousandths of text space, convert to user space
                    if (combinedWidths.Count > 0)
                    {
                        double adjustment = -intVal.Value / 1000.0 * CurrentState.FontSize;
                        adjustment *= CurrentState.HorizontalScaling / 100.0;
                        combinedWidths[^1] += adjustment;
                    }
                    break;
                }
                case PdfReal realVal:
                {
                    if (combinedWidths.Count > 0)
                    {
                        double adjustment = -realVal.Value / 1000.0 * CurrentState.FontSize;
                        adjustment *= CurrentState.HorizontalScaling / 100.0;
                        combinedWidths[^1] += adjustment;
                    }
                    break;
                }
            }
        }

        // Render all text in a single DrawText call
        if (combinedText.Length > 0)
        {
            string textPreview = combinedText.ToString().Substring(0, Math.Min(20, combinedText.Length));
            Console.WriteLine($"[TJ] Rendering '{textPreview}...' at ({CurrentState.GetTextPosition().X:F2}, {CurrentState.GetTextPosition().Y:F2})");

            // DIAGNOSTIC: Log first few widths and check for zeros
            if (combinedWidths.Count > 0)
            {
                string widthsPreview = string.Join(", ", combinedWidths.Take(5).Select(w => $"{w:F4}"));
                Console.WriteLine($"     Widths: [{widthsPreview}...] Total: {combinedWidths.Sum():F4}");

                if (combinedWidths.Take(5).All(w => w == 0))
                    Console.WriteLine($"     WARNING: ZERO WIDTHS DETECTED for font {CurrentState.FontName}");
            }

            _target.DrawText(combinedText.ToString(), combinedWidths, CurrentState, font, combinedCharCodes);

            // Advance text position by total width
            double totalAdvance = combinedWidths.Sum();
            CurrentState.AdvanceTextMatrix(totalAdvance, 0);
        }
    }

    // ==================== XObject Rendering ====================

    protected override void OnInvokeXObject(string name)
    {
        Console.WriteLine($"OnInvokeXObject: {name}");

        if (_resources == null)
        {
            Console.WriteLine($"  No resources");
            return;
        }

        var xobject = _resources.GetXObject(name);
        if (xobject == null)
        {
            Console.WriteLine($"  XObject not found");
            return;
        }

        // Check if this XObject has Optional Content that's disabled
        // PDF spec: XObjects can have an /OC key that references an Optional Content Group
        // If the OCG is in the document's /OFF list, we shouldn't render it
        bool isDisabled = IsOptionalContentDisabled(xobject);
        Console.WriteLine($"  Optional content disabled: {isDisabled}");

        if (isDisabled)
        {
            Console.WriteLine($"  SKIPPING {name} - Optional Content is disabled");
            return;
        }

        // Check if this is an image XObject
        if (PdfImage.IsImageXObject(xobject))
        {
            Console.WriteLine($"  Type: Image XObject");
            try
            {
                var image = new PdfImage(xobject, _document);
                Console.WriteLine($"  Image: {image.Width}x{image.Height}, ColorSpace={image.ColorSpace}");
                _target.DrawImage(image, CurrentState);
                Console.WriteLine($"  Image drawn successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERROR rendering image: {ex.Message}");
            }
        }
        // Handle Form XObjects (nested content streams)
        else if (IsFormXObject(xobject))
        {
            Console.WriteLine($"  Type: Form XObject");
            try
            {
                RenderFormXObject(xobject);
                Console.WriteLine($"  Form XObject rendered successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERROR rendering form XObject: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"  Type: Unknown/Unsupported XObject type");
        }
    }

    /// <summary>
    /// Checks if an XObject's Optional Content is disabled
    /// Uses OptionalContentManager if available to check visibility
    /// </summary>
    private bool IsOptionalContentDisabled(PdfStream xobject)
    {
        if (_optionalContentManager == null)
        {
            // No OCG manager - render everything
            return false;
        }

        // Use OptionalContentManager to check visibility
        return !_optionalContentManager.IsVisible(xobject);
    }

    /// <summary>
    /// Checks if a stream is a Form XObject
    /// </summary>
    private static bool IsFormXObject(PdfStream stream)
    {
        if (!stream.Dictionary.TryGetValue(new PdfName("Subtype"), out PdfObject? obj))
            return false;

        return obj is PdfName { Value: "Form" };
    }

    /// <summary>
    /// Renders a Form XObject by recursively processing its content stream
    /// </summary>
    private void RenderFormXObject(PdfStream formStream)
    {
        System.Diagnostics.Debug.WriteLine($"RenderFormXObject: Current CTM before form = [{CurrentState.Ctm.M11}, {CurrentState.Ctm.M12}, {CurrentState.Ctm.M21}, {CurrentState.Ctm.M22}, {CurrentState.Ctm.M31}, {CurrentState.Ctm.M32}]");

        // Get the Form XObject's content data
        byte[] contentData = formStream.GetDecodedData();

        // Get the Form's Resources dictionary (if any)
        // Form XObjects can have their own resources, or inherit from the page
        PdfResources? formResources = _resources;
        if (formStream.Dictionary.TryGetValue(new PdfName("Resources"), out PdfObject? resourcesObj))
        {
            if (resourcesObj is PdfDictionary resourcesDict)
            {
                // Create new resources object for the form
                formResources = new PdfResources(resourcesDict);
            }
            // TODO: Handle indirect references to Resources dictionaries
            // Would need access to the PdfDocument to resolve references
        }

        // According to PDF spec (ISO 32000-1 section 8.10):
        // Form XObjects execute with a fresh graphics state, BUT the CTM from the
        // invoking context is inherited (and concatenated with the form's Matrix if present)

        // Save the current CTM to apply to the form's coordinate space
        Matrix3x2 savedCtm = CurrentState.Ctm;

        // Create a new renderer for the form to ensure it starts with a fresh graphics state
        var formRenderer = new PdfRenderer(_target, formResources ?? _resources, _optionalContentManager, _document);

        // Set the form renderer's CTM to the saved CTM from the page
        formRenderer.CurrentState.Ctm = savedCtm;

        // TODO: If form has a /Matrix entry, concatenate it with the saved CTM
        // formCtm = formMatrix * savedCtm

        System.Diagnostics.Debug.WriteLine($"RenderFormXObject: Form renderer CTM = [{formRenderer.CurrentState.Ctm.M11}, {formRenderer.CurrentState.Ctm.M12}, {formRenderer.CurrentState.Ctm.M21}, {formRenderer.CurrentState.Ctm.M22}, {formRenderer.CurrentState.Ctm.M31}, {formRenderer.CurrentState.Ctm.M32}]");

        // Parse and process the Form XObject's content stream
        var operators = PdfContentParser.Parse(contentData);
        formRenderer.ProcessOperators(operators);
    }

    // ==================== State Management ====================

    protected override void ProcessOperator(PdfOperator op)
    {
        // Handle save/restore with render target
        if (op is SaveGraphicsStateOperator)
        {
            _target.SaveState();
        }
        else if (op is RestoreGraphicsStateOperator)
        {
            _target.RestoreState();
        }

        // Call base implementation to update CurrentState
        base.ProcessOperator(op);
    }
}
