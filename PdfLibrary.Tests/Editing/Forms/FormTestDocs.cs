using PdfLibrary.Builder;
using PdfLibrary.Builder.FormField;
using PdfLibrary.Builder.Page;
using PdfLibrary.Editing;

namespace PdfLibrary.Tests.Editing.Forms;

/// <summary>
/// Static helpers that return minimal PDF bytes for form-field read tests.
/// Builder API is used where the builder emits correct structure;
/// radio groups are hand-built because the builder's WriteFormField switch
/// has no PdfRadioGroupBuilder case and emits bare widget annotations.
/// </summary>
public static class FormTestDocs
{
    // ── Builder-based fixtures ──────────────────────────────────────────────

    public static byte[] WithTextField(string name, string? value = null)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .WithAcroForm(f => f.SetNeedAppearances(true))
            .AddPage(p =>
            {
                PdfTextFieldBuilder tf = p.AddTextField(name, 72, 700, 300, 20);
                if (value is not null)
                    tf.Value(value);
            });
        return builder.ToByteArray();
    }

    public static byte[] WithCheckbox(string name, bool checkedOn = false)
    {
        return PdfDocumentBuilder.Create()
            .WithAcroForm(f => f.SetNeedAppearances(true))
            .AddPage(p =>
            {
                PdfCheckboxBuilder cb = p.AddCheckbox(name, 72, 700, 14);
                if (checkedOn) cb.Checked(true);
            })
            .ToByteArray();
    }

    public static byte[] WithChoice(
        string name,
        (string Export, string Display)[] opts,
        bool combo,
        string[]? selected = null)
    {
        if (combo)
        {
            return PdfDocumentBuilder.Create()
                .WithAcroForm(f => f.SetNeedAppearances(true))
                .AddPage(p =>
                {
                    // The builder's AddDropdown always writes /Ff with Combo flag.
                    PdfDropdownBuilder dd = p.AddDropdown(name, 72, 700, 200, 20);
                    foreach ((string export, string display) in opts)
                        dd.AddOption(export, display);
                    if (selected is { Length: > 0 })
                        dd.Select(selected[0]);
                })
                .ToByteArray();
        }
        else
        {
            // Hand-build a list box (/FT /Ch with no Combo flag, taller rect for rows)
            return WithListBox(name, opts, selected);
        }
    }

    /// <summary>
    /// Hand-builds a minimal PDF with a list-box choice field (/FT /Ch, no Combo flag).
    /// Uses a tall rect (200x80) to accommodate multiple rows.
    /// </summary>
    private static byte[] WithListBox(
        string name,
        (string Export, string Display)[] opts,
        string[]? selected = null)
    {
        // Object layout:
        //   1: pages node
        //   2: catalog
        //   3: page
        //   4: field/widget (merged — no separate widget)
        //   5: AcroForm
        var offsets = new Dictionary<int, long>();

        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.StreamWriter(ms, System.Text.Encoding.Latin1, leaveOpen: true);

        w.WriteLine("%PDF-1.7");
        w.Flush();

        void WriteObj(int n, string content)
        {
            w.Flush();
            offsets[n] = ms.Position;
            w.WriteLine($"{n} 0 obj");
            w.WriteLine(content);
            w.WriteLine("endobj");
            w.WriteLine();
        }

        WriteObj(1, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObj(2, "<< /Type /Catalog /Pages 1 0 R /AcroForm 5 0 R >>");
        WriteObj(3, "<< /Type /Page /Parent 1 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>");

        // Build /Opt array: [export display] pairs or single string if same
        var optParts = new System.Text.StringBuilder();
        foreach ((string export, string display) in opts)
        {
            if (export == display)
                optParts.Append($" ({EscapePdfStr(export)})");
            else
                optParts.Append($" [({EscapePdfStr(export)}) ({EscapePdfStr(display)})]");
        }

        // /Ff: no Combo flag → list box. Ff=0 is fine.
        string vEntry = selected is { Length: > 0 }
            ? $"({EscapePdfStr(selected[0])})"
            : "()";

        WriteObj(4,
            $"<< /Type /Annot /Subtype /Widget /FT /Ch /T ({EscapePdfStr(name)}) " +
            $"/Ff 0 /V {vEntry} " +
            $"/Opt [{optParts}] " +
            $"/DA (/Helv 12 Tf 0 g) " +
            $"/Rect [72 620 272 700] >>"); // 200x80 tall rect

        WriteObj(5, "<< /Fields [4 0 R] /NeedAppearances true >>");

        w.Flush();
        long xrefOffset = ms.Position;
        int totalObjs = 5;

        w.WriteLine("xref");
        w.WriteLine($"0 {totalObjs + 1}");
        w.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= totalObjs; i++)
        {
            if (offsets.TryGetValue(i, out long off))
                w.WriteLine($"{off:D10} 00000 n ");
            else
                w.WriteLine("0000000000 65535 f ");
        }

        w.WriteLine("trailer");
        w.WriteLine($"<< /Size {totalObjs + 1} /Root 2 0 R >>");
        w.WriteLine("startxref");
        w.WriteLine(xrefOffset.ToString());
        w.WriteLine("%%EOF");
        w.Flush();

        return ms.ToArray();
    }

    /// <summary>
    /// Creates a minimal PDF with a text field that has explicit /Ff flags,
    /// optional /MaxLen, and a custom /Rect size (origin at [50, 700]).
    /// </summary>
    public static byte[] WithTextFieldEx(
        string name,
        string? value,
        int ff,
        int? maxLen,
        double rectW,
        double rectH)
    {
        // Object layout:
        //   1: pages node
        //   2: catalog
        //   3: page
        //   4: field/widget (merged — no separate widget)
        //   5: AcroForm
        var offsets = new Dictionary<int, long>();

        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.StreamWriter(ms, System.Text.Encoding.Latin1, leaveOpen: true);

        w.WriteLine("%PDF-1.7");
        w.Flush();

        void WriteObj(int n, string content)
        {
            w.Flush();
            offsets[n] = ms.Position;
            w.WriteLine($"{n} 0 obj");
            w.WriteLine(content);
            w.WriteLine("endobj");
            w.WriteLine();
        }

        WriteObj(1, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObj(2, "<< /Type /Catalog /Pages 1 0 R /AcroForm 5 0 R >>");
        WriteObj(3,
            $"<< /Type /Page /Parent 1 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>");

        double x0 = 50, y0 = 700, x1 = x0 + rectW, y1 = y0 + rectH;
        string vEntry = value is not null ? $"({EscapePdfStr(value)})" : "()";
        string maxLenEntry = maxLen.HasValue ? $" /MaxLen {maxLen.Value}" : "";

        WriteObj(4,
            $"<< /Type /Annot /Subtype /Widget /FT /Tx /T ({EscapePdfStr(name)}) " +
            $"/Ff {ff} /V {vEntry}{maxLenEntry} " +
            $"/DA (/Helv 12 Tf 0 g) " +
            $"/Rect [{x0} {y0} {x1:F2} {y1:F2}] >>");

        WriteObj(5, "<< /Fields [4 0 R] /NeedAppearances true >>");

        w.Flush();
        long xrefOffset = ms.Position;
        int totalObjs = 5;

        w.WriteLine("xref");
        w.WriteLine($"0 {totalObjs + 1}");
        w.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= totalObjs; i++)
        {
            if (offsets.TryGetValue(i, out long off))
                w.WriteLine($"{off:D10} 00000 n ");
            else
                w.WriteLine("0000000000 65535 f ");
        }

        w.WriteLine("trailer");
        w.WriteLine($"<< /Size {totalObjs + 1} /Root 2 0 R >>");
        w.WriteLine("startxref");
        w.WriteLine(xrefOffset.ToString());
        w.WriteLine("%%EOF");
        w.Flush();

        return ms.ToArray();
    }

    // ── Hand-built fixture ─────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal PDF with a radio-button group.
    /// Structure (PDF §12.7.4.2):
    ///   AcroForm → Fields → [parent field dict]
    ///   Parent field dict: /FT /Btn, /Ff 49152 (Radio+NoToggleToOff bits), /T, /V, /Kids → [widget refs]
    ///   Each widget dict: /Subtype /Widget, /Parent, /AP /N → dict with on-state-name and /Off keys
    /// </summary>
    public static byte[] WithRadioGroup(string name, string[] options, string? selected = null)
    {
        // We build the raw PDF string manually.
        // Object layout:
        //   1: pages node
        //   2: catalog
        //   3: page
        //   4: parent radio field
        //   5..N: widget annotations (one per option)
        //   N+1: AcroForm

        int widgetBase = 5;
        int widgetCount = options.Length;
        int acroFormObj = widgetBase + widgetCount;

        var offsets = new Dictionary<int, long>();

        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.StreamWriter(ms, System.Text.Encoding.Latin1, leaveOpen: true);

        // Write the PDF header first
        w.WriteLine("%PDF-1.7");
        w.Flush();

        void WriteObj(int n, string content)
        {
            w.Flush();
            offsets[n] = ms.Position;
            w.WriteLine($"{n} 0 obj");
            w.WriteLine(content);
            w.WriteLine("endobj");
            w.WriteLine();
        }

        // 1: Pages
        WriteObj(1,
            $"<< /Type /Pages /Kids [3 0 R] /Count 1 >>");

        // 2: Catalog
        WriteObj(2,
            $"<< /Type /Catalog /Pages 1 0 R /AcroForm {acroFormObj} 0 R >>");

        // 3: Page (minimal)
        string widgetRefs = string.Join(" ", Enumerable.Range(widgetBase, widgetCount).Select(i => $"{i} 0 R"));
        WriteObj(3,
            $"<< /Type /Page /Parent 1 0 R /MediaBox [0 0 612 792] /Annots [{widgetRefs}] >>");

        // 4: Parent radio field dict
        // Ff: Radio (bit 16, 1-based = 1<<15 = 32768) + NoToggleToOff (bit 15 = 1<<14 = 16384) = 49152
        int radioFf = (1 << 15) | (1 << 14); // Radio=16 (1-based), NoToggleToOff=15 (1-based)
        string selectedValue = selected is not null ? $"/{EscapeName(selected)}" : "/Off";
        WriteObj(4,
            $"<< /FT /Btn /Ff {radioFf} /T ({EscapePdfStr(name)}) /V {selectedValue} /Kids [{widgetRefs}] >>");

        // 5..5+N-1: Widget dicts
        for (int i = 0; i < widgetCount; i++)
        {
            int objN = widgetBase + i;
            string optName = options[i];
            bool isSelected = selected is not null && selected == optName;

            // Each widget has /AP /N << /optName <stream> /Off <stream> >>
            // We use inline empty sub-dicts (no streams) — the reader only needs the dict keys
            string asValue = isSelected ? $"/{EscapeName(optName)}" : "/Off";
            double y = 700 - (i * 20);
            WriteObj(objN,
                $"<< /Type /Annot /Subtype /Widget /Parent 4 0 R " +
                $"/Rect [72 {y:F2} {72 + 14:F2} {y + 14:F2}] " +
                $"/AS {asValue} " +
                $"/AP << /N << /{EscapeName(optName)} << >> /Off << >> >> >> >>");
        }

        // AcroForm
        WriteObj(acroFormObj,
            $"<< /Fields [4 0 R] /NeedAppearances true >>");

        w.Flush();
        long xrefOffset = ms.Position;

        int totalObjs = acroFormObj;

        w.WriteLine("xref");
        w.WriteLine($"0 {totalObjs + 1}");
        w.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= totalObjs; i++)
        {
            if (offsets.TryGetValue(i, out long off))
                w.WriteLine($"{off:D10} 00000 n ");
            else
                w.WriteLine("0000000000 65535 f ");
        }

        w.WriteLine("trailer");
        w.WriteLine($"<< /Size {totalObjs + 1} /Root 2 0 R >>");
        w.WriteLine("startxref");
        w.WriteLine(xrefOffset.ToString());
        w.WriteLine("%%EOF");
        w.Flush();

        return ms.ToArray();
    }

    /// <summary>
    /// Builds a minimal PDF with a checkbox field that already has /AP /N entries
    /// (on-state "Yes" and "Off"), stored as indirect objects so their object numbers
    /// can be tracked by tests.
    /// </summary>
    public static byte[] WithCheckboxWithAp(string name)
    {
        // Object layout:
        //   1: pages node
        //   2: catalog
        //   3: page
        //   4: field/widget (merged)
        //   5: on-state appearance stream (empty dict as placeholder)
        //   6: off-state appearance stream (empty dict as placeholder)
        //   7: AcroForm
        var offsets = new Dictionary<int, long>();

        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.StreamWriter(ms, System.Text.Encoding.Latin1, leaveOpen: true);

        w.WriteLine("%PDF-1.7");
        w.Flush();

        void WriteObj(int n, string content)
        {
            w.Flush();
            offsets[n] = ms.Position;
            w.WriteLine($"{n} 0 obj");
            w.WriteLine(content);
            w.WriteLine("endobj");
            w.WriteLine();
        }

        WriteObj(1, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObj(2, "<< /Type /Catalog /Pages 1 0 R /AcroForm 7 0 R >>");
        WriteObj(3, "<< /Type /Page /Parent 1 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>");

        // Objects 5 and 6: indirect appearance dicts (empty) for /Yes and /Off
        WriteObj(5, "<< >>");
        WriteObj(6, "<< >>");

        // Widget with /AP /N referencing indirect objects
        WriteObj(4,
            $"<< /Type /Annot /Subtype /Widget /FT /Btn /T ({EscapePdfStr(name)}) " +
            $"/Ff 0 /V /Off /AS /Off " +
            $"/AP << /N << /Yes 5 0 R /Off 6 0 R >> >> " +
            $"/Rect [72 700 86 714] >>");

        WriteObj(7, "<< /Fields [4 0 R] /NeedAppearances true >>");

        w.Flush();
        long xrefOffset = ms.Position;
        int totalObjs = 7;

        w.WriteLine("xref");
        w.WriteLine($"0 {totalObjs + 1}");
        w.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= totalObjs; i++)
        {
            if (offsets.TryGetValue(i, out long off))
                w.WriteLine($"{off:D10} 00000 n ");
            else
                w.WriteLine("0000000000 65535 f ");
        }

        w.WriteLine("trailer");
        w.WriteLine($"<< /Size {totalObjs + 1} /Root 2 0 R >>");
        w.WriteLine("startxref");
        w.WriteLine(xrefOffset.ToString());
        w.WriteLine("%%EOF");
        w.Flush();

        return ms.ToArray();
    }

    /// <summary>
    /// Builds a minimal PDF with a checkbox field that has NO /AP appearance dictionary.
    /// The widget has /FT /Btn, /T, /V /Off, /AS /Off, and a /Rect, but no /AP entry.
    /// This exercises the path where ButtonStateWriter must synthesise an appearance.
    /// </summary>
    public static byte[] WithCheckboxNoAp(string name)
    {
        // Object layout:
        //   1: pages node
        //   2: catalog
        //   3: page
        //   4: field/widget (merged — no separate widget, no /AP)
        //   5: AcroForm
        var offsets = new Dictionary<int, long>();

        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.StreamWriter(ms, System.Text.Encoding.Latin1, leaveOpen: true);

        w.WriteLine("%PDF-1.7");
        w.Flush();

        void WriteObj(int n, string content)
        {
            w.Flush();
            offsets[n] = ms.Position;
            w.WriteLine($"{n} 0 obj");
            w.WriteLine(content);
            w.WriteLine("endobj");
            w.WriteLine();
        }

        WriteObj(1, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObj(2, "<< /Type /Catalog /Pages 1 0 R /AcroForm 5 0 R >>");
        WriteObj(3, "<< /Type /Page /Parent 1 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>");

        // Checkbox widget with NO /AP — just FT, T, V, AS, Rect
        // Ff=0 is a plain checkbox (no Radio bit)
        WriteObj(4,
            $"<< /Type /Annot /Subtype /Widget /FT /Btn /T ({EscapePdfStr(name)}) " +
            $"/Ff 0 /V /Off /AS /Off " +
            $"/Rect [72 700 86 714] >>");

        WriteObj(5, "<< /Fields [4 0 R] /NeedAppearances true >>");

        w.Flush();
        long xrefOffset = ms.Position;
        int totalObjs = 5;

        w.WriteLine("xref");
        w.WriteLine($"0 {totalObjs + 1}");
        w.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= totalObjs; i++)
        {
            if (offsets.TryGetValue(i, out long off))
                w.WriteLine($"{off:D10} 00000 n ");
            else
                w.WriteLine("0000000000 65535 f ");
        }

        w.WriteLine("trailer");
        w.WriteLine($"<< /Size {totalObjs + 1} /Root 2 0 R >>");
        w.WriteLine("startxref");
        w.WriteLine(xrefOffset.ToString());
        w.WriteLine("%%EOF");
        w.Flush();

        return ms.ToArray();
    }

    /// <summary>
    /// Builds a minimal single-page PDF containing a text field named "text1" at
    /// <paramref name="textRect"/> and a checkbox named "check1" at <paramref name="checkRect"/>
    /// with the given on-state name, then opens it in an editor.
    /// </summary>
    public static PdfDocumentEditor SingleTextAndCheckbox(
        PdfRect textRect,
        PdfRect checkRect,
        string checkOnState = "Yes")
    {
        byte[] pdf = BuildSingleTextAndCheckboxPdf(textRect, checkRect, checkOnState);
        return PdfDocumentEditor.Open(new System.IO.MemoryStream(pdf));
    }

    private static byte[] BuildSingleTextAndCheckboxPdf(
        PdfRect textRect,
        PdfRect checkRect,
        string checkOnState)
    {
        // Object layout:
        //   1: pages node
        //   2: catalog
        //   3: page  (Annots: 4, 5)
        //   4: text field widget  /FT /Tx /T (text1)
        //   5: checkbox widget    /FT /Btn /T (check1) with /AP /N
        //   6: AcroForm
        var offsets = new Dictionary<int, long>();

        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.StreamWriter(ms, System.Text.Encoding.Latin1, leaveOpen: true);

        w.WriteLine("%PDF-1.7");
        w.Flush();

        void WriteObj(int n, string content)
        {
            w.Flush();
            offsets[n] = ms.Position;
            w.WriteLine($"{n} 0 obj");
            w.WriteLine(content);
            w.WriteLine("endobj");
            w.WriteLine();
        }

        WriteObj(1, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObj(2, "<< /Type /Catalog /Pages 1 0 R /AcroForm 6 0 R >>");
        WriteObj(3, "<< /Type /Page /Parent 1 0 R /MediaBox [0 0 612 792] /Annots [4 0 R 5 0 R] >>");

        // Text field widget (merged field+widget, no separate parent)
        WriteObj(4,
            $"<< /Type /Annot /Subtype /Widget /FT /Tx /T (text1) " +
            $"/V () /DA (/Helv 12 Tf 0 g) " +
            $"/Rect [{textRect.Left:G} {textRect.Bottom:G} {textRect.Right:G} {textRect.Top:G}] >>");

        // Checkbox widget with /AP /N containing the on-state and /Off keys
        WriteObj(5,
            $"<< /Type /Annot /Subtype /Widget /FT /Btn /T (check1) " +
            $"/Ff 0 /V /Off /AS /Off " +
            $"/AP << /N << /{EscapeName(checkOnState)} << >> /Off << >> >> >> " +
            $"/Rect [{checkRect.Left:G} {checkRect.Bottom:G} {checkRect.Right:G} {checkRect.Top:G}] >>");

        WriteObj(6, "<< /Fields [4 0 R 5 0 R] /NeedAppearances true >>");

        w.Flush();
        long xrefOffset = ms.Position;
        int totalObjs = 6;

        w.WriteLine("xref");
        w.WriteLine($"0 {totalObjs + 1}");
        w.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= totalObjs; i++)
        {
            if (offsets.TryGetValue(i, out long off))
                w.WriteLine($"{off:D10} 00000 n ");
            else
                w.WriteLine("0000000000 65535 f ");
        }

        w.WriteLine("trailer");
        w.WriteLine($"<< /Size {totalObjs + 1} /Root 2 0 R >>");
        w.WriteLine("startxref");
        w.WriteLine(xrefOffset.ToString());
        w.WriteLine("%%EOF");
        w.Flush();

        return ms.ToArray();
    }

    private static string EscapePdfStr(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static string EscapeName(string s) => s; // assume simple ASCII option names
}
