using System.Diagnostics;
using System.Numerics;
using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using Logging;
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
    private PdfResources? _currentResources; // Can be swapped for annotation resources
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
        _currentResources = resources; // Initially use page resources
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
        // Get page dimensions from MediaBox
        PdfRectangle mediaBox = page.GetMediaBox();
        double width = mediaBox.Width;
        double height = mediaBox.Height;

        PdfLogger.Log(LogCategory.Transforms, $"RenderPage: MediaBox={mediaBox}");

        // Begin the page lifecycle
        _target.BeginPage(pageNumber, width, height);

        try
        {
            PdfResources? resources = page.GetResources();
            List<PdfStream> contents = page.GetContents();

            // Diagnostic: List available XObjects
            if (resources != null)
            {
                List<string> xobjectNames = resources.GetXObjectNames();
                PdfLogger.Log(LogCategory.PdfTool, $"XObjects available: [{string.Join(", ", xobjectNames)}]");

                // Also list color spaces
                PdfDictionary? colorSpaces = resources.GetColorSpaces();
                if (colorSpaces != null)
                {
                    List<string> csNames = colorSpaces.Keys.Select(k => k.Value).ToList();
                    PdfLogger.Log(LogCategory.PdfTool, $"ColorSpaces available: [{string.Join(", ", csNames)}]");
                }

                // Diagnostic: List available fonts
                List<string> fontNames = resources.GetFontNames();
                PdfLogger.Log(LogCategory.PdfTool, $"Page {pageNumber} Fonts available: [{string.Join(", ", fontNames)}]");
                PdfLogger.Log(LogCategory.PdfTool, $"Page {pageNumber} _currentResources has {(_currentResources != null ? _currentResources.GetFontNames().Count : 0)} fonts");
            }

            PdfLogger.Log(LogCategory.PdfTool, $"Processing {contents.Count} content stream(s)");

            // Parse and process all content streams
            var streamIndex = 0;
            foreach (PdfStream stream in contents)
            {
                byte[] decodedData = stream.GetDecodedData();

                // Diagnostic: Dump the first stream's raw content to see scn operands
                if (streamIndex == 0 && decodedData.Length > 0)
                {
                    string text = Encoding.ASCII.GetString(decodedData);
                    // Find and show context around scn/SCN operators
                    string[] lines = text.Split('\n');
                    foreach (string line in lines.Take(50))  // First 50 lines
                    {
                        if (line.Contains("scn") || line.Contains("SCN") || line.Contains(" cs") || line.Contains(" CS"))
                        {
                            PdfLogger.Log(LogCategory.Graphics, $"RAW: {line.Trim()}");
                        }
                    }
                }

                List<PdfOperator> operators = PdfContentParser.Parse(decodedData);

                // Diagnostic: Count operator types
                int doOps = operators.Count(o => o.Name == "Do");
                int csOps = operators.Count(o => o.Name is "cs" or "CS");
                int scnOps = operators.Count(o => o.Name is "scn" or "SCN" or "sc" or "SC");
                PdfLogger.Log(LogCategory.PdfTool, $"Stream {streamIndex}: Total: {operators.Count}, Do: {doOps}, cs/CS: {csOps}, scn/SCN/sc/SC: {scnOps}");

                ProcessOperators(operators);
                streamIndex++;
            }

            // Render annotation appearances
            RenderAnnotations(page);
        }
        finally
        {
            // Always end page, even if exception occurs
            _target.EndPage();
        }
    }

    /// <summary>
    /// Renders annotation appearance streams on the page
    /// </summary>
    private void RenderAnnotations(PdfPage page)
    {
        PdfArray? annotations = page.GetAnnotations();
        if (annotations == null || annotations.Count == 0)
            return;

        PdfLogger.Log(LogCategory.Graphics, $"Found {annotations.Count} annotations");

        foreach (PdfObject annotObj in annotations)
        {
            // Resolve annotation reference if needed
            PdfDictionary? annotDict = annotObj switch
            {
                PdfIndirectReference reference => _document?.GetObject(reference.ObjectNumber) as PdfDictionary,
                PdfDictionary dict => dict,
                _ => null
            };

            if (annotDict == null)
                continue;

            // Get annotation rectangle
            if (!annotDict.TryGetValue(new PdfName("Rect"), out PdfObject rectObj))
                continue;

            PdfArray? rectArray = rectObj switch
            {
                PdfArray arr => arr,
                PdfIndirectReference rRef => _document?.GetObject(rRef.ObjectNumber) as PdfArray,
                _ => null
            };

            if (rectArray == null || rectArray.Count < 4)
                continue;

            double llx = GetAnnotNumber(rectArray[0]);
            double lly = GetAnnotNumber(rectArray[1]);
            double urx = GetAnnotNumber(rectArray[2]);
            double ury = GetAnnotNumber(rectArray[3]);

            // Get appearance dictionary
            if (!annotDict.TryGetValue(new PdfName("AP"), out PdfObject apObj))
                continue;

            PdfDictionary? apDict = apObj switch
            {
                PdfDictionary dict => dict,
                PdfIndirectReference apRef => _document?.GetObject(apRef.ObjectNumber) as PdfDictionary,
                _ => null
            };

            if (apDict == null)
                continue;

            // Get normal appearance (N)
            if (!apDict.TryGetValue(new PdfName("N"), out PdfObject nObj))
                continue;

            // N can be a stream or a dictionary of streams (for different appearance states)
            PdfStream? appearanceStream = nObj switch
            {
                PdfStream stream => stream,
                PdfIndirectReference nRef => _document?.GetObject(nRef.ObjectNumber) as PdfStream,
                PdfDictionary stateDict => GetAppearanceFromStateDict(stateDict, annotDict),
                _ => null
            };

            if (appearanceStream == null)
                continue;

            // Get appearance stream's BBox
            PdfArray? bbox = null;
            if (appearanceStream.Dictionary.TryGetValue(new PdfName("BBox"), out PdfObject bboxObj))
            {
                bbox = bboxObj switch
                {
                    PdfArray arr => arr,
                    PdfIndirectReference bRef => _document?.GetObject(bRef.ObjectNumber) as PdfArray,
                    _ => null
                };
            }

            double bboxLlx = 0, bboxLly = 0, bboxUrx = 1, bboxUry = 1;
            if (bbox is { Count: >= 4 })
            {
                bboxLlx = GetAnnotNumber(bbox[0]);
                bboxLly = GetAnnotNumber(bbox[1]);
                bboxUrx = GetAnnotNumber(bbox[2]);
                bboxUry = GetAnnotNumber(bbox[3]);
            }

            // Calculate transformation matrix to map BBox to Rect
            double rectWidth = urx - llx;
            double rectHeight = ury - lly;
            double bboxWidth = bboxUrx - bboxLlx;
            double bboxHeight = bboxUry - bboxLly;

            double sx = bboxWidth != 0 ? rectWidth / bboxWidth : 1;
            double sy = bboxHeight != 0 ? rectHeight / bboxHeight : 1;
            double tx = llx - bboxLlx * sx;
            double ty = lly - bboxLly * sy;

            PdfLogger.Log(LogCategory.Graphics, $"Rendering annotation appearance at ({llx:F1}, {lly:F1}) - ({urx:F1}, {ury:F1})");

            // Debug: check for color and border entries
            if (annotDict.TryGetValue(new PdfName("C"), out PdfObject colorObj))
            {
                if (colorObj is PdfArray colorArray)
                {
                    string components = string.Join(", ", colorArray.Select(c => c.ToString()));
                    PdfLogger.Log(LogCategory.Graphics, $"Annotation has /C color: [{components}]");
                }
            }
            if (annotDict.TryGetValue(new PdfName("Border"), out PdfObject borderObj))
            {
                PdfLogger.Log(LogCategory.Graphics, $"Annotation has /Border: {borderObj}");
            }
            if (annotDict.TryGetValue(new PdfName("Subtype"), out PdfObject subtypeObj))
            {
                PdfLogger.Log(LogCategory.Graphics, $"Annotation Subtype: {subtypeObj}");
            }

            // Get annotation appearance stream resources
            PdfResources? annotResources = null;
            if (appearanceStream.Dictionary.TryGetValue(new PdfName("Resources"), out PdfObject resObj))
            {
                PdfDictionary? resDict = resObj switch
                {
                    PdfDictionary dict => dict,
                    PdfIndirectReference resRef => _document?.GetObject(resRef.ObjectNumber) as PdfDictionary,
                    _ => null
                };
                if (resDict is not null)
                {
                    annotResources = new PdfResources(resDict, _document);
                    PdfLogger.Log(LogCategory.Graphics, "Using annotation resources");
                }
            }

            // Save current resources and swap in annotation resources
            PdfResources? savedResources = _currentResources;
            if (annotResources is not null)
                _currentResources = annotResources;

            // Create a new graphics state for the annotation
            // Use q/Q to save/restore state
            _target.SaveState();

            // Apply transformation: scale and translate
            CurrentState.ConcatenateMatrix((float)sx, 0, 0, (float)sy, (float)tx, (float)ty);

            // Parse and render the appearance stream
            byte[] decodedData = appearanceStream.GetDecodedData();
            List<PdfOperator> operators = PdfContentParser.Parse(decodedData);
            PdfLogger.Log(LogCategory.Graphics, $"Annotation stream has {operators.Count} operators");
            // Debug: print first few operators
            foreach (PdfOperator op in operators.Take(20))
            {
                PdfLogger.Log(LogCategory.Graphics, $"Annotation operator: {op.GetType().Name}");
            }
            ProcessOperators(operators);

            _target.RestoreState();

            // Restore original resources
            _currentResources = savedResources;
        }
    }

    private PdfStream? GetAppearanceFromStateDict(PdfDictionary stateDict, PdfDictionary annotDict)
    {
        // Get appearance state name from /AS entry
        string? stateName = null;
        if (annotDict.TryGetValue(new PdfName("AS"), out PdfObject asObj))
        {
            stateName = asObj switch
            {
                PdfName name => name.Value,
                _ => null
            };
        }

        if (string.IsNullOrEmpty(stateName))
        {
            // Use first entry if no state specified
            return stateDict.Select(kvp => kvp.Value switch
                {
                    PdfStream stream => stream,
                    PdfIndirectReference sRef => _document?.GetObject(sRef.ObjectNumber) as PdfStream,
                    _ => null
                })
                .FirstOrDefault();
        }

        // Look up the named state
        if (stateDict.TryGetValue(new PdfName(stateName), out PdfObject stateObj))
        {
            return stateObj switch
            {
                PdfStream stream => stream,
                PdfIndirectReference sRef => _document?.GetObject(sRef.ObjectNumber) as PdfStream,
                _ => null
            };
        }

        return null;
    }

    private static double GetAnnotNumber(PdfObject obj)
    {
        return obj switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => 0
        };
    }

    // ==================== Graphics State ====================

    protected override void OnMatrixChanged()
    {
        // Apply CTM to the render target's canvas
        // This follows Melville.Pdf's architecture where CTM is applied to the canvas
        // and glyph transformations are applied separately
        _target.ApplyCtm(CurrentState.Ctm);
    }

    // ==================== Path Construction ====================

    protected override void OnMoveTo(double x, double y)
    {
        // Transform point to device space
        Vector2 transformed = Vector2.Transform(new Vector2((float)x, (float)y), CurrentState.Ctm);
        _currentPath.MoveTo(transformed.X, transformed.Y);
    }

    protected override void OnLineTo(double x, double y)
    {
        Vector2 transformed = Vector2.Transform(new Vector2((float)x, (float)y), CurrentState.Ctm);
        _currentPath.LineTo(transformed.X, transformed.Y);
    }

    protected override void OnCurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        Vector2 p1 = Vector2.Transform(new Vector2((float)x1, (float)y1), CurrentState.Ctm);
        Vector2 p2 = Vector2.Transform(new Vector2((float)x2, (float)y2), CurrentState.Ctm);
        Vector2 p3 = Vector2.Transform(new Vector2((float)x3, (float)y3), CurrentState.Ctm);
        _currentPath.CurveTo(p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y);
    }

    protected override void OnRectangle(double x, double y, double width, double height)
    {
        PdfLogger.Log(LogCategory.Graphics, $"OnRectangle: ({x}, {y}) size ({width}, {height})");
        PdfLogger.Log(LogCategory.Graphics, $"  CTM: [{CurrentState.Ctm.M11}, {CurrentState.Ctm.M12}, {CurrentState.Ctm.M21}, {CurrentState.Ctm.M22}, {CurrentState.Ctm.M31}, {CurrentState.Ctm.M32}]");

        // Transform rectangle corners
        Vector2 p1 = Vector2.Transform(new Vector2((float)x, (float)y), CurrentState.Ctm);
        Vector2 p2 = Vector2.Transform(new Vector2((float)(x + width), (float)y), CurrentState.Ctm);
        Vector2 p3 = Vector2.Transform(new Vector2((float)(x + width), (float)(y + height)), CurrentState.Ctm);
        Vector2 p4 = Vector2.Transform(new Vector2((float)x, (float)(y + height)), CurrentState.Ctm);

        PdfLogger.Log(LogCategory.Graphics, $"  Transformed: ({p1.X}, {p1.Y}) ({p2.X}, {p2.Y}) ({p3.X}, {p3.Y}) ({p4.X}, {p4.Y})");

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
        if (_currentPath.IsEmpty) return;
        List<double> color = CurrentState.StrokeColor;
        string colorStr = string.Join(",", color.Select(c => c.ToString("F2")));
        PdfLogger.Log(LogCategory.Graphics, $"PATH STROKE: ColorSpace={CurrentState.StrokeColorSpace}, Color=[{colorStr}], LineWidth={CurrentState.LineWidth}");
        _target.StrokePath(_currentPath, CurrentState);
        _currentPath.Clear();
    }

    protected override void OnFill(bool evenOdd)
    {
        if (_currentPath.IsEmpty) return;
        List<double> color = CurrentState.FillColor;
        string colorStr = string.Join(",", color.Select(c => c.ToString("F2")));
        PdfLogger.Log(LogCategory.Graphics, $"PATH FILL: ColorSpace={CurrentState.FillColorSpace}, Color=[{colorStr}], PathEmpty={_currentPath.IsEmpty}");
        _target.FillPath(_currentPath, CurrentState, evenOdd);
        _currentPath.Clear();
    }

    protected override void OnFillAndStroke()
    {
        if (_currentPath.IsEmpty) return;
        _target.FillAndStrokePath(_currentPath, CurrentState, evenOdd: false);
        _currentPath.Clear();
    }

    protected override void OnEndPath()
    {
        // End path without painting (used for clipping)
        if (_currentPath.IsEmpty) return;
        _target.SetClippingPath(_currentPath, CurrentState, evenOdd: false);
        _currentPath.Clear();
    }

    // ==================== Text Rendering ====================

    protected override void OnShowText(PdfString text)
    {
        if (_currentResources == null || CurrentState.FontName == null)
        {
            PdfLogger.Log(LogCategory.Text, $"TEXT-SKIPPED: _currentResources={_currentResources != null}, FontName={CurrentState.FontName}");
            return;
        }

        // Get the font
        PdfFont? font = _currentResources.GetFontObject(CurrentState.FontName);
        if (font == null)
        {
            PdfLogger.Log(LogCategory.Text, $"TEXT-SKIPPED: Font '{CurrentState.FontName}' not found in _currentResources (has {_currentResources.GetFontNames().Count} fonts: {string.Join(", ", _currentResources.GetFontNames())})");
            return;
        }

        // Decode the text - handle multi-byte encodings for Type0 fonts
        byte[] bytes = text.Bytes;
        var decodedText = new StringBuilder();
        var glyphWidths = new List<double>();
        var charCodes = new List<int>();

        // Type0 fonts use multi-byte character codes (typically 2 bytes)
        bool isType0 = font.FontType == PdfFontType.Type0;
        int bytesPerChar = isType0 ? 2 : 1;

        var i = 0;
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
            if (charCode is 0x03 or 0x0766 or >= 0x0700 and <= 0x0800)
            {
                PdfLogger.Log(LogCategory.Text, $"  DEBUG: charCode=0x{charCode:X4} → '{decoded}' (U+{((int)decoded[0]):X4})");
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
        var textToRender = decodedText.ToString();
        _target.DrawText(textToRender, glyphWidths, CurrentState, font, charCodes);

        // Advance text position by the total width
        double totalAdvance = glyphWidths.Sum();
        CurrentState.AdvanceTextMatrix(totalAdvance, 0);
    }

    protected override void OnColorChanged()
    {
        // Resolve named color spaces to device color spaces
        string? fillCs = CurrentState.FillColorSpace;
        List<double>? fillColor = CurrentState.FillColor;
        ResolveColorSpace(ref fillCs, ref fillColor);
        CurrentState.FillColorSpace = fillCs ?? string.Empty;
        CurrentState.FillColor = fillColor ?? [];

        string? strokeCs = CurrentState.StrokeColorSpace;
        List<double>? strokeColor = CurrentState.StrokeColor;
        ResolveColorSpace(ref strokeCs, ref strokeColor);
        CurrentState.StrokeColorSpace = strokeCs ?? string.Empty;
        CurrentState.StrokeColor = strokeColor ?? [];
    }

    private void ResolveColorSpace(ref string? colorSpaceName, ref List<double>? color)
    {
        if (string.IsNullOrEmpty(colorSpaceName))
            return;

        // Ensure the color list exists
        color ??= [];

        // Skip device color spaces - they don't need resolution
        if (colorSpaceName is "DeviceGray" or "DeviceRGB" or "DeviceCMYK")
            return;

        // Try to resolve named color space from resources

        PdfDictionary? colorSpaces = _currentResources?.GetColorSpaces();
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

            switch (csType.Value)
            {
                // Handle ICCBased color space: [/ICCBased stream]
                case "ICCBased" when csArray.Count >= 2:
                {
                    // Get the ICC profile stream
                    PdfObject? streamObj = csArray[1];
                    if (streamObj is PdfIndirectReference streamRef && _document != null)
                        streamObj = _document.ResolveReference(streamRef);

                    if (streamObj is not PdfStream iccStream) return;
                    // Get stream dictionary to find alternate color space and number of components
                    PdfDictionary streamDict = iccStream.Dictionary;

                    // Get /N (number of components): 1=Gray, 3=RGB, 4=CMYK
                    var numComponents = 1;
                    if (streamDict.TryGetValue(new PdfName("N"), out PdfObject nObj) && nObj is PdfInteger nNum)
                    {
                        numComponents = nNum.Value;
                    }

                    PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: ICCBased '{colorSpaceName}': N={numComponents}, current color has {color.Count} components, color=[{string.Join(", ", color.Select(c => c.ToString("F2")))}]");

                    // Get /Alternate color space
                    string? alternateSpace = null;
                    if (streamDict.TryGetValue(new PdfName("Alternate"), out PdfObject altObj))
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

                    break;
                }
                // Handle Separation color space: [/Separation name alternateSpace tintTransform]
                // Get alternate color space (usually /DeviceRGB or /DeviceCMYK)
                case "Separation" when csArray is [_, _, PdfName alternateName, ..]:
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

                    break;
                }
            }
        }
    }

    protected override void OnShowTextWithPositioning(PdfArray array)
    {
        // TJ operator: combine all strings and adjustments into a single DrawText call
        if (_currentResources == null || CurrentState.FontName == null)
            return;

        PdfFont? font = _currentResources.GetFontObject(CurrentState.FontName);
        if (font == null)
        {
            PdfLogger.Log(LogCategory.Text, $"Font '{CurrentState.FontName}' NOT FOUND");
            return;
        }

        PdfLogger.Log(LogCategory.Text, $"Using font '{CurrentState.FontName}' Type={font.FontType} BaseFont={font.BaseFont}");
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
                    var i = 0;
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
        if (combinedText.Length <= 0) return;
        var fullText = combinedText.ToString();
        string textPreview = fullText[..Math.Min(20, fullText.Length)];
        PdfLogger.Log(LogCategory.Text, $"TJ: Rendering '{textPreview}...' at ({CurrentState.GetTextPosition().X:F2}, {CurrentState.GetTextPosition().Y:F2})");

        // Show full text for Type0 fonts to debug extra character issue
        PdfFont? tjFont = _currentResources.GetFontObject(CurrentState.FontName);
        if (tjFont?.FontType == PdfFontType.Type0)
            PdfLogger.Log(LogCategory.Text, $"TJ-FULL: Type0 text ({fullText.Length} chars, {combinedCharCodes.Count} codes): '{fullText}'");

        // DIAGNOSTIC: Log the first few widths and check for zeros
        if (combinedWidths.Count > 0)
        {
            string widthsPreview = string.Join(", ", combinedWidths.Take(5).Select(w => $"{w:F4}"));
            PdfLogger.Log(LogCategory.Text, $"  Widths: [{widthsPreview}...] Total: {combinedWidths.Sum():F4}");

            if (combinedWidths.Take(5).All(w => w == 0))
                PdfLogger.Log(LogCategory.Text, $"  WARNING: ZERO WIDTHS DETECTED for font {CurrentState.FontName}");
        }

        _target.DrawText(combinedText.ToString(), combinedWidths, CurrentState, font, combinedCharCodes);

        // Advance text position by total width
        double totalAdvance = combinedWidths.Sum();
        CurrentState.AdvanceTextMatrix(totalAdvance, 0);
    }

    // ==================== XObject Rendering ====================

    protected override void OnInvokeXObject(string name)
    {
        PdfLogger.Log(LogCategory.Images, $"OnInvokeXObject: {name}");

        if (_currentResources == null)
        {
            PdfLogger.Log(LogCategory.Images, "  No resources");
            return;
        }

        PdfStream? xobject = _currentResources.GetXObject(name);
        if (xobject == null)
        {
            PdfLogger.Log(LogCategory.Images, "  XObject not found");
            return;
        }

        // Check if this XObject has Optional Content that's disabled
        // PDF spec: XObjects can have an /OC key that references an Optional Content Group
        // If the OCG is in the document's /OFF list, we shouldn't render it
        bool isDisabled = IsOptionalContentDisabled(xobject);
        PdfLogger.Log(LogCategory.Images, $"  Optional content disabled: {isDisabled}");

        if (isDisabled)
        {
            PdfLogger.Log(LogCategory.Images, $"  SKIPPING {name} - Optional Content is disabled");
            return;
        }

        // Check if this is an image XObject
        if (PdfImage.IsImageXObject(xobject))
        {
            PdfLogger.Log(LogCategory.Images, "  Type: Image XObject");
            try
            {
                var image = new PdfImage(xobject, _document);
                PdfLogger.Log(LogCategory.Images, $"  Image: {image.Width}x{image.Height}, ColorSpace={image.ColorSpace}");
                _target.DrawImage(image, CurrentState);
                PdfLogger.Log(LogCategory.Images, "  Image drawn successfully");
            }
            catch (Exception ex)
            {
                PdfLogger.Log(LogCategory.Images, $"  ERROR rendering image: {ex.Message}");
            }
        }
        // Handle Form XObjects (nested content streams)
        else if (IsFormXObject(xobject))
        {
            PdfLogger.Log(LogCategory.Graphics, "  Type: Form XObject");
            try
            {
                RenderFormXObject(xobject);
                PdfLogger.Log(LogCategory.Graphics, "  Form XObject rendered successfully");
            }
            catch (Exception ex)
            {
                PdfLogger.Log(LogCategory.Graphics, $"  ERROR rendering form XObject: {ex.Message}");
            }
        }
        else
        {
            PdfLogger.Log(LogCategory.Images, "  Type: Unknown/Unsupported XObject type");
        }
    }

    /// <summary>
    /// Handles inline images (BI/ID/EI operators)
    /// </summary>
    protected override void OnInlineImage(InlineImageOperator inlineImage)
    {
        PdfLogger.Log(LogCategory.Images, $"INLINE-IMAGE: {inlineImage.Width}x{inlineImage.Height}, ColorSpace={inlineImage.ColorSpace}, BPC={inlineImage.BitsPerComponent}, Filter={inlineImage.Filter ?? "none"}");

        try
        {
            // Create PdfImage from inline image operator
            var image = new PdfImage(inlineImage);
            PdfLogger.Log(LogCategory.Images, $"  Created PdfImage: {image.Width}x{image.Height}, ColorSpace={image.ColorSpace}");

            // Draw the image using the same mechanism as XObject images
            _target.DrawImage(image, CurrentState);
            PdfLogger.Log(LogCategory.Images, "  Inline image drawn successfully");
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Images, $"  ERROR rendering inline image: {ex.Message}");
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
        if (!stream.Dictionary.TryGetValue(new PdfName("Subtype"), out PdfObject obj))
            return false;

        return obj is PdfName { Value: "Form" };
    }

    /// <summary>
    /// Renders a Form XObject by recursively processing its content stream
    /// </summary>
    private void RenderFormXObject(PdfStream formStream)
    {
        PdfLogger.Log(LogCategory.Graphics, $"RenderFormXObject: Current CTM before form = [{CurrentState.Ctm.M11}, {CurrentState.Ctm.M12}, {CurrentState.Ctm.M21}, {CurrentState.Ctm.M22}, {CurrentState.Ctm.M31}, {CurrentState.Ctm.M32}]");

        // Get the Form XObject's content data
        byte[] contentData = formStream.GetDecodedData();

        // Get the Form's Resources dictionary (if any)
        // Form XObjects can have their own resources, or inherit from the page
        PdfResources? formResources = _resources;
        if (formStream.Dictionary.TryGetValue(new PdfName("Resources"), out PdfObject resourcesObj))
        {
            if (resourcesObj is PdfDictionary resourcesDict)
            {
                // Create a new resources object for the form
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
        var formRenderer = new PdfRenderer(_target, formResources ?? _resources, _optionalContentManager, _document)
            {
                CurrentState =
                {
                    // Set the form renderer's CTM to the saved CTM from the page
                    Ctm = savedCtm
                }
            };

        // TODO: If form has a /Matrix entry, concatenate it with the saved CTM
        // formCtm = formMatrix * savedCtm

        PdfLogger.Log(LogCategory.Graphics, $"RenderFormXObject: Form renderer CTM = [{formRenderer.CurrentState.Ctm.M11}, {formRenderer.CurrentState.Ctm.M12}, {formRenderer.CurrentState.Ctm.M21}, {formRenderer.CurrentState.Ctm.M22}, {formRenderer.CurrentState.Ctm.M31}, {formRenderer.CurrentState.Ctm.M32}]");

        // Parse and process the Form XObject's content stream
        List<PdfOperator> operators = PdfContentParser.Parse(contentData);
        formRenderer.ProcessOperators(operators);
    }

    // ==================== State Management ====================

    protected override void ProcessOperator(PdfOperator op)
    {
        switch (op)
        {
            // Handle save/restore with a render target
            case SaveGraphicsStateOperator:
                PdfLogger.Log(LogCategory.Transforms, $"q (SaveState): CTM=[{CurrentState.Ctm.M11:F4}, {CurrentState.Ctm.M12:F4}, {CurrentState.Ctm.M21:F4}, {CurrentState.Ctm.M22:F4}, {CurrentState.Ctm.M31:F4}, {CurrentState.Ctm.M32:F4}]");
                _target.SaveState();
                break;
            case RestoreGraphicsStateOperator:
                PdfLogger.Log(LogCategory.Transforms, $"Q (RestoreState) Before restore: CTM=[{CurrentState.Ctm.M11:F4}, {CurrentState.Ctm.M12:F4}, {CurrentState.Ctm.M21:F4}, {CurrentState.Ctm.M22:F4}, {CurrentState.Ctm.M31:F4}, {CurrentState.Ctm.M32:F4}]");
                _target.RestoreState();
                break;
        }

        // Call base implementation to update CurrentState
        base.ProcessOperator(op);

        if (op is not RestoreGraphicsStateOperator) return;
        PdfLogger.Log(LogCategory.Transforms, $"Q (RestoreState) After restore: CTM=[{CurrentState.Ctm.M11:F4}, {CurrentState.Ctm.M12:F4}, {CurrentState.Ctm.M21:F4}, {CurrentState.Ctm.M22:F4}, {CurrentState.Ctm.M31:F4}, {CurrentState.Ctm.M32:F4}]");
        // After restoring state, we need to update the canvas matrix to match
        _target.ApplyCtm(CurrentState.Ctm);
    }
}
