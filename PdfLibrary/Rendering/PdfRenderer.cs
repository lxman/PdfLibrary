using System.Diagnostics;
using System.Numerics;
using System.Text;
using Logging;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fixups;
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
    private PdfResources? _currentResources; // Can be swapped for annotation resources
    private readonly IPathBuilder _currentPath;
    private readonly OptionalContentManager? _optionalContentManager;
    private readonly PdfDocument? _document;
    private readonly ColorSpaceResolver _colorSpaceResolver;
    private readonly ExtGStateApplier _extGStateApplier;
    private readonly FixupManager? _fixupManager;

    /// <summary>
    /// Creates a new PDF renderer
    /// </summary>
    /// <param name="target">The rendering target (WPF, Skia, etc.)</param>
    /// <param name="resources">Page resources for fonts, images, etc.</param>
    /// <param name="optionalContentManager">Optional content manager for layer visibility</param>
    /// <param name="document">The PDF document (for resolving indirect references in images)</param>
    /// <param name="fixupManager">Optional fixup manager for handling edge cases</param>
    internal PdfRenderer(IRenderTarget target, PdfResources? resources = null, OptionalContentManager? optionalContentManager = null, PdfDocument? document = null, FixupManager? fixupManager = null)
    {
        PdfLogger.Log(LogCategory.Text, $"[RENDERER-CTOR] PdfRenderer constructor called: fixupManager!=null={fixupManager != null}");

        _target = target ?? throw new ArgumentNullException(nameof(target));
        _resources = resources;
        _currentResources = resources; // Initially use page resources
        _currentPath = new PathBuilder();
        _optionalContentManager = optionalContentManager;
        _document = document;
        _colorSpaceResolver = new ColorSpaceResolver(document);
        _extGStateApplier = new ExtGStateApplier(document, target)
        {
            RenderSoftMaskGroupCallback = RenderSoftMaskGroup
        };
        _fixupManager = fixupManager;

        PdfLogger.Log(LogCategory.Text, $"[RENDERER-CTOR] _fixupManager assigned: _fixupManager!=null={_fixupManager != null}");
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

        // Get page rotation (0, 90, 180, or 270 degrees clockwise)
        int rotation = page.Rotate;

        PdfLogger.Log(LogCategory.Transforms, $"RenderPage: MediaBox={mediaBox}, CropBox={cropBox}, Rotation={rotation}°");
        PdfLogger.Log(LogCategory.Transforms, $"RenderPage: CropOffset=({cropOffsetX:F2}, {cropOffsetY:F2}), Scale={scale:F2}, OutputSize={width * scale:F0}x{height * scale:F0}");

        // Begin the page lifecycle - pass CropBox dimensions, offset, and rotation
        _target.BeginPage(pageNumber, width, height, scale, cropOffsetX, cropOffsetY, rotation);

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
            foreach (var stream in contents)
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

                // Skip Widget annotations for now - they often have opaque backgrounds
                // that cover page content. TODO: Handle these properly with transparency/blend modes.
                if (subtypeObj is PdfName subtypeName && subtypeName.Value == "Widget")
                {
                    PdfLogger.Log(LogCategory.Graphics, "Skipping Widget annotation (form field background)");
                    continue;
                }
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
        PdfLogger.Log(LogCategory.Graphics, $"PATH FILL: ColorSpace={CurrentState.FillColorSpace} -> {CurrentState.ResolvedFillColorSpace}, Color=[{colorStr}] -> [{resolvedColorStr}], Pattern={CurrentState.FillPatternName}, PathEmpty={_currentPath.IsEmpty}");

        // Check if we should use pattern fill
        if (CurrentState.ResolvedFillColorSpace == "Pattern" && CurrentState.FillPatternName is not null)
        {
            FillWithPattern(_currentPath, evenOdd, CurrentState.FillPatternName);
        }
        else
        {
            _target.FillPath(_currentPath, CurrentState, evenOdd);
        }
        _currentPath.Clear();
    }

    /// <summary>
    /// Fills a path using a tiling pattern
    /// </summary>
    private void FillWithPattern(IPathBuilder path, bool evenOdd, string patternName)
    {
        if (_currentResources is null || _document is null)
        {
            PdfLogger.Log(LogCategory.Graphics, $"PATTERN FILL: No resources or document for pattern '{patternName}'");
            return;
        }

        // Get the pattern stream from resources
        PdfStream? patternStream = _currentResources.GetPattern(patternName);
        if (patternStream is null)
        {
            PdfLogger.Log(LogCategory.Graphics, $"PATTERN FILL: Pattern '{patternName}' not found in resources");
            return;
        }

        // Create pattern object from dictionary, including the content stream
        PdfTilingPattern? pattern = PdfTilingPattern.FromDictionary(patternStream.Dictionary, _document, patternStream);
        if (pattern is null)
        {
            PdfLogger.Log(LogCategory.Graphics, $"PATTERN FILL: Failed to parse pattern '{patternName}' (may not be a tiling pattern)");
            return;
        }

        PdfLogger.Log(LogCategory.Graphics, $"PATTERN FILL: Using pattern '{patternName}': PaintType={pattern.PaintType}, TilingType={pattern.TilingType}, BBox={pattern.BBox}, XStep={pattern.XStep}, YStep={pattern.YStep}");

        // Create callback to render pattern content
        void RenderPatternContent(IRenderTarget target)
        {
            if (pattern.ContentStream is null) return;

            // Get pattern's resources (or fall back to page resources)
            PdfResources? patternResources = pattern.Resources ?? _currentResources;

            // Decode pattern content stream
            byte[] contentData = pattern.ContentStream.GetDecodedData(_document?.Decryptor);

            // Create a sub-renderer for the pattern content
            var patternRenderer = new PdfRenderer(target, patternResources, _optionalContentManager, _document, _fixupManager);

            // Process the pattern's content stream
            List<PdfOperator> operators = PdfContentParser.Parse(contentData);

            foreach (PdfOperator op in operators)
            {
                patternRenderer.ProcessOperator(op);
            }
        }

        // Call the render target to fill with the pattern
        _target.FillPathWithTilingPattern(path, CurrentState, evenOdd, pattern, RenderPatternContent);
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

    private protected override void OnShowText(PdfString text)
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

        // Type3 fonts: Execute CharProc content streams for each glyph
        if (font.FontType == PdfFontType.Type3 && font is Type3Font type3Font)
        {
            PdfLogger.Log(LogCategory.Text, $"Type3 font '{CurrentState.FontName}' - rendering via CharProc execution");
            RenderType3Text(text, type3Font);
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

        // Calculate total text width
        double totalAdvance = glyphWidths.Sum();

        // Apply fixups if manager is present
        var textToRender = decodedText.ToString();

        // DEBUG: Log BEFORE condition check
        PdfLogger.Log(LogCategory.Text, $"[RENDERER-ENTRY] OnShowText reached fixup section: text='{textToRender}' font='{font.BaseFont}' _fixupManager!=null={_fixupManager is not null}");

        if (_fixupManager is not null && !string.IsNullOrEmpty(textToRender))
        {
            // Get current text position from text matrix (M31, M32 are translation components)
            float textX = CurrentState.TextMatrix.M31;
            float textY = CurrentState.TextMatrix.M32;

            // Create context for fixups
            var context = new TextRunContext(
                text: textToRender,
                x: textX,
                y: textY,
                fontSize: (float)CurrentState.FontSize,
                fontName: font.BaseFont,
                isFallbackFont: false, // TODO: Determine if using fallback font
                intendedWidth: (float)totalAdvance,
                actualWidth: (float)totalAdvance, // TODO: Measure actual width with system font
                graphicsState: CurrentState);

            // Set detection flags for Base14 fonts
            context.IsBase14Font = IsBase14Font(font);
            context.HasEmbeddedFontData = HasEmbeddedFontData(font);

            PdfLogger.Log(LogCategory.Text, $"[RENDERER-DEBUG] Before fixup call: text='{textToRender}' font='{font.BaseFont}' IsBase14={context.IsBase14Font} HasEmbedded={context.HasEmbeddedFontData} fixupManager!=null={_fixupManager != null}");

            // Apply fixups
            _fixupManager.ApplyTextRunFixups(context);

            // Check if fixup wants to skip this text
            if (context.ShouldSkip)
            {
                PdfLogger.Log(LogCategory.Text, $"TEXT-SKIPPED: Fixup requested skip for '{textToRender}'");
                return;
            }

            // Apply spacing adjustment if present
            if (context.CustomData.TryGetValue("SpacingAdjustment", out object? adjustment))
            {
                if (adjustment is float spacingAdjustment)
                {
                    // Distribute the spacing adjustment across all glyphs
                    double perGlyphAdjustment = spacingAdjustment / glyphWidths.Count;
                    for (int j = 0; j < glyphWidths.Count; j++)
                    {
                        glyphWidths[j] += perGlyphAdjustment;
                    }

                    // Update total advance
                    totalAdvance += spacingAdjustment;

                    PdfLogger.Log(LogCategory.Text,
                        $"FIXUP-APPLIED: Added {spacingAdjustment:F2} spacing adjustment to '{textToRender}' ({glyphWidths.Count} glyphs)");
                }
            }
        }

        // Render the text
        _target.DrawText(textToRender, glyphWidths, CurrentState, font, charCodes);

        // Advance text position by the total width
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

        PdfDictionary? colorSpaces = _currentResources?.GetColorSpaces();
        _colorSpaceResolver.ResolveColorSpace(ref fillCs, ref fillColor, colorSpaces);

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
        _colorSpaceResolver.ResolveColorSpace(ref strokeCs, ref strokeColor, colorSpaces);
        CurrentState.ResolvedStrokeColorSpace = strokeCs ?? string.Empty;
        CurrentState.ResolvedStrokeColor = strokeColor ?? [];
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
            PdfLogger.Log(LogCategory.Graphics, "  No resources for ExtGState lookup");
            return;
        }

        PdfDictionary? extGState = _currentResources.GetExtGState(dictName);
        if (extGState is null)
        {
            PdfLogger.Log(LogCategory.Graphics, $"  ExtGState '{dictName}' not found");
            return;
        }

        // Apply all ExtGState parameters to the current graphics state
        _extGStateApplier.ApplyExtGState(extGState, CurrentState);
    }

    /// <summary>
    /// Renders the SMask Group XObject using the render target's soft mask support.
    /// Called by ExtGStateApplier when a soft mask with a Group is encountered.
    /// </summary>
    private void RenderSoftMaskGroup(PdfSoftMask softMask)
    {
        if (softMask.Group is null)
            return;

        // Get the form's resources
        PdfResources? formResources = _resources;
        if (softMask.Group.Dictionary.TryGetValue(new PdfName("Resources"), out PdfObject? resourcesObj))
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
            double a = matrixArray[0].ToDoubleOrNull() ?? 1;
            double b = matrixArray[1].ToDoubleOrNull() ?? 0;
            double c = matrixArray[2].ToDoubleOrNull() ?? 0;
            double d = matrixArray[3].ToDoubleOrNull() ?? 1;
            double e = matrixArray[4].ToDoubleOrNull() ?? 0;
            double f = matrixArray[5].ToDoubleOrNull() ?? 0;
            formMatrix = new Matrix3x2((float)a, (float)b, (float)c, (float)d, (float)e, (float)f);
        }

        // Parse the content operators ahead of time
        byte[] contentData = softMask.Group.GetDecodedData(_document?.Decryptor);
        List<PdfOperator> operators = PdfContentParser.Parse(contentData);

        // Use the render target's soft mask rendering method with a callback
        _target.RenderSoftMask(softMask.Subtype, maskTarget =>
        {
            // Create a renderer for the mask content
            var maskRenderer = new PdfRenderer(maskTarget, formResources ?? _resources, _optionalContentManager, _document, _fixupManager)
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

    
    private protected override void OnShowTextWithPositioning(PdfArray array)
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

        // Type3 fonts: Execute CharProc content streams for each glyph
        if (font.FontType == PdfFontType.Type3 && font is Type3Font type3Font)
        {
            PdfLogger.Log(LogCategory.Text, $"Type3 font '{CurrentState.FontName}' - rendering via CharProc execution (TJ)");
            RenderType3TextWithPositioning(array, type3Font);
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
                string lastChars = fullText.Length >= 3 ? fullText[^3..] : fullText;
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

    // ==================== Type3 Font CharProc Rendering ====================

    /// <summary>
    /// Renders Type3 font glyphs by executing their CharProc content streams.
    /// Type3 fonts define each glyph as a PDF content stream (CharProc).
    /// </summary>
    private void RenderType3Text(PdfString text, Type3Font type3Font)
    {
        byte[] bytes = text.Bytes;
        double totalAdvance = 0;

        // Get the font's resources for CharProc execution
        PdfDictionary? type3ResourcesDict = type3Font.GetResourcesDictionary();
        PdfResources? type3Resources = type3ResourcesDict is not null
            ? new PdfResources(type3ResourcesDict, _document)
            : _currentResources;

        // Get the font matrix to transform glyph space to text space
        double[] fontMatrix = type3Font.FontMatrix;
        var fontMatrixM = new Matrix3x2(
            (float)fontMatrix[0], (float)fontMatrix[1],
            (float)fontMatrix[2], (float)fontMatrix[3],
            (float)fontMatrix[4], (float)fontMatrix[5]);

        PdfLogger.Log(LogCategory.Text, $"RenderType3Text: FontMatrix=[{fontMatrix[0]}, {fontMatrix[1]}, {fontMatrix[2]}, {fontMatrix[3]}, {fontMatrix[4]}, {fontMatrix[5]}]");
        PdfLogger.Log(LogCategory.Text, $"RenderType3Text: TextMatrix=[{CurrentState.TextMatrix.M11:F4}, {CurrentState.TextMatrix.M12:F4}, {CurrentState.TextMatrix.M21:F4}, {CurrentState.TextMatrix.M22:F4}, {CurrentState.TextMatrix.M31:F4}, {CurrentState.TextMatrix.M32:F4}]");
        PdfLogger.Log(LogCategory.Text, $"RenderType3Text: CTM=[{CurrentState.Ctm.M11:F4}, {CurrentState.Ctm.M12:F4}, {CurrentState.Ctm.M21:F4}, {CurrentState.Ctm.M22:F4}, {CurrentState.Ctm.M31:F4}, {CurrentState.Ctm.M32:F4}]");

        for (var i = 0; i < bytes.Length; i++)
        {
            int charCode = bytes[i];

            // Get glyph name from encoding
            string? glyphName = type3Font.GetGlyphName(charCode);
            if (glyphName is null)
            {
                PdfLogger.Log(LogCategory.Text, $"  CharCode {charCode}: No glyph name found");
                continue;
            }

            // Get the CharProc content stream
            PdfStream? charProc = type3Font.GetCharProc(glyphName);
            if (charProc is null)
            {
                PdfLogger.Log(LogCategory.Text, $"  CharCode {charCode} ('{glyphName}'): No CharProc found");
                continue;
            }

            PdfLogger.Log(LogCategory.Text, $"  Rendering CharCode {charCode} ('{glyphName}')");

            // Calculate the position for this glyph
            // The glyph is rendered at the current text position
            // TextRenderingMatrix = Tfs × Tc × Tm × CTM
            var fontSize = (float)CurrentState.FontSize;

            // Build the complete transformation matrix for this glyph
            // Glyph space → Text space (via FontMatrix) → User space (via TextMatrix × fontSize) → Device space (via CTM)
            var fontSizeMatrix = new Matrix3x2(fontSize, 0, 0, fontSize, 0, 0);

            // Calculate glyph position: FontMatrix * FontSize * TextMatrix * CTM
            Matrix3x2 glyphMatrix = fontMatrixM * fontSizeMatrix * CurrentState.TextMatrix * CurrentState.Ctm;

            // Execute the CharProc content stream
            ExecuteType3CharProc(charProc, type3Resources, glyphMatrix);

            // Advance text position
            double glyphWidth = type3Font.GetCharacterWidth(charCode);
            double advance = glyphWidth * CurrentState.FontSize / 1000.0;
            advance *= CurrentState.HorizontalScaling / 100.0;

            if (CurrentState.CharacterSpacing != 0)
                advance += CurrentState.CharacterSpacing;

            string decoded = type3Font.DecodeCharacter(charCode);
            if (decoded == " " && CurrentState.WordSpacing != 0)
                advance += CurrentState.WordSpacing;

            totalAdvance += advance;

            // Advance the text matrix for the next glyph
            CurrentState.AdvanceTextMatrix(advance, 0);
        }
    }

    /// <summary>
    /// Renders Type3 font glyphs from a TJ array (text with positioning adjustments).
    /// </summary>
    private void RenderType3TextWithPositioning(PdfArray array, Type3Font type3Font)
    {
        // Get the font's resources for CharProc execution
        PdfDictionary? type3ResourcesDict = type3Font.GetResourcesDictionary();
        PdfResources? type3Resources = type3ResourcesDict is not null
            ? new PdfResources(type3ResourcesDict, _document)
            : _currentResources;

        // Get the font matrix to transform glyph space to text space
        double[] fontMatrix = type3Font.FontMatrix;
        var fontMatrixM = new Matrix3x2(
            (float)fontMatrix[0], (float)fontMatrix[1],
            (float)fontMatrix[2], (float)fontMatrix[3],
            (float)fontMatrix[4], (float)fontMatrix[5]);

        PdfLogger.Log(LogCategory.Text, $"RenderType3TextWithPositioning: FontMatrix=[{fontMatrix[0]}, {fontMatrix[1]}, {fontMatrix[2]}, {fontMatrix[3]}, {fontMatrix[4]}, {fontMatrix[5]}]");

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

                        // Get glyph name from encoding
                        string? glyphName = type3Font.GetGlyphName(charCode);
                        if (glyphName is null)
                        {
                            PdfLogger.Log(LogCategory.Text, $"  CharCode {charCode}: No glyph name found");
                            continue;
                        }

                        // Get the CharProc content stream
                        PdfStream? charProc = type3Font.GetCharProc(glyphName);
                        if (charProc is null)
                        {
                            PdfLogger.Log(LogCategory.Text, $"  CharCode {charCode} ('{glyphName}'): No CharProc found");
                            continue;
                        }

                        PdfLogger.Log(LogCategory.Text, $"  Rendering TJ CharCode {charCode} ('{glyphName}')");

                        // Calculate the glyph transformation matrix
                        var fontSize = (float)CurrentState.FontSize;
                        var fontSizeMatrix = new Matrix3x2(fontSize, 0, 0, fontSize, 0, 0);
                        Matrix3x2 glyphMatrix = fontMatrixM * fontSizeMatrix * CurrentState.TextMatrix * CurrentState.Ctm;

                        // Execute the CharProc content stream
                        ExecuteType3CharProc(charProc, type3Resources, glyphMatrix);

                        // Advance text position
                        double glyphWidth = type3Font.GetCharacterWidth(charCode);
                        double advance = glyphWidth * CurrentState.FontSize / 1000.0;
                        advance *= CurrentState.HorizontalScaling / 100.0;

                        if (CurrentState.CharacterSpacing != 0)
                            advance += CurrentState.CharacterSpacing;

                        string decoded = type3Font.DecodeCharacter(charCode);
                        if (decoded == " " && CurrentState.WordSpacing != 0)
                            advance += CurrentState.WordSpacing;

                        CurrentState.AdvanceTextMatrix(advance, 0);
                    }
                    break;
                }
                case PdfInteger intVal:
                {
                    // Kerning adjustment in thousandths of text space
                    double adjustment = -intVal.Value / 1000.0 * CurrentState.FontSize;
                    adjustment *= CurrentState.HorizontalScaling / 100.0;
                    CurrentState.AdvanceTextMatrix(adjustment, 0);
                    break;
                }
                case PdfReal realVal:
                {
                    double adjustment = -realVal.Value / 1000.0 * CurrentState.FontSize;
                    adjustment *= CurrentState.HorizontalScaling / 100.0;
                    CurrentState.AdvanceTextMatrix(adjustment, 0);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Executes a Type3 CharProc content stream to render a single glyph.
    /// </summary>
    private void ExecuteType3CharProc(PdfStream charProc, PdfResources? resources, Matrix3x2 glyphMatrix)
    {
        try
        {
            // Get the decoded content stream data
            byte[] contentData = charProc.GetDecodedData(_document?.Decryptor);

            PdfLogger.Log(LogCategory.Text, $"    ExecuteType3CharProc: {contentData.Length} bytes, glyphMatrix=[{glyphMatrix.M11:F6}, {glyphMatrix.M12:F6}, {glyphMatrix.M21:F6}, {glyphMatrix.M22:F6}, {glyphMatrix.M31:F2}, {glyphMatrix.M32:F2}]");

            // Create a sub-renderer for the CharProc with the glyph transformation
            var charProcRenderer = new PdfRenderer(_target, resources ?? _currentResources, _optionalContentManager, _document, _fixupManager)
            {
                CurrentState =
                {
                    // Set the CTM to the glyph matrix
                    Ctm = glyphMatrix
                }
            };

            // Save render target state
            _target.SaveState();

            // Apply the glyph matrix to the render target
            _target.ApplyCtm(glyphMatrix);

            // Parse and process the CharProc operators
            List<PdfOperator> operators = PdfContentParser.Parse(contentData);
            charProcRenderer.ProcessOperators(operators);

            // Restore render target state
            _target.RestoreState();

            PdfLogger.Log(LogCategory.Text, $"    ExecuteType3CharProc: Completed ({operators.Count} operators)");
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Text, $"    ExecuteType3CharProc: ERROR - {ex.Message}");
        }
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
    private protected override void OnInlineImage(InlineImageOperator inlineImage)
    {
        PdfLogger.Log(LogCategory.Images, $"INLINE-IMAGE: {inlineImage.Width}x{inlineImage.Height}, ColorSpace={inlineImage.ColorSpace}, BPC={inlineImage.BitsPerComponent}, Filter={inlineImage.Filter ?? "none"}");

        try
        {
            // Resolve color space resource references before creating PdfImage
            // Inline images may reference color spaces via resource names (e.g., /CS /R16)
            string colorSpaceName = inlineImage.ColorSpace;
            PdfDictionary? colorSpaces = _currentResources?.GetColorSpaces();

            // Check if the color space is a resource reference (not a device color space)
            bool isDeviceColorSpace = colorSpaceName is "DeviceGray" or "DeviceRGB" or "DeviceCMYK"
                or "G" or "RGB" or "CMYK"; // Abbreviated names

            if (!isDeviceColorSpace && colorSpaces is not null)
            {
                // Try to resolve the color space reference
                PdfName csKey = new PdfName(colorSpaceName);
                if (colorSpaces.TryGetValue(csKey, out PdfObject? csObj))
                {
                    PdfLogger.Log(LogCategory.Images, $"  Resolving color space '{colorSpaceName}' from resources");

                    // Resolve indirect reference if needed
                    if (csObj is PdfIndirectReference csRef && _document is not null)
                        csObj = _document.ResolveReference(csRef);

                    // Replace the color space in the inline image parameters
                    // The PdfImage constructor will read from Parameters dictionary
                    if (csObj is not null)
                    {
                        // Check for both abbreviated and full parameter names
                        PdfName csParamKey = inlineImage.Parameters.ContainsKey(new PdfName("CS"))
                            ? new PdfName("CS")
                            : new PdfName("ColorSpace");
                        inlineImage.Parameters[csParamKey] = csObj;

                        PdfLogger.Log(LogCategory.Images, $"  Replaced ColorSpace parameter with resolved object: {csObj.Type}");
                    }
                }
            }

            // Create PdfImage from inline image operator (now with resolved color space)
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
        if (formStream.Dictionary.TryGetValue(new PdfName("Resources"), out PdfObject? resourcesObj))
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
        if (formStream.Dictionary.TryGetValue(new PdfName("Matrix"), out PdfObject matrixObj) && matrixObj is PdfArray matrixArray && matrixArray.Count >= 6)
        {
            var m11 = (float)matrixArray[0].ToDouble();
            var m12 = (float)matrixArray[1].ToDouble();
            var m21 = (float)matrixArray[2].ToDouble();
            var m22 = (float)matrixArray[3].ToDouble();
            var m31 = (float)matrixArray[4].ToDouble();
            var m32 = (float)matrixArray[5].ToDouble();
            var formMatrix = new Matrix3x2(m11, m12, m21, m22, m31, m32);

            // Concatenate: formCtm = formMatrix * savedCtm
            // This transforms form coordinates through the form matrix, then through the page CTM
            formCtm = formMatrix * savedCtm;

            PdfLogger.Log(LogCategory.Graphics, $"RenderFormXObject: Form Matrix = [{m11}, {m12}, {m21}, {m22}, {m31}, {m32}]");
        }

        // Create a new renderer for the form to ensure it starts with a fresh graphics state
        var formRenderer = new PdfRenderer(_target, formResources ?? _resources, _optionalContentManager, _document, _fixupManager)
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

    private protected override void ProcessOperator(PdfOperator op)
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

    // ==================== Fixup Helper Methods ====================

    /// <summary>
    /// The 14 standard PDF fonts that can be referenced without embedding.
    /// </summary>
    private static readonly string[] Base14FontNames =
    [
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
        "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
        "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique",
        "Symbol", "ZapfDingbats"
    ];

    /// <summary>
    /// Checks if a font is one of the PDF Base14 standard fonts.
    /// </summary>
    private static bool IsBase14Font(PdfFont? font)
    {
        if (font is null)
            return false;

        // Get the base font name (strip subset prefix if present)
        string baseFontName = font.BaseFont;
        if (font.IsSubsetFont && baseFontName.Length > 7)
        {
            // Remove the "XXXXXX+" prefix from subset fonts
            baseFontName = baseFontName.Substring(7);
        }

        return Base14FontNames.Contains(baseFontName);
    }

    /// <summary>
    /// Checks if a font has embedded font data (FontFile/FontFile2/FontFile3).
    /// </summary>
    private static bool HasEmbeddedFontData(PdfFont? font)
    {
        if (font is null)
            return false;

        PdfFontDescriptor? descriptor = font.GetDescriptor();
        if (descriptor is null)
            return false;

        // Check for any of the three font file streams
        return descriptor.GetFontFile() is not null ||
               descriptor.GetFontFile2() is not null ||
               descriptor.GetFontFile3() is not null;
    }
}
