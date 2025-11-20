using System.Globalization;
using System.Text;

namespace PdfLibrary.Builder;

/// <summary>
/// Writes PDF documents to files or streams
/// </summary>
public class PdfDocumentWriter
{
    private int _nextObjectNumber = 1;
    private readonly List<long> _objectOffsets = new();
    private readonly Dictionary<string, int> _fontObjects = new();

    /// <summary>
    /// Write a document to a file
    /// </summary>
    public void Write(PdfDocumentBuilder builder, string filePath)
    {
        using var stream = File.Create(filePath);
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

        using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.NewLine = "\n";

        // PDF Header
        writer.WriteLine("%PDF-1.7");
        writer.WriteLine("%âãÏÓ"); // Binary marker
        writer.Flush();

        // Collect all fonts used
        var fonts = CollectFonts(builder);

        // Reserve object numbers
        int catalogObj = _nextObjectNumber++;
        int pagesObj = _nextObjectNumber++;
        int infoObj = _nextObjectNumber++;

        // Reserve font objects
        foreach (var font in fonts)
        {
            _fontObjects[font] = _nextObjectNumber++;
        }

        // Reserve page objects and their content streams
        var pageObjectNumbers = new List<(int pageObj, int contentObj)>();
        foreach (var _ in builder.Pages)
        {
            int pageObj = _nextObjectNumber++;
            int contentObj = _nextObjectNumber++;
            pageObjectNumbers.Add((pageObj, contentObj));
        }

        // AcroForm object (if needed)
        int? acroFormObj = null;
        var fieldObjects = new List<int>();
        if (HasFormFields(builder))
        {
            acroFormObj = _nextObjectNumber++;
            // Reserve objects for each field
            foreach (var page in builder.Pages)
            {
                foreach (var _ in page.FormFields)
                {
                    fieldObjects.Add(_nextObjectNumber++);
                }
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
        for (int i = 0; i < pageObjectNumbers.Count; i++)
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
        var meta = builder.Metadata;
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

        var creationDate = meta.CreationDate ?? DateTime.Now;
        writer.WriteLine($"   /CreationDate {PdfDate(creationDate)}");

        if (meta.ModificationDate.HasValue)
            writer.WriteLine($"   /ModDate {PdfDate(meta.ModificationDate.Value)}");

        writer.WriteLine(">>");
        WriteObjectEnd(writer);

        // Write Font objects
        foreach (var font in fonts)
        {
            WriteObjectStart(writer, _fontObjects[font]);
            writer.WriteLine("<< /Type /Font");
            writer.WriteLine("   /Subtype /Type1");
            writer.WriteLine($"   /BaseFont /{font}");
            writer.WriteLine("   /Encoding /WinAnsiEncoding");
            writer.WriteLine(">>");
            WriteObjectEnd(writer);
        }

        // Write Page objects and content
        int fieldIndex = 0;
        for (int i = 0; i < builder.Pages.Count; i++)
        {
            var page = builder.Pages[i];
            var (pageObj, contentObj) = pageObjectNumbers[i];

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
                int fontIndex = 1;
                foreach (var font in fonts)
                {
                    writer.WriteLine($"         /F{fontIndex} {_fontObjects[font]} 0 R");
                    fontIndex++;
                }
                writer.WriteLine("      >>");
            }
            writer.WriteLine("   >>");

            // Annotations (form fields)
            if (page.FormFields.Count > 0)
            {
                writer.Write("   /Annots [");
                for (int j = 0; j < page.FormFields.Count; j++)
                {
                    if (j > 0) writer.Write(" ");
                    writer.Write($"{fieldObjects[fieldIndex + j]} 0 R");
                }
                writer.WriteLine("]");
            }

            writer.WriteLine(">>");
            WriteObjectEnd(writer);

            // Content stream
            var content = GenerateContentStream(page, fonts);
            WriteStreamObject(writer, contentObj, content);

            fieldIndex += page.FormFields.Count;
        }

        // Write AcroForm and fields
        if (acroFormObj.HasValue)
        {
            WriteObjectStart(writer, acroFormObj.Value);
            writer.WriteLine("<<");
            writer.Write("   /Fields [");
            for (int i = 0; i < fieldObjects.Count; i++)
            {
                if (i > 0) writer.Write(" ");
                writer.Write($"{fieldObjects[i]} 0 R");
            }
            writer.WriteLine("]");

            var acroForm = builder.AcroForm;
            if (acroForm != null)
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
            int pageIndex = 0;
            foreach (var page in builder.Pages)
            {
                foreach (var field in page.FormFields)
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
        foreach (var offset in _objectOffsets)
        {
            writer.WriteLine($"{offset:D10} 00000 n ");
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
        _objectOffsets.Add(writer.BaseStream.Position);
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
        var fonts = new HashSet<string>();

        // Default font
        fonts.Add("Helvetica");

        // Fonts from content
        foreach (var page in builder.Pages)
        {
            foreach (var element in page.Content)
            {
                if (element is PdfTextContent text)
                {
                    fonts.Add(text.FontName);
                }
            }

            // Fonts from form fields
            foreach (var field in page.FormFields)
            {
                if (field is PdfTextFieldBuilder textField)
                {
                    fonts.Add(textField.FontName);
                }
                else if (field is PdfDropdownBuilder dropdown)
                {
                    fonts.Add(dropdown.FontName);
                }
            }
        }

        // Font from AcroForm defaults
        if (builder.AcroForm != null)
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

        foreach (var element in page.Content)
        {
            switch (element)
            {
                case PdfTextContent text:
                    int fontIndex = fonts.IndexOf(text.FontName) + 1;
                    sb.AppendLine("BT");
                    sb.AppendLine($"/F{fontIndex} {text.FontSize:F1} Tf");
                    sb.AppendLine($"{text.FillColor.R:F3} {text.FillColor.G:F3} {text.FillColor.B:F3} rg");
                    sb.AppendLine($"{text.X:F2} {text.Y:F2} Td");
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
                            var c = rect.FillColor.Value;
                            sb.AppendLine($"{c.R:F3} {c.G:F3} {c.B:F3} rg");
                        }
                        if (rect.StrokeColor.HasValue)
                        {
                            var c = rect.StrokeColor.Value;
                            sb.AppendLine($"{c.R:F3} {c.G:F3} {c.B:F3} RG");
                        }

                        sb.AppendLine($"{rect.Rect.Left:F2} {rect.Rect.Bottom:F2} {rect.Rect.Width:F2} {rect.Rect.Height:F2} re");

                        if (rect.FillColor.HasValue && rect.StrokeColor.HasValue)
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
                    sb.AppendLine($"{line.StrokeColor.R:F3} {line.StrokeColor.G:F3} {line.StrokeColor.B:F3} RG");
                    sb.AppendLine($"{line.X1:F2} {line.Y1:F2} m");
                    sb.AppendLine($"{line.X2:F2} {line.Y2:F2} l");
                    sb.AppendLine("S");
                    sb.AppendLine("Q");
                    break;

                case PdfImageContent image:
                    // TODO: Implement image support
                    break;
            }
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private void WriteFormField(StreamWriter writer, int objectNumber, PdfFormFieldBuilder field, int pageObj, List<string> fonts)
    {
        WriteObjectStart(writer, objectNumber);
        writer.WriteLine("<<");
        writer.WriteLine($"   /Type /Annot");
        writer.WriteLine($"   /Subtype /Widget");
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

                int flags = 0;
                if (textField.IsMultiline) flags |= 1 << 12;
                if (textField.IsPassword) flags |= 1 << 13;
                if (textField.IsComb && textField.MaxLength > 0) flags |= 1 << 24;
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
                var tc = textField.TextColor;
                string fontSize = textField.FontSize > 0 ? $"{textField.FontSize:F1}" : "0";
                writer.WriteLine($"   /DA (/F{fontIndex} {fontSize} Tf {tc.R:F3} {tc.G:F3} {tc.B:F3} rg)");
                writer.WriteLine($"   /Q {(int)textField.Alignment}");
                break;

            case PdfCheckboxBuilder checkbox:
                writer.WriteLine("   /FT /Btn");

                int cbFlags = 0;
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
                foreach (var opt in dropdown.Options)
                {
                    if (opt.Value == opt.DisplayText)
                        writer.Write($" {PdfString(opt.Value)}");
                    else
                        writer.Write($" [{PdfString(opt.Value)} {PdfString(opt.DisplayText)}]");
                }
                writer.WriteLine(" ]");

                if (!string.IsNullOrEmpty(dropdown.SelectedValue))
                    writer.WriteLine($"   /V {PdfString(dropdown.SelectedValue)}");

                // Default appearance
                int ddFontIndex = fonts.IndexOf(dropdown.FontName) + 1;
                var ddc = dropdown.TextColor;
                string ddFontSize = dropdown.FontSize > 0 ? $"{dropdown.FontSize:F1}" : "0";
                writer.WriteLine($"   /DA (/F{ddFontIndex} {ddFontSize} Tf {ddc.R:F3} {ddc.G:F3} {ddc.B:F3} rg)");
                break;

            case PdfSignatureFieldBuilder:
                writer.WriteLine("   /FT /Sig");

                int sigFlags = 0;
                if (field.IsReadOnly) sigFlags |= 1;
                if (field.IsRequired) sigFlags |= 1 << 1;
                if (sigFlags != 0)
                    writer.WriteLine($"   /Ff {sigFlags}");
                break;
        }

        // Border and background
        if (field.BorderColor.HasValue || field.BackgroundColor.HasValue)
        {
            writer.WriteLine("   /MK <<");
            if (field.BorderColor.HasValue)
            {
                var bc = field.BorderColor.Value;
                writer.WriteLine($"      /BC [{bc.R:F3} {bc.G:F3} {bc.B:F3}]");
            }
            if (field.BackgroundColor.HasValue)
            {
                var bg = field.BackgroundColor.Value;
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
}
