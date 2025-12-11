using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Compressors.Jpeg2000;
using PdfLibrary.Builder.Annotation;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Builder.FormField;
using PdfLibrary.Builder.Layer;
using PdfLibrary.Builder.Page;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Security;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace PdfLibrary.Builder;

/// <summary>
/// Key for graphics state dictionary lookup
/// </summary>
internal record GraphicsStateKey(
    double FillOpacity,
    double StrokeOpacity,
    bool FillOverprint,
    bool StrokeOverprint,
    int OverprintMode,
    string? BlendMode);

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
    private readonly Dictionary<GraphicsStateKey, int> _extGStateObjects = new();
    private readonly Dictionary<string, int> _separationColorSpaces = new(); // ColorantName -> ObjectNumber
    private readonly Dictionary<int, int> _layerObjects = new(); // Layer.Id -> object number
    private readonly Dictionary<int, int> _bookmarkObjects = new(); // Bookmark.Id -> object number
    private readonly Dictionary<int, int> _annotationObjects = new(); // Annotation.Id -> object number
    private int _outlinesRootObj; // Root Outlines dictionary object number
    private PdfEncryptor? _encryptor;
    private byte[]? _documentId;

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
        _extGStateObjects.Clear();
        _separationColorSpaces.Clear();
        _layerObjects.Clear();
        _bookmarkObjects.Clear();
        _annotationObjects.Clear();
        _outlinesRootObj = 0;
        _encryptor = null;
        _documentId = null;

        // Generate document ID (required for encryption, good practice anyway)
        _documentId = GenerateDocumentId();

        // Set up encryption if configured
        PdfEncryptionSettings? encryptionSettings = builder.EncryptionSettings;
        int? encryptObj = null;
        if (encryptionSettings != null)
        {
            // Convert builder encryption method to Security encryption method
            PdfEncryptionMethod securityMethod = encryptionSettings.Method switch
            {
                PdfEncryptionMethod.Rc4_40 => PdfEncryptionMethod.Rc4_40,
                PdfEncryptionMethod.Rc4_128 => PdfEncryptionMethod.Rc4_128,
                PdfEncryptionMethod.Aes128 => PdfEncryptionMethod.Aes128,
                PdfEncryptionMethod.Aes256 => PdfEncryptionMethod.Aes256,
                _ => PdfEncryptionMethod.Aes256
            };

            _encryptor = new PdfEncryptor(
                encryptionSettings.UserPassword,
                encryptionSettings.OwnerPassword,
                encryptionSettings.PermissionFlags,
                securityMethod,
                _documentId);
        }

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
                    var key = new GraphicsStateKey(
                        FillOpacity: image.Opacity,
                        StrokeOpacity: image.Opacity,
                        FillOverprint: false,
                        StrokeOverprint: false,
                        OverprintMode: 0,
                        BlendMode: null);
                    if (!_extGStateObjects.ContainsKey(key))
                    {
                        _extGStateObjects[key] = _nextObjectNumber++;
                    }
                }
            }

            // Collect graphics state from path content
            foreach (PdfContentElement content in page.Content)
            {
                if (content is not PdfPathContent path) continue;

                bool needsExtGState = path.FillOpacity < 1.0 || path.StrokeOpacity < 1.0 ||
                                     path.FillOverprint || path.StrokeOverprint ||
                                     path.OverprintMode != 0 || path.BlendMode != null;
                if (!needsExtGState) continue;

                var key = new GraphicsStateKey(
                    FillOpacity: path.FillOpacity,
                    StrokeOpacity: path.StrokeOpacity,
                    FillOverprint: path.FillOverprint,
                    StrokeOverprint: path.StrokeOverprint,
                    OverprintMode: path.OverprintMode,
                    BlendMode: path.BlendMode);
                if (!_extGStateObjects.ContainsKey(key))
                {
                    _extGStateObjects[key] = _nextObjectNumber++;
                }
            }

            // Collect Separation color spaces from all content
            foreach (PdfContentElement content in page.Content)
            {
                if (content is PdfPathContent path)
                {
                    if (path.FillColor.HasValue && path.FillColor.Value.ColorSpace == PdfColorSpace.Separation)
                    {
                        string colorantName = path.FillColor.Value.ColorantName ?? "Unknown";
                        if (!_separationColorSpaces.ContainsKey(colorantName))
                        {
                            _separationColorSpaces[colorantName] = _nextObjectNumber++;
                        }
                    }
                    if (path.StrokeColor.HasValue && path.StrokeColor.Value.ColorSpace == PdfColorSpace.Separation)
                    {
                        string colorantName = path.StrokeColor.Value.ColorantName ?? "Unknown";
                        if (!_separationColorSpaces.ContainsKey(colorantName))
                        {
                            _separationColorSpaces[colorantName] = _nextObjectNumber++;
                        }
                    }
                }
                else if (content is PdfRectangleContent rect)
                {
                    if (rect.FillColor.HasValue && rect.FillColor.Value.ColorSpace == PdfColorSpace.Separation)
                    {
                        string colorantName = rect.FillColor.Value.ColorantName ?? "Unknown";
                        if (!_separationColorSpaces.ContainsKey(colorantName))
                        {
                            _separationColorSpaces[colorantName] = _nextObjectNumber++;
                        }
                    }
                    if (rect.StrokeColor.HasValue && rect.StrokeColor.Value.ColorSpace == PdfColorSpace.Separation)
                    {
                        string colorantName = rect.StrokeColor.Value.ColorantName ?? "Unknown";
                        if (!_separationColorSpaces.ContainsKey(colorantName))
                        {
                            _separationColorSpaces[colorantName] = _nextObjectNumber++;
                        }
                    }
                }
                else if (content is PdfLineContent line)
                {
                    if (line.StrokeColor.ColorSpace == PdfColorSpace.Separation)
                    {
                        string colorantName = line.StrokeColor.ColorantName ?? "Unknown";
                        if (!_separationColorSpaces.ContainsKey(colorantName))
                        {
                            _separationColorSpaces[colorantName] = _nextObjectNumber++;
                        }
                    }
                }
                else if (content is PdfTextContent text)
                {
                    if (text.FillColor.ColorSpace == PdfColorSpace.Separation)
                    {
                        string colorantName = text.FillColor.ColorantName ?? "Unknown";
                        if (!_separationColorSpaces.ContainsKey(colorantName))
                        {
                            _separationColorSpaces[colorantName] = _nextObjectNumber++;
                        }
                    }
                    if (text.StrokeColor.HasValue && text.StrokeColor.Value.ColorSpace == PdfColorSpace.Separation)
                    {
                        string colorantName = text.StrokeColor.Value.ColorantName ?? "Unknown";
                        if (!_separationColorSpaces.ContainsKey(colorantName))
                        {
                            _separationColorSpaces[colorantName] = _nextObjectNumber++;
                        }
                    }
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

        // Encryption object (if needed)
        if (_encryptor != null)
        {
            encryptObj = _nextObjectNumber++;
        }

        // Reserve layer (OCG) objects
        if (builder.Layers.Count > 0)
        {
            foreach (PdfLayer layer in builder.Layers)
            {
                _layerObjects[layer.Id] = _nextObjectNumber++;
            }
        }

        // Reserve bookmark (outline) objects
        if (builder.Bookmarks.Count > 0)
        {
            _outlinesRootObj = _nextObjectNumber++;
            ReserveBookmarkObjects(builder.Bookmarks);
        }

        // Reserve annotation objects for each page
        foreach (PdfPageBuilder page in builder.Pages)
        {
            foreach (PdfAnnotation annotation in page.Annotations)
            {
                _annotationObjects[annotation.Id] = _nextObjectNumber++;
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
        if (builder.Bookmarks.Count > 0)
        {
            writer.WriteLine($"   /Outlines {_outlinesRootObj} 0 R");
            writer.WriteLine("   /PageMode /UseOutlines"); // Open with bookmarks panel visible
        }
        if (builder.Layers.Count > 0)
        {
            WriteOCPropertiesInline(writer, builder.Layers);
        }
        if (builder.PageLabelRanges.Count > 0)
        {
            WritePageLabelsInline(writer, builder.PageLabelRanges);
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
            writer.WriteLine($"   /Title {PdfEncryptedString(meta.Title, infoObj)}");
        if (!string.IsNullOrEmpty(meta.Author))
            writer.WriteLine($"   /Author {PdfEncryptedString(meta.Author, infoObj)}");
        if (!string.IsNullOrEmpty(meta.Subject))
            writer.WriteLine($"   /Subject {PdfEncryptedString(meta.Subject, infoObj)}");
        if (!string.IsNullOrEmpty(meta.Keywords))
            writer.WriteLine($"   /Keywords {PdfEncryptedString(meta.Keywords, infoObj)}");
        if (!string.IsNullOrEmpty(meta.Creator))
            writer.WriteLine($"   /Creator {PdfEncryptedString(meta.Creator, infoObj)}");

        string producer = meta.Producer ?? "PdfLibrary";
        writer.WriteLine($"   /Producer {PdfEncryptedString(producer, infoObj)}");

        DateTime creationDate = meta.CreationDate ?? DateTime.Now;
        writer.WriteLine($"   /CreationDate {PdfEncryptedDate(creationDate, infoObj)}");

        if (meta.ModificationDate.HasValue)
            writer.WriteLine($"   /ModDate {PdfEncryptedDate(meta.ModificationDate.Value, infoObj)}");

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

        // Write ExtGState objects
        foreach ((GraphicsStateKey key, int objNum) in _extGStateObjects)
        {
            WriteObjectStart(writer, objNum);
            writer.WriteLine("<<");
            writer.WriteLine("   /Type /ExtGState");
            writer.WriteLine($"   /ca {key.FillOpacity:F2}"); // Non-stroking (fill) alpha
            writer.WriteLine($"   /CA {key.StrokeOpacity:F2}"); // Stroking alpha
            if (key.FillOverprint)
                writer.WriteLine("   /op true");
            if (key.StrokeOverprint)
                writer.WriteLine("   /OP true");
            if (key.OverprintMode != 0)
                writer.WriteLine($"   /OPM {key.OverprintMode}");
            if (key.BlendMode != null)
                writer.WriteLine($"   /BM /{key.BlendMode}");
            writer.WriteLine(">>");
            WriteObjectEnd(writer);
        }

        // Write Separation color space objects
        foreach ((string colorantName, int objNum) in _separationColorSpaces)
        {
            WriteObjectStart(writer, objNum);
            writer.WriteLine($"[/Separation /{colorantName} /DeviceCMYK <<");
            writer.WriteLine("   /FunctionType 2");
            writer.WriteLine("   /Domain [0 1]");
            writer.WriteLine("   /C0 [0 0 0 0]"); // White at tint=0

            // Get CMYK values for this colorant
            (double c, double m, double y, double k) = GetCmykForColorant(colorantName);
            writer.WriteLine($"   /C1 [{c:F2} {m:F2} {y:F2} {k:F2}]"); // Colorant at tint=1

            writer.WriteLine("   /N 1");
            writer.WriteLine(">>]");
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

            // Add ExtGState dictionary
            if (_extGStateObjects.Count > 0)
            {
                writer.WriteLine("      /ExtGState <<");
                var gsIndex = 1;
                foreach ((GraphicsStateKey key, int objNum) in _extGStateObjects.OrderBy(x => x.Value))
                {
                    writer.WriteLine($"         /GS{gsIndex} {objNum} 0 R");
                    gsIndex++;
                }
                writer.WriteLine("      >>");
            }

            // Add ColorSpace dictionary for Separation colors
            if (_separationColorSpaces.Count > 0)
            {
                writer.WriteLine("      /ColorSpace <<");
                var csIndex = 1;
                foreach ((string colorantName, int objNum) in _separationColorSpaces.OrderBy(x => x.Value))
                {
                    writer.WriteLine($"         /CS{csIndex} {objNum} 0 R");
                    csIndex++;
                }
                writer.WriteLine("      >>");
            }

            // Add Properties dictionary for layers (OCGs)
            HashSet<PdfLayer> pageLayers = CollectPageLayers(page);
            if (pageLayers.Count > 0)
            {
                writer.WriteLine("      /Properties <<");
                foreach (PdfLayer layer in pageLayers)
                {
                    if (!_layerObjects.TryGetValue(layer.Id, out int layerObjNum))
                    {
                        throw new InvalidOperationException(
                            $"Layer '{layer.Name}' (ID={layer.Id}) was used on a page but not defined at document level via DefineLayer(). " +
                            $"Registered layer IDs: [{string.Join(", ", _layerObjects.Keys)}]");
                    }
                    writer.WriteLine($"         /{layer.ResourceName} {layerObjNum} 0 R");
                }
                writer.WriteLine("      >>");
            }
            writer.WriteLine("   >>");

            // Annotations (form fields and annotations)
            bool hasAnnotations = page.FormFields.Count > 0 || page.Annotations.Count > 0;
            if (hasAnnotations)
            {
                writer.Write("   /Annots [");
                var annotCount = 0;
                // Form fields first
                for (var j = 0; j < page.FormFields.Count; j++)
                {
                    if (annotCount > 0) writer.Write(" ");
                    writer.Write($"{fieldObjects[fieldIndex + j]} 0 R");
                    annotCount++;
                }
                // Then annotations (links, notes, highlights)
                foreach (PdfAnnotation annotation in page.Annotations)
                {
                    if (annotCount > 0) writer.Write(" ");
                    writer.Write($"{_annotationObjects[annotation.Id]} 0 R");
                    annotCount++;
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

        // Write Encryption dictionary (if needed)
        if (encryptObj.HasValue && _encryptor != null)
        {
            WriteEncryptionDictionary(writer, encryptObj.Value);
        }

        // Write Layer (OCG) objects
        foreach (PdfLayer layer in builder.Layers)
        {
            WriteLayerObject(writer, layer);
        }

        // Write Bookmark (Outline) objects
        if (builder.Bookmarks.Count > 0)
        {
            WriteOutlineObjects(writer, builder.Bookmarks, pageObjectNumbers);
        }

        // Write Annotation objects
        for (var pageIdx = 0; pageIdx < builder.Pages.Count; pageIdx++)
        {
            PdfPageBuilder page = builder.Pages[pageIdx];
            foreach (PdfAnnotation annotation in page.Annotations)
            {
                WriteAnnotationObject(writer, annotation, pageObjectNumbers[pageIdx].pageObj, pageObjectNumbers);
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

        // Add /ID array (required for encryption, recommended otherwise)
        if (_documentId != null)
        {
            string idHex = BytesToHexString(_documentId);
            writer.WriteLine($"   /ID [<{idHex}> <{idHex}>]");
        }

        // Add /Encrypt reference
        if (encryptObj.HasValue)
        {
            writer.WriteLine($"   /Encrypt {encryptObj.Value} 0 R");
        }

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

    private static void WriteObjectEnd(StreamWriter writer)
    {
        writer.WriteLine("endobj");
        writer.WriteLine();
    }

    private void WriteStreamObject(StreamWriter writer, int objectNumber, byte[] data)
    {
        // Encrypt the stream data if encryption is enabled
        byte[] streamData = _encryptor != null ? EncryptStream(data, objectNumber) : data;

        WriteObjectStart(writer, objectNumber);
        writer.WriteLine($"<< /Length {streamData.Length} >>");
        writer.WriteLine("stream");
        writer.Flush();
        writer.BaseStream.Write(streamData);
        writer.WriteLine();
        writer.WriteLine("endstream");
        WriteObjectEnd(writer);
    }

    private static List<string> CollectFonts(PdfDocumentBuilder builder)
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

    private static bool HasFormFields(PdfDocumentBuilder builder)
    {
        return builder.Pages.Any(p => p.FormFields.Count > 0);
    }

    private byte[] GenerateContentStream(PdfPageBuilder page, List<string> fonts)
    {
        var sb = new StringBuilder();

        foreach (PdfContentElement element in page.Content)
        {
            GenerateContentElement(sb, element, fonts);
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Generate PDF content stream operators for layer content (wrapped in BDC/EMC)
    /// </summary>
    private void GenerateLayerContent(StringBuilder sb, PdfLayerContent layerContent, List<string> fonts)
    {
        // Begin marked content with the layer reference
        sb.AppendLine($"/OC /{layerContent.Layer.ResourceName} BDC");

        // Generate content for all elements within the layer
        foreach (PdfContentElement element in layerContent.Content)
        {
            GenerateContentElement(sb, element, fonts);
        }

        // End marked content
        sb.AppendLine("EMC");
    }

    /// <summary>
    /// Generate content stream operators for a single content element
    /// </summary>
    private void GenerateContentElement(StringBuilder sb, PdfContentElement element, List<string> fonts)
    {
        switch (element)
        {
            case PdfTextContent text:
                GenerateTextContent(sb, text, fonts);
                break;

            case PdfRectangleContent rect:
                GenerateRectangleContent(sb, rect);
                break;

            case PdfLineContent line:
                GenerateLineContent(sb, line);
                break;

            case PdfImageContent image:
                GenerateImageContent(sb, image);
                break;

            case PdfPathContent path:
                GeneratePathContent(sb, path);
                break;

            case PdfLayerContent nestedLayer:
                GenerateLayerContent(sb, nestedLayer, fonts);
                break;
        }
    }

    /// <summary>
    /// Generate PDF content stream operators for text
    /// </summary>
    private void GenerateTextContent(StringBuilder sb, PdfTextContent text, List<string> fonts)
    {
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
    }

    /// <summary>
    /// Generate PDF content stream operators for a rectangle
    /// </summary>
    private void GenerateRectangleContent(StringBuilder sb, PdfRectangleContent rect)
    {
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
    }

    /// <summary>
    /// Generate PDF content stream operators for a line
    /// </summary>
    private void GenerateLineContent(StringBuilder sb, PdfLineContent line)
    {
        sb.AppendLine("q");
        sb.AppendLine($"{line.LineWidth:F2} w");
        AppendColorOperator(sb, line.StrokeColor, isFill: false);
        sb.AppendLine($"{line.X1:F2} {line.Y1:F2} m");
        sb.AppendLine($"{line.X2:F2} {line.Y2:F2} l");
        sb.AppendLine("S");
        sb.AppendLine("Q");
    }

    /// <summary>
    /// Generate PDF content stream operators for an image
    /// </summary>
    private void GenerateImageContent(StringBuilder sb, PdfImageContent image)
    {
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
                var gsKey = new GraphicsStateKey(
                    FillOpacity: image.Opacity,
                    StrokeOpacity: image.Opacity,
                    FillOverprint: false,
                    StrokeOverprint: false,
                    OverprintMode: 0,
                    BlendMode: null);
                if (_extGStateObjects.ContainsKey(gsKey))
                {
                    int gsIndex = GetExtGStateIndex(gsKey);
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
    }

    /// <summary>
    /// Generate PDF content stream operators for a path
    /// </summary>
    private void GeneratePathContent(StringBuilder sb, PdfPathContent path)
    {
        sb.AppendLine("q"); // Save graphics state

        // Apply graphics state if needed
        bool needsExtGState = path.FillOpacity < 1.0 || path.StrokeOpacity < 1.0 ||
                             path.FillOverprint || path.StrokeOverprint ||
                             path.OverprintMode != 0 || path.BlendMode != null;
        if (needsExtGState)
        {
            var gsKey = new GraphicsStateKey(
                FillOpacity: path.FillOpacity,
                StrokeOpacity: path.StrokeOpacity,
                FillOverprint: path.FillOverprint,
                StrokeOverprint: path.StrokeOverprint,
                OverprintMode: path.OverprintMode,
                BlendMode: path.BlendMode);
            if (_extGStateObjects.ContainsKey(gsKey))
            {
                int gsIndex = GetExtGStateIndex(gsKey);
                sb.AppendLine($"/GS{gsIndex} gs");
            }
        }

        // Apply CTM transformation if specified
        if (path.Transform.HasValue)
        {
            var m = path.Transform.Value;
            sb.AppendLine($"{m.M11:F4} {m.M12:F4} {m.M21:F4} {m.M22:F4} {m.M31:F2} {m.M32:F2} cm");
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
    private void AppendColorOperator(StringBuilder sb, PdfColor color, bool isFill)
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

            case PdfColorSpace.Separation:
                // cs/CS to set color space, scn/SCN to set color value (tint)
                int csIndex = GetColorSpaceIndex(color.ColorantName ?? "Unknown");
                sb.AppendLine(isFill
                    ? $"/CS{csIndex} cs"
                    : $"/CS{csIndex} CS");
                sb.AppendLine(isFill
                    ? $"{color.Tint:F3} scn"
                    : $"{color.Tint:F3} SCN");
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
    /// Get the index of a Separation color space for the given colorant name
    /// </summary>
    private int GetColorSpaceIndex(string colorantName)
    {
        var index = 1;
        foreach (int objNum in _separationColorSpaces.Values.OrderBy(x => x))
        {
            if (_separationColorSpaces[colorantName] == objNum)
                return index;
            index++;
        }
        return -1;
    }

    /// <summary>
    /// Get the index of an ExtGState object for the given graphics state key
    /// </summary>
    private int GetExtGStateIndex(GraphicsStateKey key)
    {
        var index = 1;
        foreach (int objNum in _extGStateObjects.Values.OrderBy(x => x))
        {
            if (_extGStateObjects[key] == objNum)
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
        writer.WriteLine($"   /T {PdfEncryptedString(field.Name, objectNumber)}");

        if (!string.IsNullOrEmpty(field.Tooltip))
            writer.WriteLine($"   /TU {PdfEncryptedString(field.Tooltip, objectNumber)}");

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
                    writer.WriteLine($"   /V {PdfEncryptedString(textField.DefaultValue, objectNumber)}");

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
                        ? $" {PdfEncryptedString(opt.Value, objectNumber)}"
                        : $" [{PdfEncryptedString(opt.Value, objectNumber)} {PdfEncryptedString(opt.DisplayText, objectNumber)}]");
                }
                writer.WriteLine(" ]");

                if (!string.IsNullOrEmpty(dropdown.SelectedValue))
                    writer.WriteLine($"   /V {PdfEncryptedString(dropdown.SelectedValue, objectNumber)}");

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

        // Encrypt the image data if encryption is enabled
        byte[] streamData = _encryptor != null ? EncryptStream(imageData, objectNumber) : imageData;

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

        writer.WriteLine($"   /Length {streamData.Length}");
        writer.WriteLine(">>");
        writer.WriteLine("stream");
        writer.Flush();
        writer.BaseStream.Write(streamData, 0, streamData.Length);
        writer.WriteLine();
        writer.WriteLine("endstream");
        WriteObjectEnd(writer);
    }

    private (byte[] data, int width, int height, string colorSpace, int bitsPerComponent, string filter) ProcessImage(PdfImageContent image)
    {
        byte[] data = image.ImageData;

        // Check for JPEG2000 signatures
        // JP2 format: 00 00 00 0C 6A 50 20 20 0D 0A 87 0A (12-byte signature)
        // J2K codestream: FF 4F FF 51 (SOC + SIZ markers)
        if ((data.Length >= 12 && data is [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A, ..]) ||
            (data.Length >= 4 && data is [0xFF, 0x4F, 0xFF, 0x51, ..]))
        {
            // JPEG2000 image - pass through directly with JPXDecode filter
            (int width, int height, int components) = GetJpeg2000Dimensions(data);
            // Determine color space based on component count
            string colorSpace = components switch
            {
                1 => "DeviceGray",
                3 => "DeviceRGB",
                4 => "DeviceCMYK",
                _ => "DeviceRGB" // Default to RGB
            };
            return (data, width, height, colorSpace, 8, "JPXDecode");
        }

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

    private static (int width, int height) GetJpegDimensions(byte[] data)
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

    private static (int width, int height, int components) GetJpeg2000Dimensions(byte[] data)
    {
        try
        {
            // Use Melville.CSJ2K to parse the JPEG2000 file and extract dimensions
            // This works for both JP2 and J2K formats
            byte[] portableImage = Jpeg2000.Decompress(data, out int width, out int height, out int components);
            return (width, height, components);
        }
        catch
        {
            // If parsing fails, return default values
            return (100, 100, 3);
        }
    }

    private static byte[] CompressFlate(byte[] data)
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

    private static uint ComputeAdler32(byte[] data)
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

    private static byte[] CompressJpeg(Image<Rgb24> image, int quality)
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

        // Encrypt the compressed data if encryption is enabled
        byte[] streamData = _encryptor != null ? EncryptStream(compressedData, fontFileObj) : compressedData;

        WriteObjectStart(writer, fontFileObj);
        writer.WriteLine($"<< /Length {streamData.Length}");
        writer.WriteLine($"   /Length1 {fontData.Length}"); // Original uncompressed length
        writer.WriteLine("   /Filter /FlateDecode");
        writer.WriteLine(">>");
        writer.WriteLine("stream");
        writer.Flush();
        writer.BaseStream.Write(streamData, 0, streamData.Length);
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

    /// <summary>
    /// Generates a unique document ID
    /// </summary>
    private static byte[] GenerateDocumentId()
    {
        // Generate 16 random bytes for the document ID
        var id = new byte[16];
        RandomNumberGenerator.Fill(id);
        return id;
    }

    /// <summary>
    /// Converts a byte array to a hex string
    /// </summary>
    private static string BytesToHexString(byte[] bytes)
    {
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Gets CMYK values for a given separation colorant name.
    /// These are approximate CMYK equivalents for common spot colors used in testing.
    /// </summary>
    private static (double c, double m, double y, double k) GetCmykForColorant(string colorantName)
    {
        // Map colorant names to approximate CMYK values
        // Format: (Cyan, Magenta, Yellow, Black) - each 0.0 to 1.0
        return colorantName switch
        {
            // Standard test colors
            "Orange" => (0.0, 0.5, 1.0, 0.0),        // Orange
            "BrandOrange" => (0.0, 0.6, 0.9, 0.0),   // Slightly different orange

            // PMS colors (approximate CMYK equivalents)
            "PMS485" => (0.0, 1.0, 0.91, 0.0),       // Red (PMS 485)
            "PMS300" => (1.0, 0.44, 0.0, 0.0),       // Blue (PMS 300)
            "PMS375" => (0.42, 0.0, 1.0, 0.0),       // Green (PMS 375)

            // Custom brand colors
            "RefxBlue" => (1.0, 0.5, 0.0, 0.1),      // Custom blue with slight black

            // Fallback for unknown colorants - use a distinctive purple to make it obvious
            _ => (0.5, 1.0, 0.0, 0.0)
        };
    }

    /// <summary>
    /// Writes the encryption dictionary
    /// </summary>
    private void WriteEncryptionDictionary(StreamWriter writer, int objectNumber)
    {
        if (_encryptor == null) return;

        WriteObjectStart(writer, objectNumber);
        writer.WriteLine("<<");
        writer.WriteLine("   /Filter /Standard");
        writer.WriteLine($"   /V {_encryptor.Version}");
        writer.WriteLine($"   /R {_encryptor.Revision}");
        writer.WriteLine($"   /P {_encryptor.Permissions.RawValue}");

        // Write O and U values as hex strings
        writer.WriteLine($"   /O <{BytesToHexString(_encryptor.OValue)}>");
        writer.WriteLine($"   /U <{BytesToHexString(_encryptor.UValue)}>");

        // Key length for V=2,3,4
        if (_encryptor.Version >= 2 && _encryptor.Version <= 4)
        {
            writer.WriteLine($"   /Length {_encryptor.KeyLengthBits}");
        }

        // V=4 specific: Crypt filters
        if (_encryptor.Version == 4)
        {
            writer.WriteLine("   /CF <<");
            writer.WriteLine("      /StdCF <<");
            writer.WriteLine("         /AuthEvent /DocOpen");
            writer.WriteLine("         /CFM /AESV2");
            writer.WriteLine("         /Length 16");
            writer.WriteLine("      >>");
            writer.WriteLine("   >>");
            writer.WriteLine("   /StmF /StdCF");
            writer.WriteLine("   /StrF /StdCF");
        }

        // V=5 specific: AES-256 with additional values
        if (_encryptor.Version == 5)
        {
            writer.WriteLine("   /CF <<");
            writer.WriteLine("      /StdCF <<");
            writer.WriteLine("         /AuthEvent /DocOpen");
            writer.WriteLine("         /CFM /AESV3");
            writer.WriteLine("         /Length 32");
            writer.WriteLine("      >>");
            writer.WriteLine("   >>");
            writer.WriteLine("   /StmF /StdCF");
            writer.WriteLine("   /StrF /StdCF");

            // OE, UE, Perms are required for V=5
            if (_encryptor.OEValue != null)
                writer.WriteLine($"   /OE <{BytesToHexString(_encryptor.OEValue)}>");
            if (_encryptor.UEValue != null)
                writer.WriteLine($"   /UE <{BytesToHexString(_encryptor.UEValue)}>");
            if (_encryptor.PermsValue != null)
                writer.WriteLine($"   /Perms <{BytesToHexString(_encryptor.PermsValue)}>");
        }

        writer.WriteLine(">>");
        WriteObjectEnd(writer);
    }

    /// <summary>
    /// Encrypts a string for the current object being written
    /// </summary>
    private byte[] EncryptString(byte[] data, int objectNumber)
    {
        return _encryptor?.EncryptString(data, objectNumber, 0) ?? data;
    }

    /// <summary>
    /// Encrypts stream data for the current object being written
    /// </summary>
    private byte[] EncryptStream(byte[] data, int objectNumber)
    {
        return _encryptor?.EncryptStream(data, objectNumber, 0) ?? data;
    }

    /// <summary>
    /// Creates an encrypted PDF string from text
    /// </summary>
    private string PdfEncryptedString(string text, int objectNumber)
    {
        if (_encryptor == null)
            return PdfString(text);

        // Convert to bytes and encrypt
        byte[] textBytes = Encoding.Latin1.GetBytes(text);
        byte[] encrypted = EncryptString(textBytes, objectNumber);

        // Return as hex string
        return $"<{BytesToHexString(encrypted)}>";
    }

    /// <summary>
    /// Creates an encrypted PDF date string
    /// </summary>
    private string PdfEncryptedDate(DateTime date, int objectNumber)
    {
        var dateStr = $"D:{date:yyyyMMddHHmmss}";
        if (_encryptor == null)
            return $"({dateStr})";

        byte[] textBytes = Encoding.Latin1.GetBytes(dateStr);
        byte[] encrypted = EncryptString(textBytes, objectNumber);
        return $"<{BytesToHexString(encrypted)}>";
    }

    /// <summary>
    /// Writes the OCProperties dictionary inline in the catalog
    /// </summary>
    private void WriteOCPropertiesInline(StreamWriter writer, IReadOnlyList<PdfLayer> layers)
    {
        writer.WriteLine("   /OCProperties <<");

        // OCGs array - list of all layer object references
        writer.Write("      /OCGs [");
        for (var i = 0; i < layers.Count; i++)
        {
            if (i > 0) writer.Write(" ");
            writer.Write($"{_layerObjects[layers[i].Id]} 0 R");
        }
        writer.WriteLine("]");

        // D - Default viewing configuration
        writer.WriteLine("      /D <<");
        writer.WriteLine("         /Name (Default)");

        // Order array - defines layer order in UI
        writer.Write("         /Order [");
        for (var i = 0; i < layers.Count; i++)
        {
            if (i > 0) writer.Write(" ");
            writer.Write($"{_layerObjects[layers[i].Id]} 0 R");
        }
        writer.WriteLine("]");

        // ON array - layers visible by default
        List<PdfLayer> onLayers = layers.Where(l => l.IsVisibleByDefault).ToList();
        if (onLayers.Count > 0)
        {
            writer.Write("         /ON [");
            for (var i = 0; i < onLayers.Count; i++)
            {
                if (i > 0) writer.Write(" ");
                writer.Write($"{_layerObjects[onLayers[i].Id]} 0 R");
            }
            writer.WriteLine("]");
        }

        // OFF array - layers hidden by default
        List<PdfLayer> offLayers = layers.Where(l => !l.IsVisibleByDefault).ToList();
        if (offLayers.Count > 0)
        {
            writer.Write("         /OFF [");
            for (var i = 0; i < offLayers.Count; i++)
            {
                if (i > 0) writer.Write(" ");
                writer.Write($"{_layerObjects[offLayers[i].Id]} 0 R");
            }
            writer.WriteLine("]");
        }

        // Locked array - layers that cannot be toggled
        List<PdfLayer> lockedLayers = layers.Where(l => l.IsLocked).ToList();
        if (lockedLayers.Count > 0)
        {
            writer.Write("         /Locked [");
            for (var i = 0; i < lockedLayers.Count; i++)
            {
                if (i > 0) writer.Write(" ");
                writer.Write($"{_layerObjects[lockedLayers[i].Id]} 0 R");
            }
            writer.WriteLine("]");
        }

        writer.WriteLine("      >>");
        writer.WriteLine("   >>");
    }

    /// <summary>
    /// Writes a layer (OCG) object
    /// </summary>
    private void WriteLayerObject(StreamWriter writer, PdfLayer layer)
    {
        int objNum = _layerObjects[layer.Id];
        WriteObjectStart(writer, objNum);
        writer.WriteLine("<< /Type /OCG");
        writer.WriteLine($"   /Name {PdfEncryptedString(layer.Name, objNum)}");

        // Intent
        if (layer.Intent != PdfLayerIntent.View)
        {
            string intent = layer.Intent switch
            {
                PdfLayerIntent.Design => "/Design",
                PdfLayerIntent.All => "[/View /Design]",
                _ => "/View"
            };
            writer.WriteLine($"   /Intent {intent}");
        }

        // Usage dictionary for print/export states
        if (layer.PrintState.HasValue || layer.ExportState.HasValue)
        {
            writer.WriteLine("   /Usage <<");
            if (layer.PrintState.HasValue)
            {
                string printState = layer.PrintState.Value ? "/ON" : "/OFF";
                writer.WriteLine($"      /Print << /PrintState {printState} >>");
            }
            if (layer.ExportState.HasValue)
            {
                string exportState = layer.ExportState.Value ? "/ON" : "/OFF";
                writer.WriteLine($"      /Export << /ExportState {exportState} >>");
            }
            writer.WriteLine("   >>");
        }

        writer.WriteLine(">>");
        WriteObjectEnd(writer);
    }

    /// <summary>
    /// Collects all layers used on a page
    /// </summary>
    private static HashSet<PdfLayer> CollectPageLayers(PdfPageBuilder page)
    {
        var layers = new HashSet<PdfLayer>();
        foreach (PdfContentElement element in page.Content)
        {
            if (element is PdfLayerContent layerContent)
            {
                layers.Add(layerContent.Layer);
            }
        }
        return layers;
    }

    /// <summary>
    /// Recursively reserves object numbers for bookmarks
    /// </summary>
    private void ReserveBookmarkObjects(IEnumerable<PdfBookmark> bookmarks)
    {
        foreach (PdfBookmark bookmark in bookmarks)
        {
            _bookmarkObjects[bookmark.Id] = _nextObjectNumber++;
            if (bookmark.Children.Count > 0)
            {
                ReserveBookmarkObjects(bookmark.Children);
            }
        }
    }

    /// <summary>
    /// Writes the outline (bookmark) objects
    /// </summary>
    private void WriteOutlineObjects(StreamWriter writer, IReadOnlyList<PdfBookmark> bookmarks,
        List<(int pageObj, int contentObj)> pageObjectNumbers)
    {
        // Write the root Outlines dictionary
        WriteObjectStart(writer, _outlinesRootObj);
        writer.WriteLine("<< /Type /Outlines");

        // First and Last point to first and last top-level bookmarks
        int firstBookmarkObj = _bookmarkObjects[bookmarks[0].Id];
        int lastBookmarkObj = _bookmarkObjects[bookmarks[^1].Id];
        writer.WriteLine($"   /First {firstBookmarkObj} 0 R");
        writer.WriteLine($"   /Last {lastBookmarkObj} 0 R");

        // Count is total number of open outline items at all levels
        int totalCount = CountOpenOutlineItems(bookmarks);
        writer.WriteLine($"   /Count {totalCount}");

        writer.WriteLine(">>");
        WriteObjectEnd(writer);

        // Write each bookmark
        WriteBookmarkObjects(writer, bookmarks, _outlinesRootObj, pageObjectNumbers);
    }

    /// <summary>
    /// Recursively writes bookmark objects
    /// </summary>
    private void WriteBookmarkObjects(StreamWriter writer, IReadOnlyList<PdfBookmark> bookmarks,
        int parentObj, List<(int pageObj, int contentObj)> pageObjectNumbers)
    {
        for (var i = 0; i < bookmarks.Count; i++)
        {
            PdfBookmark bookmark = bookmarks[i];
            int objNum = _bookmarkObjects[bookmark.Id];

            WriteObjectStart(writer, objNum);
            writer.WriteLine("<<");

            // Title (required)
            writer.WriteLine($"   /Title {PdfEncryptedString(bookmark.Title, objNum)}");

            // Parent (required)
            writer.WriteLine($"   /Parent {parentObj} 0 R");

            // Prev (if not first)
            if (i > 0)
            {
                int prevObj = _bookmarkObjects[bookmarks[i - 1].Id];
                writer.WriteLine($"   /Prev {prevObj} 0 R");
            }

            // Next (if not last)
            if (i < bookmarks.Count - 1)
            {
                int nextObj = _bookmarkObjects[bookmarks[i + 1].Id];
                writer.WriteLine($"   /Next {nextObj} 0 R");
            }

            // First and Last (if has children)
            if (bookmark.Children.Count > 0)
            {
                int firstChildObj = _bookmarkObjects[bookmark.Children[0].Id];
                int lastChildObj = _bookmarkObjects[bookmark.Children[^1].Id];
                writer.WriteLine($"   /First {firstChildObj} 0 R");
                writer.WriteLine($"   /Last {lastChildObj} 0 R");

                // Count: positive if open (expanded), negative if closed
                int childCount = CountOpenOutlineItems(bookmark.Children);
                if (bookmark.IsOpen)
                {
                    writer.WriteLine($"   /Count {childCount}");
                }
                else
                {
                    writer.WriteLine($"   /Count -{bookmark.Children.Count}");
                }
            }

            // Destination
            WriteBookmarkDestination(writer, bookmark.Destination, pageObjectNumbers);

            // Text style (C for color, F for flags)
            if (bookmark.TextColor.HasValue)
            {
                PdfColor color = bookmark.TextColor.Value;
                writer.WriteLine($"   /C [{color.R:F3} {color.G:F3} {color.B:F3}]");
            }

            if (bookmark.IsBold || bookmark.IsItalic)
            {
                var flags = 0;
                if (bookmark.IsItalic) flags |= 1;
                if (bookmark.IsBold) flags |= 2;
                writer.WriteLine($"   /F {flags}");
            }

            writer.WriteLine(">>");
            WriteObjectEnd(writer);

            // Recursively write children
            if (bookmark.Children.Count > 0)
            {
                WriteBookmarkObjects(writer, bookmark.Children, objNum, pageObjectNumbers);
            }
        }
    }

    /// <summary>
    /// Writes the destination for a bookmark
    /// </summary>
    private static void WriteBookmarkDestination(StreamWriter writer, PdfDestination dest,
        List<(int pageObj, int contentObj)> pageObjectNumbers)
    {
        // Ensure page index is valid
        int pageIndex = Math.Clamp(dest.PageIndex, 0, pageObjectNumbers.Count - 1);
        int pageObj = pageObjectNumbers[pageIndex].pageObj;

        string destStr = dest.Type switch
        {
            PdfDestinationType.Fit => $"[{pageObj} 0 R /Fit]",
            PdfDestinationType.FitH => $"[{pageObj} 0 R /FitH {FormatDestCoord(dest.Top)}]",
            PdfDestinationType.FitV => $"[{pageObj} 0 R /FitV {FormatDestCoord(dest.Left)}]",
            PdfDestinationType.FitB => $"[{pageObj} 0 R /FitB]",
            PdfDestinationType.FitBH => $"[{pageObj} 0 R /FitBH {FormatDestCoord(dest.Top)}]",
            PdfDestinationType.FitBV => $"[{pageObj} 0 R /FitBV {FormatDestCoord(dest.Left)}]",
            PdfDestinationType.FitR => $"[{pageObj} 0 R /FitR {dest.Left ?? 0} {dest.Bottom ?? 0} {dest.Right ?? 612} {dest.Top ?? 792}]",
            _ => $"[{pageObj} 0 R /XYZ {FormatDestCoord(dest.Left)} {FormatDestCoord(dest.Top)} {FormatDestCoord(dest.Zoom)}]"
        };

        writer.WriteLine($"   /Dest {destStr}");
    }

    /// <summary>
    /// Formats a destination coordinate (null becomes "null" in PDF)
    /// </summary>
    private static string FormatDestCoord(double? value)
    {
        return value.HasValue ? value.Value.ToString("F2") : "null";
    }

    /// <summary>
    /// Counts the total number of open outline items (for /Count in root)
    /// </summary>
    private static int CountOpenOutlineItems(IReadOnlyList<PdfBookmark> bookmarks)
    {
        int count = bookmarks.Count;
        foreach (PdfBookmark bookmark in bookmarks)
        {
            if (bookmark.IsOpen && bookmark.Children.Count > 0)
            {
                count += CountOpenOutlineItems(bookmark.Children);
            }
        }
        return count;
    }

    /// <summary>
    /// Writes the PageLabels dictionary inline in the catalog
    /// </summary>
    private static void WritePageLabelsInline(StreamWriter writer, IReadOnlyList<PdfPageLabelRange> ranges)
    {
        writer.WriteLine("   /PageLabels <<");
        writer.WriteLine("      /Nums [");

        foreach (PdfPageLabelRange range in ranges.OrderBy(r => r.StartPageIndex))
        {
            writer.Write($"         {range.StartPageIndex} << ");

            // Style (S entry)
            if (range.Style != PdfPageLabelStyle.None)
            {
                string styleCode = range.Style switch
                {
                    PdfPageLabelStyle.Decimal => "D",
                    PdfPageLabelStyle.UppercaseRoman => "R",
                    PdfPageLabelStyle.LowercaseRoman => "r",
                    PdfPageLabelStyle.UppercaseLetters => "A",
                    PdfPageLabelStyle.LowercaseLetters => "a",
                    _ => "D"
                };
                writer.Write($"/S /{styleCode} ");
            }

            // Prefix (P entry)
            if (!string.IsNullOrEmpty(range.Prefix))
            {
                writer.Write($"/P ({EscapePdfString(range.Prefix)}) ");
            }

            // Starting number (St entry) - only if not 1
            if (range.StartNumber != 1)
            {
                writer.Write($"/St {range.StartNumber} ");
            }

            writer.WriteLine(">>");
        }

        writer.WriteLine("      ]");
        writer.WriteLine("   >>");
    }

    /// <summary>
    /// Writes an annotation object
    /// </summary>
    private void WriteAnnotationObject(StreamWriter writer, PdfAnnotation annotation, int pageObj,
        List<(int pageObj, int contentObj)> pageObjectNumbers)
    {
        int objNum = _annotationObjects[annotation.Id];
        WriteObjectStart(writer, objNum);
        writer.WriteLine("<<");
        writer.WriteLine("   /Type /Annot");
        writer.WriteLine($"   /Subtype /{annotation.Subtype}");
        writer.WriteLine($"   /Rect [{annotation.Rect.Left:F2} {annotation.Rect.Bottom:F2} {annotation.Rect.Right:F2} {annotation.Rect.Top:F2}]");
        writer.WriteLine($"   /P {pageObj} 0 R");

        // Annotation flags
        if (annotation.Flags != PdfAnnotationFlags.None)
        {
            writer.WriteLine($"   /F {(int)annotation.Flags}");
        }

        // Border
        if (annotation.Border != null)
        {
            writer.Write($"   /Border [{annotation.Border.HorizontalRadius:F1} {annotation.Border.VerticalRadius:F1} {annotation.Border.Width:F1}");
            if (annotation.Border.DashPattern is { Length: > 0 })
            {
                writer.Write(" [");
                foreach (double d in annotation.Border.DashPattern)
                    writer.Write($" {d:F1}");
                writer.Write(" ]");
            }
            writer.WriteLine("]");
        }

        // Type-specific properties
        switch (annotation)
        {
            case PdfLinkAnnotation link:
                WriteLinkAnnotation(writer, link, objNum, pageObjectNumbers);
                break;

            case PdfTextAnnotation text:
                WriteTextAnnotation(writer, text, objNum);
                break;

            case PdfHighlightAnnotation highlight:
                WriteHighlightAnnotation(writer, highlight, objNum);
                break;
        }

        writer.WriteLine(">>");
        WriteObjectEnd(writer);
    }

    /// <summary>
    /// Writes link annotation specific properties
    /// </summary>
    private void WriteLinkAnnotation(StreamWriter writer, PdfLinkAnnotation link, int objNum,
        List<(int pageObj, int contentObj)> pageObjectNumbers)
    {
        // Highlight mode
        string hlMode = link.HighlightMode switch
        {
            PdfLinkHighlightMode.None => "N",
            PdfLinkHighlightMode.Outline => "O",
            PdfLinkHighlightMode.Push => "P",
            _ => "I" // Invert is default
        };
        writer.WriteLine($"   /H /{hlMode}");

        // Action
        switch (link.Action)
        {
            case PdfGoToAction goTo:
                // Write destination directly
                PdfDestination dest = goTo.Destination;
                int pageIndex = Math.Clamp(dest.PageIndex, 0, pageObjectNumbers.Count - 1);
                int pageObj = pageObjectNumbers[pageIndex].pageObj;

                string destStr = dest.Type switch
                {
                    PdfDestinationType.Fit => $"[{pageObj} 0 R /Fit]",
                    PdfDestinationType.FitH => $"[{pageObj} 0 R /FitH {FormatDestCoord(dest.Top)}]",
                    PdfDestinationType.FitV => $"[{pageObj} 0 R /FitV {FormatDestCoord(dest.Left)}]",
                    PdfDestinationType.FitB => $"[{pageObj} 0 R /FitB]",
                    PdfDestinationType.FitBH => $"[{pageObj} 0 R /FitBH {FormatDestCoord(dest.Top)}]",
                    PdfDestinationType.FitBV => $"[{pageObj} 0 R /FitBV {FormatDestCoord(dest.Left)}]",
                    PdfDestinationType.FitR => $"[{pageObj} 0 R /FitR {dest.Left ?? 0} {dest.Bottom ?? 0} {dest.Right ?? 612} {dest.Top ?? 792}]",
                    _ => $"[{pageObj} 0 R /XYZ {FormatDestCoord(dest.Left)} {FormatDestCoord(dest.Top)} {FormatDestCoord(dest.Zoom)}]"
                };
                writer.WriteLine($"   /Dest {destStr}");
                break;

            case PdfUriAction uri:
                // Write URI action
                writer.WriteLine("   /A <<");
                writer.WriteLine("      /Type /Action");
                writer.WriteLine("      /S /URI");
                writer.WriteLine($"      /URI {PdfEncryptedString(uri.Uri, objNum)}");
                writer.WriteLine("   >>");
                break;
        }
    }

    /// <summary>
    /// Writes text annotation specific properties
    /// </summary>
    private void WriteTextAnnotation(StreamWriter writer, PdfTextAnnotation text, int objNum)
    {
        // Contents
        if (!string.IsNullOrEmpty(text.Contents))
        {
            writer.WriteLine($"   /Contents {PdfEncryptedString(text.Contents, objNum)}");
        }

        // Icon name
        string iconName = text.Icon switch
        {
            PdfTextAnnotationIcon.Comment => "Comment",
            PdfTextAnnotationIcon.Key => "Key",
            PdfTextAnnotationIcon.Help => "Help",
            PdfTextAnnotationIcon.NewParagraph => "NewParagraph",
            PdfTextAnnotationIcon.Paragraph => "Paragraph",
            PdfTextAnnotationIcon.Insert => "Insert",
            _ => "Note"
        };
        writer.WriteLine($"   /Name /{iconName}");

        // Open state
        if (text.IsOpen)
        {
            writer.WriteLine("   /Open true");
        }

        // Color
        if (text.Color.HasValue)
        {
            PdfColor c = text.Color.Value;
            writer.WriteLine($"   /C [{c.R:F3} {c.G:F3} {c.B:F3}]");
        }
    }

    /// <summary>
    /// Writes highlight annotation specific properties
    /// </summary>
    private static void WriteHighlightAnnotation(StreamWriter writer, PdfHighlightAnnotation highlight, int objNum)
    {
        // Color
        writer.WriteLine($"   /C [{highlight.Color.R:F3} {highlight.Color.G:F3} {highlight.Color.B:F3}]");

        // QuadPoints
        if (highlight.QuadPoints.Count > 0)
        {
            writer.Write("   /QuadPoints [");
            foreach (PdfQuadPoints quad in highlight.QuadPoints)
            {
                // PDF spec order: x1,y1,x2,y2,x3,y3,x4,y4 (counter-clockwise from bottom-left)
                writer.Write($" {quad.X1:F2} {quad.Y1:F2}");
                writer.Write($" {quad.X2:F2} {quad.Y2:F2}");
                writer.Write($" {quad.X3:F2} {quad.Y3:F2}");
                writer.Write($" {quad.X4:F2} {quad.Y4:F2}");
            }
            writer.WriteLine(" ]");
        }
    }
}
