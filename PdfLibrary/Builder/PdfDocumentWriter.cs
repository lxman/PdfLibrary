using System.IO.Compression;
using System.Text;
using PdfLibrary.Fonts.Embedded;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace PdfLibrary.Builder;

/// <summary>
/// Writes PDF documents to files or streams
/// </summary>
public class PdfDocumentWriter
{
    private int _nextObjectNumber = 1;
    private readonly Dictionary<int, long> _objectOffsets = new();
    private readonly Dictionary<string, int> _fontObjects = new();
    private readonly Dictionary<string, int> _fontDescriptorObjects = new(); // For custom fonts
    private readonly List<(PdfImageContent image, int objectNumber)> _imageObjects = [];
    private readonly Dictionary<(double fillOpacity, double strokeOpacity), int> _extGStateObjects = new(); // (fillOpacity, strokeOpacity) -> object number

    /// <summary>
    /// Write a document to a file
    /// </summary>
    public void Write(PdfDocumentBuilder builder, string filePath)
    {
        using FileStream stream = File.Create(filePath);
        Write(builder, stream);
    }

    /// <summary>
    /// Write a document to a stream
    /// </summary>
    public void Write(PdfDocumentBuilder builder, Stream stream)
    {
        _nextObjectNumber = 1;
        _objectOffsets.Clear();
        _fontObjects.Clear();
        _fontDescriptorObjects.Clear();
        _imageObjects.Clear();

        using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.NewLine = "\n";

        // PDF Header
        writer.WriteLine("%PDF-1.7");
        writer.WriteLine("%âãÏÓ"); // Binary marker
        writer.Flush();

        // Collect all fonts used
        List<string> fonts = CollectFonts(builder);

        // Reserve object numbers
        int catalogObj = _nextObjectNumber++;
        int pagesObj = _nextObjectNumber++;
        int infoObj = _nextObjectNumber++;

        // Reserve font objects
        foreach (string font in fonts)
        {
            _fontObjects[font] = _nextObjectNumber++;

            // For custom fonts, reserve additional objects for FontDescriptor and FontFile2
            if (!builder.CustomFonts.ContainsKey(font)) continue;
            _fontDescriptorObjects[font] = _nextObjectNumber++; // FontDescriptor
            _nextObjectNumber++; // FontFile2 stream (we'll track this in WriteTrueTypeFont)
        }

        // Reserve page objects and their content streams
        List<(int pageObj, int contentObj)> pageObjectNumbers =
            (from _ in builder.Pages
                select _nextObjectNumber++
                into pageObj
                let contentObj = _nextObjectNumber++
                select (pageObj, contentObj))
            .ToList();

        // Collect and reserve image objects
        foreach (PdfPageBuilder page in builder.Pages)
        {
            foreach (PdfContentElement content in page.Content)
            {
                if (content is not PdfImageContent image) continue;
                int imageObj = _nextObjectNumber++;
                _imageObjects.Add((image, imageObj));

                // Collect opacity values for ExtGState objects
                if (image.Opacity < 1.0)
                {
                    (double, double) key = (image.Opacity, image.Opacity);
                    if (!_extGStateObjects.ContainsKey(key))
                    {
                        _extGStateObjects[key] = _nextObjectNumber++;
                    }
                }
            }

            // Collect opacity values from path content
            foreach (PdfContentElement content in page.Content)
            {
                if (content is not PdfPathContent path) continue;

                bool needsOpacity = path.FillOpacity < 1.0 || path.StrokeOpacity < 1.0;
                if (!needsOpacity) continue;

                (double FillOpacity, double StrokeOpacity) key = (path.FillOpacity, path.StrokeOpacity);
                if (!_extGStateObjects.ContainsKey(key))
                {
                    _extGStateObjects[key] = _nextObjectNumber++;
                }
            }
        }

        // AcroForm object (if needed)
        int? acroFormObj = null;
        var fieldObjects = new List<int>();
        if (HasFormFields(builder))
        {
            acroFormObj = _nextObjectNumber++;
            // Reserve objects for each field
            foreach (PdfPageBuilder page in builder.Pages)
            {
                fieldObjects.AddRange(page.FormFields.Select(_ => _nextObjectNumber++));
            }
        }

        // Write Catalog
        WriteObjectStart(writer, catalogObj);
        writer.WriteLine("<< /Type /Catalog");
        writer.WriteLine($"   /Pages {pagesObj} 0 R");
        if (acroFormObj.HasValue)
        {
            writer.WriteLine($"   /AcroForm {acroFormObj.Value} 0 R");
        }
        writer.WriteLine(">>");
        WriteObjectEnd(writer);

        // Write Pages
        WriteObjectStart(writer, pagesObj);
        writer.WriteLine("<< /Type /Pages");
        writer.Write("   /Kids [");
        for (var i = 0; i < pageObjectNumbers.Count; i++)
        {
            if (i > 0) writer.Write(" ");
            writer.Write($"{pageObjectNumbers[i].pageObj} 0 R");
        }
        writer.WriteLine("]");
        writer.WriteLine($"   /Count {builder.Pages.Count}");
        writer.WriteLine(">>");
        WriteObjectEnd(writer);

        // Write Info dictionary
        WriteObjectStart(writer, infoObj);
        writer.WriteLine("<<");
        PdfMetadataBuilder meta = builder.Metadata;
        if (!string.IsNullOrEmpty(meta.Title))
            writer.WriteLine($"   /Title {PdfString(meta.Title)}");
        if (!string.IsNullOrEmpty(meta.Author))
            writer.WriteLine($"   /Author {PdfString(meta.Author)}");
        if (!string.IsNullOrEmpty(meta.Subject))
            writer.WriteLine($"   /Subject {PdfString(meta.Subject)}");
        if (!string.IsNullOrEmpty(meta.Keywords))
            writer.WriteLine($"   /Keywords {PdfString(meta.Keywords)}");
        if (!string.IsNullOrEmpty(meta.Creator))
            writer.WriteLine($"   /Creator {PdfString(meta.Creator)}");

        string producer = meta.Producer ?? "PdfLibrary";
        writer.WriteLine($"   /Producer {PdfString(producer)}");

        DateTime creationDate = meta.CreationDate ?? DateTime.Now;
        writer.WriteLine($"   /CreationDate {PdfDate(creationDate)}");

        if (meta.ModificationDate.HasValue)
            writer.WriteLine($"   /ModDate {PdfDate(meta.ModificationDate.Value)}");

        writer.WriteLine(">>");
        WriteObjectEnd(writer);

        // Write Font objects
        foreach (string font in fonts)
        {
            if (builder.CustomFonts.TryGetValue(font, out CustomFontInfo? customFont))
            {
                // Write TrueType font with embedding
                WriteTrueTypeFont(writer, font, customFont);
            }
            else
            {
                // Write standard Type1 font (Base-14)
                WriteObjectStart(writer, _fontObjects[font]);
                writer.WriteLine("<< /Type /Font");
                writer.WriteLine("   /Subtype /Type1");
                writer.WriteLine($"   /BaseFont /{font}");
                writer.WriteLine("   /Encoding /WinAnsiEncoding");
                writer.WriteLine(">>");
                WriteObjectEnd(writer);
            }
        }

        // Write ExtGState objects for opacity
        foreach (((double fillOpacity, double strokeOpacity) key, int objNum) in _extGStateObjects)
        {
            WriteObjectStart(writer, objNum);
            writer.WriteLine("<<");
            writer.WriteLine("   /Type /ExtGState");
            writer.WriteLine($"   /ca {key.fillOpacity:F2}"); // Non-stroking (fill) alpha
            writer.WriteLine($"   /CA {key.strokeOpacity:F2}"); // Stroking alpha
            writer.WriteLine(">>");
            WriteObjectEnd(writer);
        }

        // Write Image XObjects
        foreach ((PdfImageContent image, int objNum) in _imageObjects)
        {
            WriteImageXObject(writer, objNum, image);
        }

        // Write Page objects and content
        var fieldIndex = 0;
        for (var i = 0; i < builder.Pages.Count; i++)
        {
            PdfPageBuilder page = builder.Pages[i];
            (int pageObj, int contentObj) = pageObjectNumbers[i];

            // Page object
            WriteObjectStart(writer, pageObj);
            writer.WriteLine("<< /Type /Page");
            writer.WriteLine($"   /Parent {pagesObj} 0 R");
            writer.WriteLine($"   /MediaBox [0 0 {page.Size.Width:F2} {page.Size.Height:F2}]");
            writer.WriteLine($"   /Contents {contentObj} 0 R");

            // Resources
            writer.WriteLine("   /Resources <<");
            if (_fontObjects.Count > 0)
            {
                writer.WriteLine("      /Font <<");
                var fontIndex = 1;
                foreach (string font in fonts)
                {
                    writer.WriteLine($"         /F{fontIndex} {_fontObjects[font]} 0 R");
                    fontIndex++;
                }
                writer.WriteLine("      >>");
            }

            // Add XObject dictionary for images
            if (_imageObjects.Count > 0)
            {
                writer.WriteLine("      /XObject <<");
                for (var imgIdx = 0; imgIdx < _imageObjects.Count; imgIdx++)
                {
                    writer.WriteLine($"         /Im{imgIdx + 1} {_imageObjects[imgIdx].objectNumber} 0 R");
                }
                writer.WriteLine("      >>");
            }

            // Add ExtGState dictionary for opacity
            if (_extGStateObjects.Count > 0)
            {
                writer.WriteLine("      /ExtGState <<");
                var gsIndex = 1;
                foreach (((double fillOpacity, double strokeOpacity) key, int objNum) in _extGStateObjects.OrderBy(x => x.Key))
                {
                    writer.WriteLine($"         /GS{gsIndex} {objNum} 0 R");
                    gsIndex++;
                }
                writer.WriteLine("      >>");
            }
            writer.WriteLine("   >>");

            // Annotations (form fields)
            if (page.FormFields.Count > 0)
            {
                writer.Write("   /Annots [");
                for (var j = 0; j < page.FormFields.Count; j++)
                {
                    if (j > 0) writer.Write(" ");
                    writer.Write($"{fieldObjects[fieldIndex + j]} 0 R");
                }
                writer.WriteLine("]");
            }

            writer.WriteLine(">>");
            WriteObjectEnd(writer);

            // Content stream
            byte[] content = GenerateContentStream(page, fonts);
            WriteStreamObject(writer, contentObj, content);

            fieldIndex += page.FormFields.Count;
        }

        // Write AcroForm and fields
        if (acroFormObj.HasValue)
        {
            WriteObjectStart(writer, acroFormObj.Value);
            writer.WriteLine("<<");
            writer.Write("   /Fields [");
            for (var i = 0; i < fieldObjects.Count; i++)
            {
                if (i > 0) writer.Write(" ");
                writer.Write($"{fieldObjects[i]} 0 R");
            }
            writer.WriteLine("]");

            PdfAcroFormBuilder? acroForm = builder.AcroForm;
            if (acroForm is not null)
            {
                if (acroForm.NeedAppearances)
                    writer.WriteLine("   /NeedAppearances true");

                // Default appearance
                int fontIndex = fonts.IndexOf(acroForm.DefaultFont) + 1;
                if (fontIndex > 0)
                {
                    writer.WriteLine($"   /DA (/F{fontIndex} {acroForm.DefaultFontSize:F1} Tf 0 g)");
                }
            }
            else
            {
                writer.WriteLine("   /NeedAppearances true");
            }

            writer.WriteLine(">>");
            WriteObjectEnd(writer);

            // Write field objects
            fieldIndex = 0;
            var pageIndex = 0;
            foreach (PdfPageBuilder page in builder.Pages)
            {
                foreach (PdfFormFieldBuilder field in page.FormFields)
                {
                    WriteFormField(writer, fieldObjects[fieldIndex], field, pageObjectNumbers[pageIndex].pageObj, fonts);
                    fieldIndex++;
                }
                pageIndex++;
            }
        }

        // Write xref table
        writer.Flush();
        long xrefOffset = stream.Position;

        writer.WriteLine("xref");
        writer.WriteLine($"0 {_objectOffsets.Count + 1}");
        writer.WriteLine("0000000000 65535 f ");
        // Write offsets in object number order (1 to N)
        for (var objNum = 1; objNum <= _objectOffsets.Count; objNum++)
        {
            writer.WriteLine($"{_objectOffsets[objNum]:D10} 00000 n ");
        }

        // Write trailer
        writer.WriteLine("trailer");
        writer.WriteLine("<<");
        writer.WriteLine($"   /Size {_objectOffsets.Count + 1}");
        writer.WriteLine($"   /Root {catalogObj} 0 R");
        writer.WriteLine($"   /Info {infoObj} 0 R");
        writer.WriteLine(">>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefOffset);
        writer.WriteLine("%%EOF");
    }

    private void WriteObjectStart(StreamWriter writer, int objectNumber)
    {
        writer.Flush();
        _objectOffsets[objectNumber] = writer.BaseStream.Position;
        writer.WriteLine($"{objectNumber} 0 obj");
    }

    private void WriteObjectEnd(StreamWriter writer)
    {
        writer.WriteLine("endobj");
        writer.WriteLine();
    }

    private void WriteStreamObject(StreamWriter writer, int objectNumber, byte[] data)
    {
        WriteObjectStart(writer, objectNumber);
        writer.WriteLine($"<< /Length {data.Length} >>");
        writer.WriteLine("stream");
        writer.Flush();
        writer.BaseStream.Write(data);
        writer.WriteLine();
        writer.WriteLine("endstream");
        WriteObjectEnd(writer);
    }

    private List<string> CollectFonts(PdfDocumentBuilder builder)
    {
        var fonts = new HashSet<string> {
            // Default font
            "Helvetica" };

        // Fonts from content
        foreach (PdfPageBuilder page in builder.Pages)
        {
            foreach (PdfContentElement element in page.Content)
            {
                if (element is PdfTextContent text)
                {
                    fonts.Add(text.FontName);
                }
            }

            // Fonts from form fields
            foreach (PdfFormFieldBuilder field in page.FormFields)
            {
                switch (field)
                {
                    case PdfTextFieldBuilder textField:
                        fonts.Add(textField.FontName);
                        break;
                    case PdfDropdownBuilder dropdown:
                        fonts.Add(dropdown.FontName);
                        break;
                }
            }
        }

        // Font from AcroForm defaults
        if (builder.AcroForm is not null)
        {
            fonts.Add(builder.AcroForm.DefaultFont);
        }

        return fonts.ToList();
    }

    private bool HasFormFields(PdfDocumentBuilder builder)
    {
        return builder.Pages.Any(p => p.FormFields.Count > 0);
    }

    private byte[] GenerateContentStream(PdfPageBuilder page, List<string> fonts)
    {
        var sb = new StringBuilder();

        foreach (PdfContentElement element in page.Content)
        {
            switch (element)
            {
                case PdfTextContent text:
                    int fontIndex = fonts.IndexOf(text.FontName) + 1;

                    // Graphics state operators must be OUTSIDE the text block
                    // Set stroke color and line width before BT
                    if (text.StrokeColor.HasValue)
                    {
                        AppendColorOperator(sb, text.StrokeColor.Value, isFill: false);
                        sb.AppendLine($"{text.StrokeWidth:F2} w");
                    }

                    sb.AppendLine("BT");

                    // Set font
                    sb.AppendLine($"/F{fontIndex} {text.FontSize:F1} Tf");

                    // ALWAYS set all text state operators to ensure clean state
                    // (text state persists across BT/ET blocks, so we must reset to defaults)
                    sb.AppendLine($"{text.CharacterSpacing:F2} Tc");
                    sb.AppendLine($"{text.WordSpacing:F2} Tw");
                    sb.AppendLine($"{text.HorizontalScale:F1} Tz");
                    sb.AppendLine($"{text.TextRise:F2} Ts");
                    sb.AppendLine($"{(int)text.RenderMode} Tr");

                    if (text.LineSpacing > 0)
                        sb.AppendLine($"{text.LineSpacing:F2} TL");

                    // Fill color (rg is allowed inside BT...ET)
                    AppendColorOperator(sb, text.FillColor, isFill: true);

                    // Position with optional rotation using the text matrix
                    // Text matrix: [a b c d e f] where:
                    //   a = horizontal scaling * cos(rotation)
                    //   b = horizontal scaling * sin(rotation)
                    //   c = -sin(rotation)
                    //   d = cos(rotation)
                    //   e = x position
                    //   f = y position
                    if (text.Rotation != 0)
                    {
                        double radians = text.Rotation * Math.PI / 180;
                        double cos = Math.Cos(radians);
                        double sin = Math.Sin(radians);
                        sb.AppendLine($"{cos:F4} {sin:F4} {-sin:F4} {cos:F4} {text.X:F2} {text.Y:F2} Tm");
                    }
                    else
                    {
                        // Use Tm for absolute positioning (1 0 0 1 x y = identity matrix at position x,y)
                        sb.AppendLine($"1 0 0 1 {text.X:F2} {text.Y:F2} Tm");
                    }

                    // Output text
                    sb.AppendLine($"({EscapePdfString(text.Text)}) Tj");
                    sb.AppendLine("ET");
                    break;

                case PdfRectangleContent rect:
                    if (rect.FillColor.HasValue || rect.StrokeColor.HasValue)
                    {
                        sb.AppendLine("q");
                        sb.AppendLine($"{rect.LineWidth:F2} w");

                        if (rect.FillColor.HasValue)
                        {
                            AppendColorOperator(sb, rect.FillColor.Value, isFill: true);
                        }
                        if (rect.StrokeColor.HasValue)
                        {
                            AppendColorOperator(sb, rect.StrokeColor.Value, isFill: false);
                        }

                        sb.AppendLine($"{rect.Rect.Left:F2} {rect.Rect.Bottom:F2} {rect.Rect.Width:F2} {rect.Rect.Height:F2} re");

                        if (rect is { FillColor: not null, StrokeColor: not null })
                            sb.AppendLine("B");
                        else if (rect.FillColor.HasValue)
                            sb.AppendLine("f");
                        else
                            sb.AppendLine("S");

                        sb.AppendLine("Q");
                    }
                    break;

                case PdfLineContent line:
                    sb.AppendLine("q");
                    sb.AppendLine($"{line.LineWidth:F2} w");
                    AppendColorOperator(sb, line.StrokeColor, isFill: false);
                    sb.AppendLine($"{line.X1:F2} {line.Y1:F2} m");
                    sb.AppendLine($"{line.X2:F2} {line.Y2:F2} l");
                    sb.AppendLine("S");
                    sb.AppendLine("Q");
                    break;

                case PdfImageContent image:
                    // Find the image index
                    int imageIndex = -1;
                    for (var idx = 0; idx < _imageObjects.Count; idx++)
                    {
                        if (!ReferenceEquals(_imageObjects[idx].image, image)) continue;
                        imageIndex = idx + 1;
                        break;
                    }

                    if (imageIndex > 0)
                    {
                        sb.AppendLine("q"); // Save graphics state

                        // Apply opacity if not fully opaque
                        if (image.Opacity < 1.0)
                        {
                            (double, double) opacityKey = (image.Opacity, image.Opacity);
                            if (_extGStateObjects.ContainsKey(opacityKey))
                            {
                                int gsIndex = GetExtGStateIndex(opacityKey);
                                sb.AppendLine($"/GS{gsIndex} gs");
                            }
                        }

                        // Build transformation matrix
                        double width = image.Rect.Width;
                        double height = image.Rect.Height;
                        double x = image.Rect.Left;
                        double y = image.Rect.Bottom;

                        if (image.Rotation != 0)
                        {
                            // Rotation around center of image
                            double radians = image.Rotation * Math.PI / 180;
                            double cos = Math.Cos(radians);
                            double sin = Math.Sin(radians);
                            double cx = x + width / 2;
                            double cy = y + height / 2;

                            // Translate to center, rotate, scale, translate back
                            sb.AppendLine($"1 0 0 1 {cx:F2} {cy:F2} cm"); // Translate to center
                            sb.AppendLine($"{cos:F4} {sin:F4} {-sin:F4} {cos:F4} 0 0 cm"); // Rotate
                            sb.AppendLine($"{width:F2} 0 0 {height:F2} {-width / 2:F2} {-height / 2:F2} cm"); // Scale and offset
                        }
                        else
                        {
                            // Simple transformation: scale and position
                            // cm matrix: [width 0 0 height x y]
                            sb.AppendLine($"{width:F2} 0 0 {height:F2} {x:F2} {y:F2} cm");
                        }

                        // Draw the image
                        sb.AppendLine($"/Im{imageIndex} Do");
                        sb.AppendLine("Q"); // Restore graphics state
                    }
                    break;

                case PdfPathContent path:
                    GeneratePathContent(sb, path);
                    break;
            }
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Generate PDF content stream operators for a path
    /// </summary>
    private void GeneratePathContent(StringBuilder sb, PdfPathContent path)
    {
        sb.AppendLine("q"); // Save graphics state

        // Apply opacity if needed
        bool needsOpacity = path.FillOpacity < 1.0 || path.StrokeOpacity < 1.0;
        if (needsOpacity)
        {
            (double FillOpacity, double StrokeOpacity) opacityKey = (path.FillOpacity, path.StrokeOpacity);
            if (_extGStateObjects.ContainsKey(opacityKey))
            {
                int gsIndex = GetExtGStateIndex(opacityKey);
                sb.AppendLine($"/GS{gsIndex} gs");
            }
        }

        // Set line width
        sb.AppendLine($"{path.LineWidth:F2} w");

        // Set line cap
        sb.AppendLine($"{(int)path.LineCap} J");

        // Set line join
        sb.AppendLine($"{(int)path.LineJoin} j");

        // Set miter limit
        sb.AppendLine($"{path.MiterLimit:F2} M");

        // Set dash pattern
        if (path.DashPattern is { Length: > 0 })
        {
            sb.Append("[");
            for (var i = 0; i < path.DashPattern.Length; i++)
            {
                if (i > 0) sb.Append(" ");
                sb.Append($"{path.DashPattern[i]:F2}");
            }
            sb.AppendLine($"] {path.DashPhase:F2} d");
        }
        else
        {
            sb.AppendLine("[] 0 d"); // Reset to solid line
        }

        // Set fill color
        if (path.FillColor.HasValue)
        {
            AppendColorOperator(sb, path.FillColor.Value, isFill: true);
        }

        // Set stroke color
        if (path.StrokeColor.HasValue)
        {
            AppendColorOperator(sb, path.StrokeColor.Value, isFill: false);
        }

        // Generate path segments
        foreach (PdfPathSegment segment in path.Segments)
        {
            switch (segment.Type)
            {
                case PdfPathSegmentType.MoveTo:
                    sb.AppendLine($"{segment.Points[0]:F2} {segment.Points[1]:F2} m");
                    break;
                case PdfPathSegmentType.LineTo:
                    sb.AppendLine($"{segment.Points[0]:F2} {segment.Points[1]:F2} l");
                    break;
                case PdfPathSegmentType.CurveTo:
                    sb.AppendLine($"{segment.Points[0]:F2} {segment.Points[1]:F2} {segment.Points[2]:F2} {segment.Points[3]:F2} {segment.Points[4]:F2} {segment.Points[5]:F2} c");
                    break;
                case PdfPathSegmentType.CurveToV:
                    sb.AppendLine($"{segment.Points[0]:F2} {segment.Points[1]:F2} {segment.Points[2]:F2} {segment.Points[3]:F2} v");
                    break;
                case PdfPathSegmentType.CurveToY:
                    sb.AppendLine($"{segment.Points[0]:F2} {segment.Points[1]:F2} {segment.Points[2]:F2} {segment.Points[3]:F2} y");
                    break;
                case PdfPathSegmentType.ClosePath:
                    sb.AppendLine("h");
                    break;
                case PdfPathSegmentType.Rectangle:
                    sb.AppendLine($"{segment.Points[0]:F2} {segment.Points[1]:F2} {segment.Points[2]:F2} {segment.Points[3]:F2} re");
                    break;
            }
        }

        // Paint the path
        if (path.IsClippingPath)
        {
            // Clipping path
            if (path.FillRule == PdfFillRule.EvenOdd)
                sb.AppendLine("W* n"); // Even-odd clip, no paint
            else
                sb.AppendLine("W n"); // Non-zero winding clip, no paint
        }
        else if (path.FillColor.HasValue && path.StrokeColor.HasValue)
        {
            // Fill and stroke
            if (path.FillRule == PdfFillRule.EvenOdd)
                sb.AppendLine("B*"); // Fill (even-odd) and stroke
            else
                sb.AppendLine("B"); // Fill (non-zero) and stroke
        }
        else if (path.FillColor.HasValue)
        {
            // Fill only
            if (path.FillRule == PdfFillRule.EvenOdd)
                sb.AppendLine("f*"); // Fill using even-odd rule
            else
                sb.AppendLine("f"); // Fill using non-zero winding
        }
        else if (path.StrokeColor.HasValue)
        {
            // Stroke only
            sb.AppendLine("S");
        }
        else
        {
            // No paint - end path
            sb.AppendLine("n");
        }

        sb.AppendLine("Q"); // Restore graphics state
    }

    /// <summary>
    /// Append color operator for fill or stroke based on color space
    /// </summary>
    private static void AppendColorOperator(StringBuilder sb, PdfColor color, bool isFill)
    {
        switch (color.ColorSpace)
        {
            case PdfColorSpace.DeviceGray:
                // g for fill, G for stroke
                sb.AppendLine(isFill
                    ? $"{color.Components[0]:F3} g"
                    : $"{color.Components[0]:F3} G");
                break;

            case PdfColorSpace.DeviceCMYK:
                // k for fill, K for stroke
                sb.AppendLine(isFill
                    ? $"{color.Components[0]:F3} {color.Components[1]:F3} {color.Components[2]:F3} {color.Components[3]:F3} k"
                    : $"{color.Components[0]:F3} {color.Components[1]:F3} {color.Components[2]:F3} {color.Components[3]:F3} K");
                break;

            case PdfColorSpace.DeviceRGB:
            default:
                // rg for fill, RG for stroke
                sb.AppendLine(isFill
                    ? $"{color.R:F3} {color.G:F3} {color.B:F3} rg"
                    : $"{color.R:F3} {color.G:F3} {color.B:F3} RG");
                break;
        }
    }

    /// <summary>
    /// Get the index of an ExtGState object for the given opacity key
    /// </summary>
    private int GetExtGStateIndex((double fillOpacity, double strokeOpacity) key)
    {
        var index = 1;
        foreach ((double fillOpacity, double strokeOpacity) k in _extGStateObjects.Keys.OrderBy(x => x))
        {
            if (k == key)
                return index;
            index++;
        }
        return -1;
    }

    private void WriteFormField(StreamWriter writer, int objectNumber, PdfFormFieldBuilder field, int pageObj, List<string> fonts)
    {
        WriteObjectStart(writer, objectNumber);
        writer.WriteLine("<<");
        writer.WriteLine("   /Type /Annot");
        writer.WriteLine("   /Subtype /Widget");
        writer.WriteLine($"   /Rect [{field.Rect.Left:F2} {field.Rect.Bottom:F2} {field.Rect.Right:F2} {field.Rect.Top:F2}]");
        writer.WriteLine($"   /P {pageObj} 0 R");
        writer.WriteLine($"   /T {PdfString(field.Name)}");

        if (!string.IsNullOrEmpty(field.Tooltip))
            writer.WriteLine($"   /TU {PdfString(field.Tooltip)}");

        // Field-specific properties
        switch (field)
        {
            case PdfTextFieldBuilder textField:
                writer.WriteLine("   /FT /Tx");

                var flags = 0;
                if (textField.IsMultiline) flags |= 1 << 12;
                if (textField.IsPassword) flags |= 1 << 13;
                if (textField is { IsComb: true, MaxLength: > 0 }) flags |= 1 << 24;
                if (textField.IsReadOnly) flags |= 1;
                if (textField.IsRequired) flags |= 1 << 1;
                if (flags != 0)
                    writer.WriteLine($"   /Ff {flags}");

                if (textField.MaxLength > 0)
                    writer.WriteLine($"   /MaxLen {textField.MaxLength}");

                if (!string.IsNullOrEmpty(textField.DefaultValue))
                    writer.WriteLine($"   /V {PdfString(textField.DefaultValue)}");

                // Default appearance
                int fontIndex = fonts.IndexOf(textField.FontName) + 1;
                PdfColor tc = textField.TextColor;
                string fontSize = textField.FontSize > 0 ? $"{textField.FontSize:F1}" : "0";
                writer.WriteLine($"   /DA (/F{fontIndex} {fontSize} Tf {tc.R:F3} {tc.G:F3} {tc.B:F3} rg)");
                writer.WriteLine($"   /Q {(int)textField.Alignment}");
                break;

            case PdfCheckboxBuilder checkbox:
                writer.WriteLine("   /FT /Btn");

                var cbFlags = 0;
                if (checkbox.IsReadOnly) cbFlags |= 1;
                if (checkbox.IsRequired) cbFlags |= 1 << 1;
                if (cbFlags != 0)
                    writer.WriteLine($"   /Ff {cbFlags}");

                string state = checkbox.IsChecked ? "Yes" : "Off";
                writer.WriteLine($"   /V /{state}");
                writer.WriteLine($"   /AS /{state}");
                break;

            case PdfDropdownBuilder dropdown:
                writer.WriteLine("   /FT /Ch");

                int ddFlags = 1 << 17; // Combo box
                if (dropdown.AllowEdit) ddFlags |= 1 << 18;
                if (dropdown.Sort) ddFlags |= 1 << 19;
                if (dropdown.IsReadOnly) ddFlags |= 1;
                if (dropdown.IsRequired) ddFlags |= 1 << 1;
                writer.WriteLine($"   /Ff {ddFlags}");

                // Options
                writer.Write("   /Opt [");
                foreach (PdfDropdownOption opt in dropdown.Options)
                {
                    writer.Write(opt.Value == opt.DisplayText
                        ? $" {PdfString(opt.Value)}"
                        : $" [{PdfString(opt.Value)} {PdfString(opt.DisplayText)}]");
                }
                writer.WriteLine(" ]");

                if (!string.IsNullOrEmpty(dropdown.SelectedValue))
                    writer.WriteLine($"   /V {PdfString(dropdown.SelectedValue)}");

                // Default appearance
                int ddFontIndex = fonts.IndexOf(dropdown.FontName) + 1;
                PdfColor ddc = dropdown.TextColor;
                string ddFontSize = dropdown.FontSize > 0 ? $"{dropdown.FontSize:F1}" : "0";
                writer.WriteLine($"   /DA (/F{ddFontIndex} {ddFontSize} Tf {ddc.R:F3} {ddc.G:F3} {ddc.B:F3} rg)");
                break;

            case PdfSignatureFieldBuilder:
                writer.WriteLine("   /FT /Sig");

                var sigFlags = 0;
                if (field.IsReadOnly) sigFlags |= 1;
                if (field.IsRequired) sigFlags |= 1 << 1;
                if (sigFlags != 0)
                    writer.WriteLine($"   /Ff {sigFlags}");
                break;
        }

        // Border style
        if (field.BorderWidth > 0)
        {
            writer.WriteLine("   /BS <<");
            writer.WriteLine($"      /W {field.BorderWidth:F1}");

            string styleCode = field.BorderStyle switch
            {
                PdfBorderStyle.Solid => "S",
                PdfBorderStyle.Dashed => "D",
                PdfBorderStyle.Beveled => "B",
                PdfBorderStyle.Inset => "I",
                PdfBorderStyle.Underline => "U",
                _ => "S"
            };
            writer.WriteLine($"      /S /{styleCode}");

            if (field is { BorderStyle: PdfBorderStyle.Dashed, DashPattern: not null })
            {
                writer.Write("      /D [");
                foreach (double d in field.DashPattern)
                    writer.Write($" {d:F1}");
                writer.WriteLine(" ]");
            }
            writer.WriteLine("   >>");
        }

        // Border color and background (MK dictionary)
        if (field.BorderColor.HasValue || field.BackgroundColor.HasValue)
        {
            writer.WriteLine("   /MK <<");
            if (field.BorderColor.HasValue)
            {
                PdfColor bc = field.BorderColor.Value;
                writer.WriteLine($"      /BC [{bc.R:F3} {bc.G:F3} {bc.B:F3}]");
            }
            if (field.BackgroundColor.HasValue)
            {
                PdfColor bg = field.BackgroundColor.Value;
                writer.WriteLine($"      /BG [{bg.R:F3} {bg.G:F3} {bg.B:F3}]");
            }
            writer.WriteLine("   >>");
        }

        writer.WriteLine(">>");
        WriteObjectEnd(writer);
    }

    private static string PdfString(string text)
    {
        return $"({EscapePdfString(text)})";
    }

    private static string EscapePdfString(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private static string PdfDate(DateTime date)
    {
        return $"(D:{date:yyyyMMddHHmmss})";
    }

    private void WriteImageXObject(StreamWriter writer, int objectNumber, PdfImageContent image)
    {
        // Detect image format and get dimensions
        (byte[] imageData, int width, int height, string colorSpace, int bitsPerComponent, string filter) = ProcessImage(image);

        WriteObjectStart(writer, objectNumber);
        writer.WriteLine("<<");
        writer.WriteLine("   /Type /XObject");
        writer.WriteLine("   /Subtype /Image");
        writer.WriteLine($"   /Width {width}");
        writer.WriteLine($"   /Height {height}");
        writer.WriteLine($"   /ColorSpace /{colorSpace}");
        writer.WriteLine($"   /BitsPerComponent {bitsPerComponent}");

        if (!string.IsNullOrEmpty(filter))
        {
            writer.WriteLine($"   /Filter /{filter}");
        }

        if (image.Interpolate)
        {
            writer.WriteLine("   /Interpolate true");
        }

        writer.WriteLine($"   /Length {imageData.Length}");
        writer.WriteLine(">>");
        writer.WriteLine("stream");
        writer.Flush();
        writer.BaseStream.Write(imageData, 0, imageData.Length);
        writer.WriteLine();
        writer.WriteLine("endstream");
        WriteObjectEnd(writer);
    }

    private (byte[] data, int width, int height, string colorSpace, int bitsPerComponent, string filter) ProcessImage(PdfImageContent image)
    {
        byte[] data = image.ImageData;

        // Check for JPEG signature (FFD8FF)
        if (data is [0xFF, 0xD8, 0xFF, ..])
        {
            // JPEG image - can pass through directly
            (int width, int height) = GetJpegDimensions(data);
            return (data, width, height, "DeviceRGB", 8, "DCTDecode");
        }

        // For all other formats (PNG, BMP, etc.), use ImageSharp to decode
        try
        {
            using var memStream = new MemoryStream(data);
            using Image<Rgb24> imageData = Image.Load<Rgb24>(memStream);

            int width = imageData.Width;
            int height = imageData.Height;

            // Convert to RGB888 format
            var rgbData = new byte[width * height * 3];
            var idx = 0;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    Rgb24 pixel = imageData[x, y];
                    rgbData[idx++] = pixel.R;
                    rgbData[idx++] = pixel.G;
                    rgbData[idx++] = pixel.B;
                }
            }

            switch (image.Compression)
            {
                // Compress based on settings
                case PdfImageCompression.Jpeg:
                {
                    byte[] compressed = CompressJpeg(imageData, image.JpegQuality);
                    return (compressed, width, height, "DeviceRGB", 8, "DCTDecode");
                }
                case PdfImageCompression.Flate or PdfImageCompression.Auto:
                {
                    byte[] compressed = CompressFlate(rgbData);
                    return (compressed, width, height, "DeviceRGB", 8, "FlateDecode");
                }
                case PdfImageCompression.None:
                    return (rgbData, width, height, "DeviceRGB", 8, "");
                default:
                {
                    // Default: Flate compression
                    byte[] defaultCompressed = CompressFlate(rgbData);
                    return (defaultCompressed, width, height, "DeviceRGB", 8, "FlateDecode");
                }
            }
        }
        catch
        {
            // If ImageSharp fails, return a placeholder
            return ([], 100, 100, "DeviceRGB", 8, "");
        }
    }

    private (int width, int height) GetJpegDimensions(byte[] data)
    {
        var i = 2;
        while (i < data.Length - 9)
        {
            if (data[i] != 0xFF)
            {
                i++;
                continue;
            }

            byte marker = data[i + 1];

            switch (marker)
            {
                // SOF markers (Start of Frame)
                case >= 0xC0 and <= 0xCF when marker != 0xC4 && marker != 0xC8 && marker != 0xCC:
                {
                    int height = (data[i + 5] << 8) | data[i + 6];
                    int width = (data[i + 7] << 8) | data[i + 8];
                    return (width, height);
                }
                // Skip to next marker
                // SOI or EOI
                case 0xD8 or 0xD9:
                // RST markers
                case >= 0xD0 and <= 0xD7:
                    i += 2;
                    break;
                default:
                {
                    int length = (data[i + 2] << 8) | data[i + 3];
                    i += 2 + length;
                    break;
                }
            }
        }

        // Default fallback
        return (100, 100);
    }

    private byte[] CompressFlate(byte[] data)
    {
        using var outputStream = new MemoryStream();

        // Write zlib header (RFC 1950)
        // CMF = 0x78 (CM=8 deflate, CINFO=7 for 32K window)
        // FLG = 0x9C (FCHECK makes header checkable, no dict, default compression)
        outputStream.WriteByte(0x78);
        outputStream.WriteByte(0x9C);

        // Write deflate-compressed data
        using (var deflateStream = new DeflateStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflateStream.Write(data, 0, data.Length);
        }

        // Write Adler-32 checksum (big-endian)
        uint adler = ComputeAdler32(data);
        outputStream.WriteByte((byte)(adler >> 24));
        outputStream.WriteByte((byte)(adler >> 16));
        outputStream.WriteByte((byte)(adler >> 8));
        outputStream.WriteByte((byte)adler);

        return outputStream.ToArray();
    }

    private uint ComputeAdler32(byte[] data)
    {
        const uint modAdler = 65521;
        uint a = 1, b = 0;

        foreach (byte c in data)
        {
            a = (a + c) % modAdler;
            b = (b + a) % modAdler;
        }

        return (b << 16) | a;
    }

    private byte[] CompressJpeg(Image<Rgb24> image, int quality)
    {
        // Encode as JPEG using ImageSharp
        using var memStream = new MemoryStream();
        var encoder = new JpegEncoder { Quality = quality };
        image.Save(memStream, encoder);
        return memStream.ToArray();
    }

    /// <summary>
    /// Write a TrueType font with embedding
    /// </summary>
    private void WriteTrueTypeFont(StreamWriter writer, string fontAlias, CustomFontInfo fontInfo)
    {
        int fontObj = _fontObjects[fontAlias];
        int descriptorObj = _fontDescriptorObjects[fontAlias];
        int fontFileObj = descriptorObj + 1; // FontFile2 comes right after descriptor

        EmbeddedFontMetrics metrics = fontInfo.Metrics;

        // Write Font Dictionary
        WriteObjectStart(writer, fontObj);
        writer.WriteLine("<< /Type /Font");
        writer.WriteLine("   /Subtype /TrueType");
        writer.WriteLine($"   /BaseFont /{SanitizeFontName(fontInfo.PostScriptName)}");
        writer.WriteLine($"   /FontDescriptor {descriptorObj} 0 R");
        writer.WriteLine("   /Encoding /WinAnsiEncoding");

        // Write FirstChar and LastChar (WinAnsi is 32-255)
        writer.WriteLine("   /FirstChar 32");
        writer.WriteLine("   /LastChar 255");

        // Generate Widths array (scaled to PDF's 1000-unit coordinate system)
        double scale = 1000.0 / metrics.UnitsPerEm;
        writer.Write("   /Widths [");
        for (var charCode = 32; charCode <= 255; charCode++)
        {
            if ((charCode - 32) % 16 == 0)
                writer.Write("\n      ");

            ushort rawWidth = metrics.GetCharacterAdvanceWidth((ushort)charCode);
            var width = (int)Math.Round(rawWidth * scale);
            writer.Write($"{width} ");
        }
        writer.WriteLine("\n   ]");

        writer.WriteLine(">>");
        WriteObjectEnd(writer);

        // Write FontDescriptor
        WriteFontDescriptor(writer, descriptorObj, fontFileObj, fontInfo);

        // Write FontFile2 stream (embedded TrueType data)
        WriteFontFile2Stream(writer, fontFileObj, fontInfo.FontData);
    }

    /// <summary>
    /// Write font descriptor object
    /// </summary>
    private void WriteFontDescriptor(StreamWriter writer, int descriptorObj, int fontFileObj, CustomFontInfo fontInfo)
    {
        EmbeddedFontMetrics metrics = fontInfo.Metrics;

        WriteObjectStart(writer, descriptorObj);
        writer.WriteLine("<< /Type /FontDescriptor");
        writer.WriteLine($"   /FontName /{SanitizeFontName(fontInfo.PostScriptName)}");

        // Font flags (see PDF spec section 9.8.2)
        // Bit 1 (0x01): FixedPitch
        // Bit 6 (0x20): Symbolic (not set for standard fonts)
        // Bit 7 (0x40): Nonsymbolic (set for standard fonts with WinAnsi)
        const int flags = 0x40; // Nonsymbolic
        writer.WriteLine($"   /Flags {flags}");

        // Font bounding box (estimate if not available)
        // Scale from font units to 1000-unit coordinate system
        double scale = 1000.0 / metrics.UnitsPerEm;
        const int llx = -200;  // Typical left bearing
        var lly = (int)(metrics.Descender * scale);
        const int urx = 1000;  // Typical right edge
        var ury = (int)(metrics.Ascender * scale);

        writer.WriteLine($"   /FontBBox [{llx} {lly} {urx} {ury}]");
        writer.WriteLine("   /ItalicAngle 0");
        writer.WriteLine($"   /Ascent {(int)(metrics.Ascender * scale)}");
        writer.WriteLine($"   /Descent {(int)(metrics.Descender * scale)}");
        writer.WriteLine($"   /CapHeight {(int)(metrics.Ascender * scale * 0.7)}"); // Estimate
        writer.WriteLine("   /StemV 80"); // Estimate - would need complex font analysis for exact value

        // Reference to embedded font program
        writer.WriteLine($"   /FontFile2 {fontFileObj} 0 R");

        writer.WriteLine(">>");
        WriteObjectEnd(writer);
    }

    /// <summary>
    /// Write FontFile2 stream with embedded TrueType data
    /// </summary>
    private void WriteFontFile2Stream(StreamWriter writer, int fontFileObj, byte[] fontData)
    {
        // Optionally compress the font data
        byte[] compressedData = CompressFlate(fontData);

        WriteObjectStart(writer, fontFileObj);
        writer.WriteLine($"<< /Length {compressedData.Length}");
        writer.WriteLine($"   /Length1 {fontData.Length}"); // Original uncompressed length
        writer.WriteLine("   /Filter /FlateDecode");
        writer.WriteLine(">>");
        writer.WriteLine("stream");
        writer.Flush();
        writer.BaseStream.Write(compressedData, 0, compressedData.Length);
        writer.WriteLine();
        writer.WriteLine("endstream");
        WriteObjectEnd(writer);
    }

    /// <summary>
    /// Sanitize the font name for PDF (remove spaces and special characters)
    /// </summary>
    private static string SanitizeFontName(string name)
    {
        return name
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "")
            .Replace(",", "")
            .Replace(".", "");
    }
}
