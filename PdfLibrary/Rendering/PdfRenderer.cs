using System.Diagnostics;
using System.Numerics;
using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Functions;
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
    /// <param name="scale">Scale factor for rendering (1.0 = 100%, 2.0 = 200%, etc.)</param>
    public void RenderPage(PdfPage page, int pageNumber = 1, double scale = 1.0)
    {
        var totalStopwatch = Stopwatch.StartNew();

        // Get page dimensions - CropBox defines the visible area, MediaBox is the full canvas
        // The output image should match CropBox dimensions
        PdfRectangle mediaBox = page.GetMediaBox();
        PdfRectangle cropBox = page.GetCropBox();

        // Use CropBox dimensions for the output image
        double width = cropBox.Width;
        double height = cropBox.Height;

        // CropBox offset relative to MediaBox origin - needed to translate coordinates
        // X1, Y1 are the lower-left corner coordinates
        double cropOffsetX = cropBox.X1;
        double cropOffsetY = cropBox.Y1;

        PdfLogger.Log(LogCategory.Transforms, $"RenderPage: MediaBox={mediaBox}, CropBox={cropBox}");
        PdfLogger.Log(LogCategory.Transforms, $"RenderPage: CropOffset=({cropOffsetX:F2}, {cropOffsetY:F2}), Scale={scale:F2}, OutputSize={width * scale:F0}x{height * scale:F0}");

        // Begin the page lifecycle - pass CropBox dimensions and offset
        _target.BeginPage(pageNumber, width, height, scale, cropOffsetX, cropOffsetY);

        Stopwatch? contentStopwatch = null;
        Stopwatch? annotationStopwatch = null;

        try
        {
            PdfResources? resources = page.GetResources();
            List<PdfStream> contents = page.GetContents();

            // Diagnostic: List available XObjects
            if (resources is not null)
            {
                List<string> xobjectNames = resources.GetXObjectNames();
                PdfLogger.Log(LogCategory.PdfTool, $"XObjects available: [{string.Join(", ", xobjectNames)}]");

                // Also list color spaces
                PdfDictionary? colorSpaces = resources.GetColorSpaces();
                if (colorSpaces is not null)
                {
                    List<string> csNames = colorSpaces.Keys.Select(k => k.Value).ToList();
                    PdfLogger.Log(LogCategory.PdfTool, $"ColorSpaces available: [{string.Join(", ", csNames)}]");
                }

                // Diagnostic: List available fonts
                List<string> fontNames = resources.GetFontNames();
                PdfLogger.Log(LogCategory.PdfTool, $"Page {pageNumber} Fonts available: [{string.Join(", ", fontNames)}]");
                PdfLogger.Log(LogCategory.PdfTool, $"Page {pageNumber} _currentResources has {(_currentResources is not null ? _currentResources.GetFontNames().Count : 0)} fonts");
            }

            PdfLogger.Log(LogCategory.PdfTool, $"Processing {contents.Count} content stream(s)");

            contentStopwatch = Stopwatch.StartNew();

            // Parse and process all content streams
            var streamIndex = 0;
            PdfLogger.Log(LogCategory.PdfTool, $"About to iterate {contents.Count} streams, contents type={contents.GetType().Name}");
            foreach (PdfStream stream in contents)
            {
                PdfLogger.Log(LogCategory.PdfTool, $"Processing stream {streamIndex}, stream type={stream?.GetType().Name ?? "null"}");
                byte[] decodedData;
                try
                {
                    decodedData = stream.GetDecodedData(_document?.Decryptor);
                    PdfLogger.Log(LogCategory.PdfTool, $"Decoded data length: {decodedData.Length}");
                }
                catch (Exception ex)
                {
                    PdfLogger.Log(LogCategory.PdfTool, $"EXCEPTION in GetDecodedData: {ex.GetType().Name}: {ex.Message}");
                    PdfLogger.Log(LogCategory.PdfTool, $"Stack trace: {ex.StackTrace}");
                    throw; // Re-throw to see full behavior
                }

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

            contentStopwatch.Stop();
            annotationStopwatch = Stopwatch.StartNew();

            // Render annotation appearances
            RenderAnnotations(page);

            annotationStopwatch.Stop();
        }
        finally
        {
            // Always end page, even if exception occurs
            _target.EndPage();

            totalStopwatch.Stop();
            long contentMs = contentStopwatch?.ElapsedMilliseconds ?? 0;
            long annotationMs = annotationStopwatch?.ElapsedMilliseconds ?? 0;
            PdfLogger.Log(LogCategory.Timings, $"Page {pageNumber} rendered in {totalStopwatch.ElapsedMilliseconds}ms (content: {contentMs}ms, annotations: {annotationMs}ms)");
        }
    }

    /// <summary>
    /// Renders annotation appearance streams on the page
    /// </summary>
    private void RenderAnnotations(PdfPage page)
    {
        PdfArray? annotations = page.GetAnnotations();
        if (annotations is null || annotations.Count == 0)
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

            if (annotDict is null)
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

            if (rectArray is null || rectArray.Count < 4)
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

            if (apDict is null)
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

            if (appearanceStream is null)
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
            byte[] decodedData = appearanceStream.GetDecodedData(_document?.Decryptor);
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

        // Apply clipping first if pending (W S or W* S sequence)
        if (PendingClip)
        {
            _target.SetClippingPath(_currentPath, CurrentState, PendingClipEvenOdd);
            ClearPendingClip();
        }

        List<double> color = CurrentState.StrokeColor;
        string colorStr = string.Join(",", color.Select(c => c.ToString("F2")));
        PdfLogger.Log(LogCategory.Graphics, $"PATH STROKE: ColorSpace={CurrentState.StrokeColorSpace}, Color=[{colorStr}], LineWidth={CurrentState.LineWidth}");
        _target.StrokePath(_currentPath, CurrentState);
        _currentPath.Clear();
    }

    protected override void OnFill(bool evenOdd)
    {
        if (_currentPath.IsEmpty) return;

        // Apply clipping first if pending (W f or W* f* sequence)
        if (PendingClip)
        {
            _target.SetClippingPath(_currentPath, CurrentState, PendingClipEvenOdd);
            ClearPendingClip();
        }

        List<double> color = CurrentState.FillColor;
        string colorStr = string.Join(",", color.Select(c => c.ToString("F2")));
        string resolvedColorStr = string.Join(",", CurrentState.ResolvedFillColor.Select(c => c.ToString("F2")));
        PdfLogger.Log(LogCategory.Graphics, $"PATH FILL: ColorSpace={CurrentState.FillColorSpace} -> {CurrentState.ResolvedFillColorSpace}, Color=[{colorStr}] -> [{resolvedColorStr}], PathEmpty={_currentPath.IsEmpty}");
        _target.FillPath(_currentPath, CurrentState, evenOdd);
        _currentPath.Clear();
    }

    protected override void OnFillAndStroke()
    {
        if (_currentPath.IsEmpty) return;

        // Apply clipping first if pending (W B or W* B* sequence)
        if (PendingClip)
        {
            _target.SetClippingPath(_currentPath, CurrentState, PendingClipEvenOdd);
            ClearPendingClip();
        }

        _target.FillAndStrokePath(_currentPath, CurrentState, evenOdd: false);
        _currentPath.Clear();
    }

    protected override void OnEndPath()
    {
        // End path without painting
        // Only set clipping path if W or W* operator was encountered before this
        if (!_currentPath.IsEmpty && PendingClip)
        {
            _target.SetClippingPath(_currentPath, CurrentState, PendingClipEvenOdd);
        }

        _currentPath.Clear();
        ClearPendingClip();
    }

    // ==================== Text Rendering ====================

    protected override void OnShowText(PdfString text)
    {
        if (_currentResources is null || CurrentState.FontName is null)
        {
            PdfLogger.Log(LogCategory.Text, $"TEXT-SKIPPED: _currentResources={_currentResources is not null}, FontName={CurrentState.FontName}");
            return;
        }

        // Get the font
        PdfFont? font = _currentResources.GetFontObject(CurrentState.FontName);
        if (font is null)
        {
            PdfLogger.Log(LogCategory.Text, $"TEXT-SKIPPED: Font '{CurrentState.FontName}' not found in _currentResources (has {_currentResources.GetFontNames().Count} fonts: {string.Join(", ", _currentResources.GetFontNames())})");
            return;
        }

        // Type3 fonts: Skip rendering (see OnShowTextWithPositioning for explanation)
        if (font.FontType == PdfFontType.Type3)
        {
            PdfLogger.Log(LogCategory.Text, $"Type3 font '{CurrentState.FontName}' - skipping visual rendering (glyphs rendered as vector paths)");
            AdvanceTextPositionForType3Simple(text, font);
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
        // We keep the original color space name and components in CurrentState
        // so subsequent color operators (scn) can work correctly with named color spaces.
        // The resolved values are stored in the Resolved* fields for rendering.

        string? fillCs = CurrentState.FillColorSpace;
        List<double>? fillColor = [..CurrentState.FillColor]; // Copy to avoid modifying original

        string beforeFillCs = fillCs ?? "null";
        var beforeFillColor = $"[{string.Join(", ", CurrentState.FillColor.Select(c => c.ToString("F3")))}]";

        ResolveColorSpace(ref fillCs, ref fillColor);

        string afterFillCs = fillCs ?? "null";
        string afterFillColor = fillColor is not null ? $"[{string.Join(", ", fillColor.Select(c => c.ToString("F3")))}]" : "null";

        if (beforeFillCs != afterFillCs || beforeFillColor != afterFillColor)
        {
            PdfLogger.Log(LogCategory.Graphics, $"OnColorChanged FILL: {beforeFillCs} {beforeFillColor} -> {afterFillCs} {afterFillColor}");
        }

        // Store resolved values for rendering, but keep original color space name
        CurrentState.ResolvedFillColorSpace = fillCs ?? string.Empty;
        CurrentState.ResolvedFillColor = fillColor ?? [];

        string? strokeCs = CurrentState.StrokeColorSpace;
        List<double>? strokeColor = [..CurrentState.StrokeColor]; // Copy to avoid modifying original
        ResolveColorSpace(ref strokeCs, ref strokeColor);
        CurrentState.ResolvedStrokeColorSpace = strokeCs ?? string.Empty;
        CurrentState.ResolvedStrokeColor = strokeColor ?? [];
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
        if (colorSpaces is null)
        {
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: No ColorSpace dict in resources for '{colorSpaceName}' - _currentResources is {(_currentResources is null ? "null" : "not null")}");
            return;
        }

        if (!colorSpaces.TryGetValue(new PdfName(colorSpaceName), out PdfObject? csObj))
        {
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: ColorSpace '{colorSpaceName}' not found in dict (has {colorSpaces.Keys.Count} entries: [{string.Join(", ", colorSpaces.Keys.Take(10).Select(k => k.Value))}])");
            return;
        }

        // Resolve indirect reference
        if (csObj is PdfIndirectReference reference && _document is not null)
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
                    if (streamObj is PdfIndirectReference streamRef && _document is not null)
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
                    if (alternateSpace is not null)
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
                // Get alternate color space (usually /DeviceRGB or /DeviceCMYK, or [/CalRGB ...], [/CalGray ...], etc.)
                case "Separation" when csArray.Count >= 4:
                {
                    // Get the colorant name (index 1)
                    string colorantName = csArray[1] is PdfName cn ? cn.Value : "Unknown";

                    // Get the alternate color space (index 2) - can be PdfName or PdfArray
                    PdfObject alternateObj = csArray[2];
                    if (alternateObj is PdfIndirectReference altRef && _document is not null)
                        alternateObj = _document.ResolveReference(altRef);

                    string altSpace;
                    int altComponents;

                    if (alternateObj is PdfName altName)
                    {
                        altSpace = altName.Value;
                        altComponents = altSpace switch
                        {
                            "DeviceGray" => 1,
                            "DeviceRGB" => 3,
                            "DeviceCMYK" => 4,
                            _ => 3
                        };
                    }
                    else if (alternateObj is PdfArray altArray && altArray.Count >= 1 && altArray[0] is PdfName altArrayType)
                    {
                        // Handle array-based alternate spaces like [/CalRGB <<...>>], [/CalGray <<...>>], [/Lab <<...>>]
                        altSpace = altArrayType.Value;
                        altComponents = altSpace switch
                        {
                            "CalGray" => 1,
                            "CalRGB" => 3,
                            "Lab" => 3,
                            "ICCBased" => 3, // Default, should read /N from stream
                            _ => 3
                        };
                    }
                    else
                    {
                        // Unknown alternate space format
                        break;
                    }

                    // Get the tint transform function (index 3)
                    PdfObject? tintTransformObj = csArray[3];
                    PdfFunction? tintTransform = _document is not null
                        ? PdfFunction.Create(tintTransformObj, _document)
                        : null;

                    PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Separation: colorant='{colorantName}', altSpace='{altSpace}', altComponents={altComponents}, tintTransform={tintTransform?.GetType().Name ?? "NULL"}, color.Count={color.Count}");

                    if (color.Count == 1)
                    {
                        double tint = color[0];

                        // Try to use the tint transform function
                        if (tintTransform is not null)
                        {
                            double[] result = tintTransform.Evaluate([tint]);
                            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Separation '{colorantName}' -> tint={tint:F3} -> function result=[{string.Join(", ", result.Select(r => r.ToString("F3")))}]");

                            if (result.Length >= 3)
                            {
                                // RGB output from tint transform
                                color = [result[0], result[1], result[2]];
                                colorSpaceName = altSpace is "CalRGB" ? "DeviceRGB" : altSpace;
                                if (colorSpaceName != "DeviceRGB" && colorSpaceName != "DeviceCMYK" && colorSpaceName != "DeviceGray")
                                    colorSpaceName = "DeviceRGB";
                            }
                            else if (result.Length == 1)
                            {
                                // Grayscale output
                                color = [result[0]];
                                colorSpaceName = "DeviceGray";
                            }
                            else if (result.Length == 4)
                            {
                                // CMYK output
                                color = [result[0], result[1], result[2], result[3]];
                                colorSpaceName = "DeviceCMYK";
                            }
                        }
                        else
                        {
                            // Fallback: simple heuristic when tint transform not available
                            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Separation '{colorantName}' -> using fallback (no tint transform), tint={tint:F3}");

                            // Handle special colorant names
                            if (colorantName == "Black" || colorantName == "All" || colorantName == "None")
                            {
                                // For Black/All separations: tint=1 means black, tint=0 means white
                                double value = 1.0 - tint;
                                color = [value, value, value];
                                colorSpaceName = "DeviceRGB";
                            }
                            else if (altSpace is "DeviceRGB" or "CalRGB")
                            {
                                double value = 1.0 - tint;
                                color = [value, value, value];
                                colorSpaceName = "DeviceRGB";
                            }
                            else if (altSpace is "DeviceGray" or "CalGray")
                            {
                                double value = 1.0 - tint;
                                color = [value];
                                colorSpaceName = "DeviceGray";
                            }
                            else if (altSpace == "DeviceCMYK")
                            {
                                color = [0.0, 0.0, 0.0, tint];
                                colorSpaceName = "DeviceCMYK";
                            }
                            else
                            {
                                double value = 1.0 - tint;
                                color = [value, value, value];
                                colorSpaceName = "DeviceRGB";
                            }
                        }
                    }

                    PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Separation END: colorSpaceName='{colorSpaceName}', color=[{string.Join(", ", color.Select(c => c.ToString("F3")))}]");
                    break;
                }
            }
        }
    }

    // ==================== Graphics State Parameter Dictionary ====================

    /// <summary>
    /// Handles the gs operator - sets graphics state from ExtGState dictionary
    /// </summary>
    protected override void OnSetGraphicsState(string dictName)
    {
        PdfLogger.Log(LogCategory.Graphics, $"gs operator: {dictName}");

        if (_currentResources is null)
        {
            PdfLogger.Log(LogCategory.Graphics, $"  No resources for ExtGState lookup");
            return;
        }

        PdfDictionary? extGState = _currentResources.GetExtGState(dictName);
        if (extGState is null)
        {
            PdfLogger.Log(LogCategory.Graphics, $"  ExtGState '{dictName}' not found");
            return;
        }

        // Apply all ExtGState parameters to the current graphics state
        ApplyExtGState(extGState);
    }

    /// <summary>
    /// Applies ExtGState dictionary parameters to the current graphics state
    /// ISO 32000-1:2008 Table 58 - Entries in a graphics state parameter dictionary
    /// </summary>
    private void ApplyExtGState(PdfDictionary extGState)
    {
        foreach (KeyValuePair<PdfName, PdfObject> entry in extGState)
        {
            string key = entry.Key.Value;
            PdfObject value = entry.Value;

            // Resolve indirect references
            if (value is PdfIndirectReference reference && _document is not null)
                value = _document.ResolveReference(reference);

            switch (key)
            {
                case "Type":
                    // Ignore - just identifies this as an ExtGState dictionary
                    break;

                // Line width (LW)
                case "LW":
                    if (GetNumber(value) is double lw)
                    {
                        CurrentState.LineWidth = lw;
                        PdfLogger.Log(LogCategory.Graphics, $"  LW (LineWidth) = {lw}");
                    }
                    break;

                // Line cap (LC)
                case "LC":
                    if (value is PdfInteger lc)
                    {
                        CurrentState.LineCap = lc.Value;
                        PdfLogger.Log(LogCategory.Graphics, $"  LC (LineCap) = {lc.Value}");
                    }
                    break;

                // Line join (LJ)
                case "LJ":
                    if (value is PdfInteger lj)
                    {
                        CurrentState.LineJoin = lj.Value;
                        PdfLogger.Log(LogCategory.Graphics, $"  LJ (LineJoin) = {lj.Value}");
                    }
                    break;

                // Miter limit (ML)
                case "ML":
                    if (GetNumber(value) is double ml)
                    {
                        CurrentState.MiterLimit = ml;
                        PdfLogger.Log(LogCategory.Graphics, $"  ML (MiterLimit) = {ml}");
                    }
                    break;

                // Dash pattern (D)
                case "D":
                    if (value is PdfArray dashArray && dashArray.Count >= 2)
                    {
                        if (dashArray[0] is PdfArray pattern)
                        {
                            var dashPattern = new double[pattern.Count];
                            for (var i = 0; i < pattern.Count; i++)
                            {
                                dashPattern[i] = GetNumber(pattern[i]) ?? 0;
                            }
                            CurrentState.DashPattern = dashPattern.Length > 0 ? dashPattern : null;
                        }
                        CurrentState.DashPhase = GetNumber(dashArray[1]) ?? 0;
                        PdfLogger.Log(LogCategory.Graphics, $"  D (DashPattern) = [{string.Join(", ", CurrentState.DashPattern ?? [])}] {CurrentState.DashPhase}");
                    }
                    break;

                // Rendering intent (RI)
                case "RI":
                    if (value is PdfName ri)
                    {
                        // Store rendering intent if needed
                        PdfLogger.Log(LogCategory.Graphics, $"  RI (RenderingIntent) = {ri.Value}");
                    }
                    break;

                // Overprint for stroking (OP)
                case "OP":
                    if (value is PdfBoolean op)
                    {
                        CurrentState.StrokeOverprint = op.Value;
                        PdfLogger.Log(LogCategory.Graphics, $"  OP (StrokeOverprint) = {op.Value}");
                    }
                    break;

                // Overprint for non-stroking (op)
                case "op":
                    if (value is PdfBoolean opFill)
                    {
                        CurrentState.FillOverprint = opFill.Value;
                        PdfLogger.Log(LogCategory.Graphics, $"  op (FillOverprint) = {opFill.Value}");
                    }
                    break;

                // Overprint mode (OPM)
                case "OPM":
                    if (value is PdfInteger opm)
                    {
                        CurrentState.OverprintMode = opm.Value;
                        PdfLogger.Log(LogCategory.Graphics, $"  OPM (OverprintMode) = {opm.Value}");
                    }
                    break;

                // Font (Font) - array of [fontRef size]
                case "Font":
                    if (value is PdfArray fontArray && fontArray.Count >= 2)
                    {
                        // Get font reference and size
                        PdfObject fontRef = fontArray[0];
                        if (fontRef is PdfIndirectReference fRef && _document is not null)
                            fontRef = _document.ResolveReference(fRef);

                        double? fontSize = GetNumber(fontArray[1]);
                        if (fontSize.HasValue)
                        {
                            CurrentState.FontSize = fontSize.Value;
                            PdfLogger.Log(LogCategory.Graphics, $"  Font size = {fontSize}");
                        }
                        // Note: Font name would need to be resolved from the font dictionary
                    }
                    break;

                // Black generation (BG, BG2)
                case "BG":
                case "BG2":
                    PdfLogger.Log(LogCategory.Graphics, $"  {key} (BlackGeneration) - not implemented");
                    break;

                // Undercolor removal (UCR, UCR2)
                case "UCR":
                case "UCR2":
                    PdfLogger.Log(LogCategory.Graphics, $"  {key} (UndercolorRemoval) - not implemented");
                    break;

                // Transfer function (TR, TR2)
                case "TR":
                case "TR2":
                    PdfLogger.Log(LogCategory.Graphics, $"  {key} (TransferFunction) - not implemented");
                    break;

                // Halftone (HT)
                case "HT":
                    PdfLogger.Log(LogCategory.Graphics, $"  HT (Halftone) - not implemented");
                    break;

                // Flatness tolerance (FL)
                case "FL":
                    if (GetNumber(value) is double fl)
                    {
                        CurrentState.Flatness = fl;
                        PdfLogger.Log(LogCategory.Graphics, $"  FL (Flatness) = {fl}");
                    }
                    break;

                // Smoothness tolerance (SM)
                case "SM":
                    if (GetNumber(value) is double sm)
                    {
                        CurrentState.Smoothness = sm;
                        PdfLogger.Log(LogCategory.Graphics, $"  SM (Smoothness) = {sm}");
                    }
                    break;

                // Stroke adjustment (SA)
                case "SA":
                    if (value is PdfBoolean sa)
                    {
                        PdfLogger.Log(LogCategory.Graphics, $"  SA (StrokeAdjustment) = {sa.Value}");
                    }
                    break;

                // Blend mode (BM)
                case "BM":
                    string blendMode = value switch
                    {
                        PdfName bmName => bmName.Value,
                        PdfArray bmArray when bmArray.Count > 0 && bmArray[0] is PdfName firstName => firstName.Value,
                        _ => "Normal"
                    };
                    CurrentState.BlendMode = blendMode;
                    PdfLogger.Log(LogCategory.Graphics, $"  BM (BlendMode) = {blendMode}");
                    break;

                // Soft mask (SMask)
                case "SMask":
                    ApplySoftMask(value);
                    break;

                // Stroking alpha (CA)
                case "CA":
                    if (GetNumber(value) is double ca)
                    {
                        CurrentState.StrokeAlpha = ca;
                        PdfLogger.Log(LogCategory.Graphics, $"  CA (StrokeAlpha) = {ca}");
                    }
                    break;

                // Non-stroking alpha (ca)
                case "ca":
                    if (GetNumber(value) is double caFill)
                    {
                        CurrentState.FillAlpha = caFill;
                        PdfLogger.Log(LogCategory.Graphics, $"  ca (FillAlpha) = {caFill}");
                    }
                    break;

                // Alpha is shape (AIS)
                case "AIS":
                    if (value is PdfBoolean ais)
                    {
                        CurrentState.AlphaIsShape = ais.Value;
                        PdfLogger.Log(LogCategory.Graphics, $"  AIS (AlphaIsShape) = {ais.Value}");
                    }
                    break;

                // Text knockout (TK)
                case "TK":
                    if (value is PdfBoolean tk)
                    {
                        CurrentState.TextKnockout = tk.Value;
                        PdfLogger.Log(LogCategory.Graphics, $"  TK (TextKnockout) = {tk.Value}");
                    }
                    break;

                default:
                    PdfLogger.Log(LogCategory.Graphics, $"  Unknown ExtGState key: {key}");
                    break;
            }
        }

        // Notify render target of graphics state change
        _target.OnGraphicsStateChanged(CurrentState);
    }

    /// <summary>
    /// Applies a soft mask from the SMask entry
    /// </summary>
    private void ApplySoftMask(PdfObject value)
    {
        if (value is PdfName nameValue)
        {
            if (nameValue.Value == "None")
            {
                // Clear the soft mask
                CurrentState.SoftMask = null;
                PdfLogger.Log(LogCategory.Graphics, "  SMask = None (cleared)");
            }
            return;
        }

        if (value is not PdfDictionary smaskDict)
        {
            PdfLogger.Log(LogCategory.Graphics, $"  SMask: unexpected type {value.GetType().Name}");
            return;
        }

        // Parse soft mask dictionary
        var softMask = new PdfSoftMask();

        // Get subtype (S) - "Alpha" or "Luminosity"
        if (smaskDict.TryGetValue(new PdfName("S"), out PdfObject sObj) && sObj is PdfName subtype)
        {
            softMask = softMask with { Subtype = subtype.Value };
        }

        // Get transparency group (G) - Form XObject
        if (smaskDict.TryGetValue(new PdfName("G"), out PdfObject gObj))
        {
            PdfStream? groupStream = gObj switch
            {
                PdfStream stream => stream,
                PdfIndirectReference gRef when _document is not null => _document.ResolveReference(gRef) as PdfStream,
                _ => null
            };
            softMask = softMask with { Group = groupStream };
        }

        // Get backdrop color (BC)
        if (smaskDict.TryGetValue(new PdfName("BC"), out PdfObject bcObj) && bcObj is PdfArray bcArray)
        {
            var backdropColor = new double[bcArray.Count];
            for (var i = 0; i < bcArray.Count; i++)
            {
                backdropColor[i] = GetNumber(bcArray[i]) ?? 0;
            }
            softMask = softMask with { BackdropColor = backdropColor };
        }

        // Get transfer function (TR)
        if (smaskDict.TryGetValue(new PdfName("TR"), out PdfObject trObj))
        {
            softMask = softMask with { TransferFunction = trObj };
        }

        CurrentState.SoftMask = softMask;
        PdfLogger.Log(LogCategory.Graphics, $"  SMask: Subtype={softMask.Subtype}, HasGroup={softMask.Group is not null}");

        // If we have a Group, render the mask using the render target's soft mask support
        if (softMask.Group is not null)
        {
            RenderSoftMaskGroup(softMask);
        }
    }

    /// <summary>
    /// Renders the SMask Group XObject using the render target's soft mask support.
    /// </summary>
    private void RenderSoftMaskGroup(PdfSoftMask softMask)
    {
        if (softMask.Group is null)
            return;

        // Get the form's resources
        PdfResources? formResources = _resources;
        if (softMask.Group.Dictionary.TryGetValue(new PdfName("Resources"), out PdfObject resourcesObj))
        {
            if (resourcesObj is PdfIndirectReference resRef && _document is not null)
                resourcesObj = _document.ResolveReference(resRef);
            if (resourcesObj is PdfDictionary resourcesDict)
                formResources = new PdfResources(resourcesDict);
        }

        // Check for Matrix entry in the form XObject
        Matrix3x2 formMatrix = Matrix3x2.Identity;
        if (softMask.Group.Dictionary.TryGetValue(new PdfName("Matrix"), out PdfObject matrixObj) &&
            matrixObj is PdfArray matrixArray && matrixArray.Count >= 6)
        {
            double a = GetNumber(matrixArray[0]) ?? 1;
            double b = GetNumber(matrixArray[1]) ?? 0;
            double c = GetNumber(matrixArray[2]) ?? 0;
            double d = GetNumber(matrixArray[3]) ?? 1;
            double e = GetNumber(matrixArray[4]) ?? 0;
            double f = GetNumber(matrixArray[5]) ?? 0;
            formMatrix = new Matrix3x2((float)a, (float)b, (float)c, (float)d, (float)e, (float)f);
        }

        // Parse the content operators ahead of time
        byte[] contentData = softMask.Group.GetDecodedData(_document?.Decryptor);
        List<PdfOperator> operators = PdfContentParser.Parse(contentData);

        // Use the render target's soft mask rendering method with a callback
        _target.RenderSoftMask(softMask.Subtype, maskTarget =>
        {
            // Create a renderer for the mask content
            var maskRenderer = new PdfRenderer(maskTarget, formResources ?? _resources, _optionalContentManager, _document)
            {
                CurrentState =
                {
                    // Start with identity CTM - the mask is in its own coordinate space
                    Ctm = Matrix3x2.Identity
                }
            };

            // Apply form matrix if present
            if (formMatrix != Matrix3x2.Identity)
            {
                maskRenderer.CurrentState.ConcatenateMatrix(
                    formMatrix.M11, formMatrix.M12,
                    formMatrix.M21, formMatrix.M22,
                    formMatrix.M31, formMatrix.M32);
            }

            // Render the mask content
            maskRenderer.ProcessOperators(operators);
        });
    }

    /// <summary>
    /// Helper to extract a number from a PDF object
    /// </summary>
    private static double? GetNumber(PdfObject obj)
    {
        return obj switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => null
        };
    }

    protected override void OnShowTextWithPositioning(PdfArray array)
    {
        // TJ operator: combine all strings and adjustments into a single DrawText call
        if (_currentResources is null || CurrentState.FontName is null)
            return;

        PdfFont? font = _currentResources.GetFontObject(CurrentState.FontName);
        if (font is null)
        {
            PdfLogger.Log(LogCategory.Text, $"Font '{CurrentState.FontName}' NOT FOUND");
            return;
        }

        PdfLogger.Log(LogCategory.Text, $"Using font '{CurrentState.FontName}' Type={font.FontType} BaseFont={font.BaseFont}");

        // Type3 fonts: Skip rendering. Type3 font glyphs are defined as CharProc content streams.
        // In this PDF library, the visual glyph shapes are typically rendered as separate vector paths
        // in the main content stream (not via CharProc execution). The TJ/Tj operators with Type3 fonts
        // are primarily for text extraction/selection. If we try to render, we'd fall back to Arial
        // (since Type3 fonts have no embedded glyph outlines), causing double rendering/ghosting.
        // TODO: Implement proper Type3 CharProc execution for PDFs that rely on it for visual rendering.
        if (font.FontType == PdfFontType.Type3)
        {
            PdfLogger.Log(LogCategory.Text, $"Type3 font '{CurrentState.FontName}' - skipping visual rendering (glyphs rendered as vector paths)");
            // Still need to advance text position for proper layout of subsequent text
            AdvanceTextPositionForType3(array, font);
            return;
        }

        bool isType0 = font.FontType == PdfFontType.Type0;
        var combinedText = new StringBuilder();
        var combinedWidths = new List<double>();
        var combinedCharCodes = new List<int>();
        double leadingAdjustment = 0; // Track leading adjustment before first character

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
                    double adjustment = -intVal.Value / 1000.0 * CurrentState.FontSize;
                    adjustment *= CurrentState.HorizontalScaling / 100.0;

                    if (combinedWidths.Count > 0)
                    {
                        double oldWidth = combinedWidths[^1];
                        combinedWidths[^1] += adjustment;
                        PdfLogger.Log(LogCategory.Text, $"  TJ-ADJ: int={intVal.Value} -> adjustment={adjustment:F6}, prevWidth={oldWidth:F6} -> newWidth={combinedWidths[^1]:F6}");
                    }
                    else
                    {
                        // Leading adjustment before first character - accumulate it
                        leadingAdjustment += adjustment;
                        PdfLogger.Log(LogCategory.Text, $"  TJ-ADJ: int={intVal.Value} -> leading adjustment={adjustment:F6}, total leading={leadingAdjustment:F6}");
                    }
                    break;
                }
                case PdfReal realVal:
                {
                    double adjustment = -realVal.Value / 1000.0 * CurrentState.FontSize;
                    adjustment *= CurrentState.HorizontalScaling / 100.0;

                    if (combinedWidths.Count > 0)
                    {
                        double oldWidth = combinedWidths[^1];
                        combinedWidths[^1] += adjustment;
                        PdfLogger.Log(LogCategory.Text, $"  TJ-ADJ: real={realVal.Value:F4} -> adjustment={adjustment:F6}, prevWidth={oldWidth:F6} -> newWidth={combinedWidths[^1]:F6}");
                    }
                    else
                    {
                        // Leading adjustment before first character - accumulate it
                        leadingAdjustment += adjustment;
                        PdfLogger.Log(LogCategory.Text, $"  TJ-ADJ: real={realVal.Value:F4} -> leading adjustment={adjustment:F6}, total leading={leadingAdjustment:F6}");
                    }
                    break;
                }
            }
        }

        // Apply leading adjustment by advancing the text matrix before rendering
        if (leadingAdjustment != 0)
        {
            CurrentState.AdvanceTextMatrix(leadingAdjustment, 0);
            PdfLogger.Log(LogCategory.Text, $"  Applied leading adjustment: {leadingAdjustment:F6}");
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
            double totalWidth = combinedWidths.Sum();
            PdfLogger.Log(LogCategory.Text, $"  Widths: [{widthsPreview}...] Total: {totalWidth:F4}");

            // Log the last 3 widths for page number alignment debugging
            if (combinedWidths.Count >= 3)
            {
                List<double> lastWidths = combinedWidths.Skip(combinedWidths.Count - 3).ToList();
                string lastChars = fullText.Length >= 3 ? fullText.Substring(fullText.Length - 3) : fullText;
                PdfLogger.Log(LogCategory.Text, $"  LastWidths: '{lastChars}' -> [{string.Join(", ", lastWidths.Select(w => $"{w:F4}"))}]");
            }

            // Calculate final X position in user space
            double startX = CurrentState.TextMatrix.M31;
            double scaleFactor = Math.Sqrt(CurrentState.TextMatrix.M11 * CurrentState.TextMatrix.M11 + CurrentState.TextMatrix.M12 * CurrentState.TextMatrix.M12);
            double finalX = startX + totalWidth * scaleFactor;
            PdfLogger.Log(LogCategory.Text, $"  Position: startX={startX:F2}, totalWidth={totalWidth:F4}, scale={scaleFactor:F2} -> finalX={finalX:F2}");

            if (combinedWidths.Take(5).All(w => w == 0))
                PdfLogger.Log(LogCategory.Text, $"  WARNING: ZERO WIDTHS DETECTED for font {CurrentState.FontName}");
        }

        _target.DrawText(combinedText.ToString(), combinedWidths, CurrentState, font, combinedCharCodes);

        // Advance text position by total width
        double totalAdvance = combinedWidths.Sum();
        CurrentState.AdvanceTextMatrix(totalAdvance, 0);
    }

    /// <summary>
    /// Advances the text position for Type3 fonts without rendering (for Tj operator).
    /// </summary>
    private void AdvanceTextPositionForType3Simple(PdfString text, PdfFont font)
    {
        double totalAdvance = 0;
        byte[] bytes = text.Bytes;

        for (var i = 0; i < bytes.Length; i++)
        {
            int charCode = bytes[i];
            double glyphWidth = font.GetCharacterWidth(charCode);
            double advance = glyphWidth * CurrentState.FontSize / 1000.0;
            advance *= CurrentState.HorizontalScaling / 100.0;

            if (CurrentState.CharacterSpacing != 0)
                advance += CurrentState.CharacterSpacing;

            string decoded = font.DecodeCharacter(charCode);
            if (decoded == " " && CurrentState.WordSpacing != 0)
                advance += CurrentState.WordSpacing;

            totalAdvance += advance;
        }

        CurrentState.AdvanceTextMatrix(totalAdvance, 0);
    }

    /// <summary>
    /// Advances the text position for Type3 fonts without rendering (for TJ operator).
    /// Type3 fonts have their glyphs rendered as vector paths, but we still need
    /// to advance the text position properly for subsequent text operations.
    /// </summary>
    private void AdvanceTextPositionForType3(PdfArray array, PdfFont font)
    {
        double totalAdvance = 0;

        foreach (PdfObject item in array)
        {
            switch (item)
            {
                case PdfString str:
                {
                    byte[] bytes = str.Bytes;
                    for (var i = 0; i < bytes.Length; i++)
                    {
                        int charCode = bytes[i];
                        double glyphWidth = font.GetCharacterWidth(charCode);
                        double advance = glyphWidth * CurrentState.FontSize / 1000.0;
                        advance *= CurrentState.HorizontalScaling / 100.0;

                        if (CurrentState.CharacterSpacing != 0)
                            advance += CurrentState.CharacterSpacing;

                        string decoded = font.DecodeCharacter(charCode);
                        if (decoded == " " && CurrentState.WordSpacing != 0)
                            advance += CurrentState.WordSpacing;

                        totalAdvance += advance;
                    }
                    break;
                }
                case PdfInteger intVal:
                {
                    // Kerning adjustment in thousandths of text space
                    double adjustment = -intVal.Value / 1000.0 * CurrentState.FontSize;
                    adjustment *= CurrentState.HorizontalScaling / 100.0;
                    totalAdvance += adjustment;
                    break;
                }
                case PdfReal realVal:
                {
                    double adjustment = -realVal.Value / 1000.0 * CurrentState.FontSize;
                    adjustment *= CurrentState.HorizontalScaling / 100.0;
                    totalAdvance += adjustment;
                    break;
                }
            }
        }

        CurrentState.AdvanceTextMatrix(totalAdvance, 0);
    }

    // ==================== XObject Rendering ====================

    protected override void OnInvokeXObject(string name)
    {
        PdfLogger.Log(LogCategory.Images, $"OnInvokeXObject: {name}");

        if (_currentResources is null)
        {
            PdfLogger.Log(LogCategory.Images, "  No resources");
            return;
        }

        PdfStream? xobject = _currentResources.GetXObject(name);
        if (xobject is null)
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
                var imageStopwatch = Stopwatch.StartNew();
                var image = new PdfImage(xobject, _document);
                long createMs = imageStopwatch.ElapsedMilliseconds;

                PdfLogger.Log(LogCategory.Images, $"  Image: {image.Width}x{image.Height}, ColorSpace={image.ColorSpace}");
                _target.DrawImage(image, CurrentState);
                imageStopwatch.Stop();

                PdfLogger.Log(LogCategory.Timings, $"Image '{name}' ({image.Width}x{image.Height}, {image.ColorSpace}): {imageStopwatch.ElapsedMilliseconds}ms (create: {createMs}ms, draw: {imageStopwatch.ElapsedMilliseconds - createMs}ms)");
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
        if (_optionalContentManager is null)
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
        byte[] contentData = formStream.GetDecodedData(_document?.Decryptor);

        // Get the Form's Resources dictionary (if any)
        // Form XObjects can have their own resources, or inherit from the page
        PdfResources? formResources = _resources;
        if (formStream.Dictionary.TryGetValue(new PdfName("Resources"), out PdfObject resourcesObj))
        {
            // Resolve indirect reference to Resources dictionary if needed
            if (resourcesObj is PdfIndirectReference resourcesRef && _document is not null)
                resourcesObj = _document.ResolveReference(resourcesRef);

            if (resourcesObj is PdfDictionary resourcesDict)
            {
                // Create a new resources object for the form, passing the document
                // so that indirect references to fonts, XObjects, etc. can be resolved
                formResources = new PdfResources(resourcesDict, _document);
            }
        }

        // According to PDF spec (ISO 32000-1 section 8.10):
        // Form XObjects execute with a fresh graphics state, BUT the CTM from the
        // invoking context is inherited (and concatenated with the form's Matrix if present)

        // Save the current CTM to apply to the form's coordinate space
        Matrix3x2 savedCtm = CurrentState.Ctm;

        // Check for Form XObject Matrix and concatenate with saved CTM
        // The Matrix entry maps form space to user space
        Matrix3x2 formCtm = savedCtm;
        if (formStream.Dictionary.TryGetValue(new PdfName("Matrix"), out PdfObject? matrixObj) && matrixObj is PdfArray matrixArray && matrixArray.Count >= 6)
        {
            var m11 = (float)(GetNumber(matrixArray[0]) ?? 0);
            var m12 = (float)(GetNumber(matrixArray[1]) ?? 0);
            var m21 = (float)(GetNumber(matrixArray[2]) ?? 0);
            var m22 = (float)(GetNumber(matrixArray[3]) ?? 0);
            var m31 = (float)(GetNumber(matrixArray[4]) ?? 0);
            var m32 = (float)(GetNumber(matrixArray[5]) ?? 0);
            var formMatrix = new Matrix3x2(m11, m12, m21, m22, m31, m32);

            // Concatenate: formCtm = formMatrix * savedCtm
            // This transforms form coordinates through the form matrix, then through the page CTM
            formCtm = formMatrix * savedCtm;

            PdfLogger.Log(LogCategory.Graphics, $"RenderFormXObject: Form Matrix = [{m11}, {m12}, {m21}, {m22}, {m31}, {m32}]");
        }

        // Create a new renderer for the form to ensure it starts with a fresh graphics state
        var formRenderer = new PdfRenderer(_target, formResources ?? _resources, _optionalContentManager, _document)
            {
                CurrentState =
                {
                    // Set the form renderer's CTM to the concatenated matrix
                    Ctm = formCtm
                }
            };

        PdfLogger.Log(LogCategory.Graphics, $"RenderFormXObject: Form renderer CTM = [{formRenderer.CurrentState.Ctm.M11}, {formRenderer.CurrentState.Ctm.M12}, {formRenderer.CurrentState.Ctm.M21}, {formRenderer.CurrentState.Ctm.M22}, {formRenderer.CurrentState.Ctm.M31}, {formRenderer.CurrentState.Ctm.M32}]");

        // Save render target state before processing form
        _target.SaveState();

        // Apply the form's CTM to the render target
        // This is needed because we set CurrentState.Ctm directly, which doesn't trigger OnMatrixChanged
        _target.ApplyCtm(formCtm);

        // Parse and process the Form XObject's content stream
        List<PdfOperator> operators = PdfContentParser.Parse(contentData);
        formRenderer.ProcessOperators(operators);

        // Restore render target state after form processing
        _target.RestoreState();
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
