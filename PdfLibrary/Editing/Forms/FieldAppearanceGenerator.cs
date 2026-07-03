using System.Globalization;
using System.Linq;
using System.Text;
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing.Forms;

/// <summary>
/// Regenerates the /AP /N appearance stream for AcroForm fields.
/// Currently handles single-line, non-comb <see cref="PdfTextField"/> only.
/// </summary>
internal static class FieldAppearanceGenerator
{
    /// <summary>
    /// Regenerates the normal (/N) appearance stream for every widget of <paramref name="field"/>.
    /// Sets /AcroForm /NeedAppearances to false on success.
    /// </summary>
    public static void Regenerate(PdfDocument doc, PdfFormField field)
    {
        // A filled widget must be printable. Widgets authored without /F (the common case) default to
        // non-printing, so viewers display the value on screen but OMIT it from print/export.
        foreach (PdfDictionary widget in field.WidgetDicts)
            EnsurePrintable(widget);

        if (field is PdfTextField t)
        {
            if (t is { IsComb: true, MaxLength: int and > 0 })
                RegenerateCombTextField(doc, t);
            else if (t.IsMultiline)
                RegenerateMultilineTextField(doc, t);
            else
                RegenerateTextField(doc, t);
            return;
        }

        if (field is PdfChoiceField c)
        {
            if (c.IsCombo)
                RegenerateComboField(doc, c);
            else
                RegenerateListField(doc, c);
        }
    }

    /// <summary>
    /// Sets the /F Print flag (bit 3, value 4) on a widget annotation so its appearance prints, not
    /// just displays. No-op if already set. Form widgets default to non-printing when /F is absent.
    /// </summary>
    public static void EnsurePrintable(PdfDictionary widget)
    {
        const int print = 4;
        int flags = widget.Get(new PdfName("F")) is PdfInteger fi ? fi.Value : 0;
        if ((flags & print) == 0)
            widget[new PdfName("F")] = new PdfInteger(flags | print);
    }

    // ─── Single-line text ──────────────────────────────────────────────────────

    private static void RegenerateTextField(PdfDocument doc, PdfTextField field)
    {
        string value = field.Value ?? string.Empty;

        // Effective DA: own field → /AcroForm /DA → default
        string? effectiveDa = GetEffectiveDa(doc, field);
        FieldDa da = FieldDaParser.Parse(effectiveDa);

        (string resName, PdfIndirectReference fontRef) =
            AppearanceFontResolver.Resolve(doc, da.FontName);

        bool anyWidgetWritten = false;

        foreach (PdfDictionary widget in field.WidgetDicts)
        {
            // Read /Rect
            if (!TryGetRect(doc, widget, out double x0, out double y0, out double x1, out double y1))
                continue;

            double w = Math.Abs(x1 - x0);
            double h = Math.Abs(y1 - y0);

            if (w <= 0 || h <= 0)
                continue;

            double pad = 2.0;

            // Font size
            double size = da.FontSize > 0
                ? da.FontSize
                : AutoSize(value, w, h, pad);

            // Horizontal position based on quadding
            // Re-read from the dict so tests/callers that mutate dict directly take effect
            double textW = PdfFontMetrics.MeasureText(value, "Helvetica", size);
            int q = GetQuadding(doc, field);
            double tx = q == 1
                ? (w - textW) / 2.0
                : q == 2
                    ? w - pad - textW
                    : pad;

            // For left-aligned (q==0), clamp to pad so text doesn't overflow left
            if (q == 0)
                tx = Math.Max(pad, tx);

            // Vertical: baseline position — (h-size)/2 + size*0.2 as the descent compensation
            double ty = (h - size) / 2.0 + size * 0.2;

            // Build content stream — all numbers invariant-culture
            string sizeStr = FormatNumber(size);
            string txStr = FormatNumber(tx);
            string tyStr = FormatNumber(ty);
            string wStr = FormatNumber(w);
            string hStr = FormatNumber(h);

            // Produce the show-string token using PdfString.FromText for correct escaping
            string showToken = PdfString.FromText(value).ToPdfString();

            string content =
                "/Tx BMC\n" +
                "q\n" +
                "BT\n" +
                da.ColorOps + "\n" +
                "/" + resName + " " + sizeStr + " Tf\n" +
                "1 0 0 1 " + txStr + " " + tyStr + " Tm\n" +
                showToken + " Tj\n" +
                "ET\n" +
                "Q\n" +
                "EMC";

            byte[] contentBytes = Encoding.ASCII.GetBytes(content);

            // Form XObject stream dict
            var xobjDict = new PdfDictionary
            {
                [new PdfName("Type")] = new PdfName("XObject"),
                [new PdfName("Subtype")] = new PdfName("Form"),
                [new PdfName("BBox")] = new PdfArray
                {
                    new PdfReal(0), new PdfReal(0), new PdfReal(w), new PdfReal(h)
                },
                [new PdfName("Matrix")] = new PdfArray
                {
                    new PdfInteger(1), new PdfInteger(0),
                    new PdfInteger(0), new PdfInteger(1),
                    new PdfInteger(0), new PdfInteger(0)
                }
            };

            // /Resources /Font /resName <fontRef>
            var fontResources = new PdfDictionary
            {
                [new PdfName(resName)] = fontRef
            };
            var resources = new PdfDictionary
            {
                [new PdfName("Font")] = fontResources
            };
            xobjDict[new PdfName("Resources")] = resources;

            var xobjStream = new PdfStream(xobjDict, contentBytes);
            PdfIndirectReference apRef = doc.RegisterObject(xobjStream);

            // Set /AP /N on the widget
            var apDict = new PdfDictionary
            {
                [new PdfName("N")] = apRef
            };
            widget[new PdfName("AP")] = apDict;

            anyWidgetWritten = true;
        }

        if (anyWidgetWritten)
        {
            // Clear /NeedAppearances on the /AcroForm dict
            SetNeedAppearancesFalse(doc);
        }
    }

    // ─── Multiline text ────────────────────────────────────────────────────────

    private static void RegenerateMultilineTextField(PdfDocument doc, PdfTextField field)
    {
        string value = field.Value ?? string.Empty;

        string? effectiveDa = GetEffectiveDa(doc, field);
        FieldDa da = FieldDaParser.Parse(effectiveDa);

        (string resName, PdfIndirectReference fontRef) =
            AppearanceFontResolver.Resolve(doc, da.FontName);

        bool anyWidgetWritten = false;

        foreach (PdfDictionary widget in field.WidgetDicts)
        {
            if (!TryGetRect(doc, widget, out double x0, out double y0, out double x1, out double y1))
                continue;

            double w = Math.Abs(x1 - x0);
            double h = Math.Abs(y1 - y0);

            if (w <= 0 || h <= 0)
                continue;

            double pad = 2.0;
            double size = da.FontSize > 0 ? da.FontSize : 12.0;
            double leading = 1.15 * size;

            // Split on explicit hard line breaks first (\r\n, \r, \n), then word-wrap each hard line
            string[] hardLines = value.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var lines = new List<string>();

            foreach (string hardLine in hardLines)
            {
                if (hardLine.Length == 0)
                {
                    // Empty hard line — emit a blank line slot
                    lines.Add(string.Empty);
                    continue;
                }

                // Greedy word-wrap within this hard line
                string[] words = hardLine.Split(' ');
                string current = string.Empty;

                foreach (string word in words)
                {
                    if (current.Length == 0)
                    {
                        current = word;
                    }
                    else
                    {
                        string candidate = current + " " + word;
                        if (PdfFontMetrics.MeasureText(candidate, "Helvetica", size) <= w - 2 * pad)
                            current = candidate;
                        else
                        {
                            lines.Add(current);
                            current = word;
                        }
                    }
                }
                lines.Add(current);
            }

            // Clip to what fits vertically
            double firstBaselineY = h - pad - size;
            var visibleLines = new List<string>();
            double y = firstBaselineY;
            foreach (string line in lines)
            {
                if (y < pad) break;
                visibleLines.Add(line);
                y -= leading;
            }

            if (visibleLines.Count == 0)
                continue;

            // Build content stream
            string sizeStr = FormatNumber(size);
            string tyStr = FormatNumber(firstBaselineY);
            string txStr = FormatNumber(pad);
            string leadingStr = FormatNumber(-leading);
            string wStr = FormatNumber(w);
            string hStr = FormatNumber(h);

            var sb = new StringBuilder();
            sb.AppendLine("/Tx BMC");
            sb.AppendLine("q");
            sb.AppendLine("BT");
            sb.AppendLine(da.ColorOps);
            sb.AppendLine("/" + resName + " " + sizeStr + " Tf");
            sb.AppendLine("1 0 0 1 " + txStr + " " + tyStr + " Tm");
            sb.Append(PdfString.FromText(visibleLines[0]).ToPdfString() + " Tj");
            for (int i = 1; i < visibleLines.Count; i++)
            {
                sb.AppendLine();
                sb.Append("0 " + leadingStr + " Td");
                sb.AppendLine();
                sb.Append(PdfString.FromText(visibleLines[i]).ToPdfString() + " Tj");
            }
            sb.AppendLine();
            sb.AppendLine("ET");
            sb.AppendLine("Q");
            sb.Append("EMC");

            byte[] contentBytes = Encoding.ASCII.GetBytes(sb.ToString());

            var xobjDict = new PdfDictionary
            {
                [new PdfName("Type")] = new PdfName("XObject"),
                [new PdfName("Subtype")] = new PdfName("Form"),
                [new PdfName("BBox")] = new PdfArray
                {
                    new PdfReal(0), new PdfReal(0), new PdfReal(w), new PdfReal(h)
                },
                [new PdfName("Matrix")] = new PdfArray
                {
                    new PdfInteger(1), new PdfInteger(0),
                    new PdfInteger(0), new PdfInteger(1),
                    new PdfInteger(0), new PdfInteger(0)
                }
            };

            var fontResources = new PdfDictionary
            {
                [new PdfName(resName)] = fontRef
            };
            var resources = new PdfDictionary
            {
                [new PdfName("Font")] = fontResources
            };
            xobjDict[new PdfName("Resources")] = resources;

            var xobjStream = new PdfStream(xobjDict, contentBytes);
            PdfIndirectReference apRef = doc.RegisterObject(xobjStream);

            var apDict = new PdfDictionary
            {
                [new PdfName("N")] = apRef
            };
            widget[new PdfName("AP")] = apDict;

            anyWidgetWritten = true;
        }

        if (anyWidgetWritten)
            SetNeedAppearancesFalse(doc);
    }

    // ─── Comb text ─────────────────────────────────────────────────────────────

    private static void RegenerateCombTextField(PdfDocument doc, PdfTextField field)
    {
        string value = field.Value ?? string.Empty;
        int maxLen = field.MaxLength!.Value;

        string? effectiveDa = GetEffectiveDa(doc, field);
        FieldDa da = FieldDaParser.Parse(effectiveDa);

        (string resName, PdfIndirectReference fontRef) =
            AppearanceFontResolver.Resolve(doc, da.FontName);

        bool anyWidgetWritten = false;

        foreach (PdfDictionary widget in field.WidgetDicts)
        {
            if (!TryGetRect(doc, widget, out double x0, out double y0, out double x1, out double y1))
                continue;

            double w = Math.Abs(x1 - x0);
            double h = Math.Abs(y1 - y0);

            if (w <= 0 || h <= 0)
                continue;

            double pad = 2.0;
            double size = da.FontSize > 0
                ? da.FontSize
                : Math.Max(4.0, Math.Min(12.0, Math.Floor(h - 2.0 * pad)));
            double cellW = (w - 2.0 * pad) / maxLen;
            double baselineY = (h - size) / 2.0 + size * 0.2;

            string sizeStr = FormatNumber(size);
            string byStr = FormatNumber(baselineY);
            string wStr = FormatNumber(w);
            string hStr = FormatNumber(h);

            var sb = new StringBuilder();
            sb.AppendLine("/Tx BMC");
            sb.AppendLine("q");
            sb.AppendLine("BT");
            sb.AppendLine(da.ColorOps);
            sb.AppendLine("/" + resName + " " + sizeStr + " Tf");

            int charsToDraw = Math.Min(value.Length, maxLen);
            for (int i = 0; i < charsToDraw; i++)
            {
                string ch = value[i].ToString();
                double charW = PdfFontMetrics.MeasureText(ch, "Helvetica", size);
                double cx = pad + cellW * i + (cellW - charW) / 2.0;

                string cxStr = FormatNumber(cx);
                string showToken = PdfString.FromText(ch).ToPdfString();

                sb.AppendLine("1 0 0 1 " + cxStr + " " + byStr + " Tm");
                sb.Append(showToken + " Tj");
                if (i < charsToDraw - 1)
                    sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("ET");
            sb.AppendLine("Q");
            sb.Append("EMC");

            byte[] contentBytes = Encoding.ASCII.GetBytes(sb.ToString());

            var xobjDict = new PdfDictionary
            {
                [new PdfName("Type")] = new PdfName("XObject"),
                [new PdfName("Subtype")] = new PdfName("Form"),
                [new PdfName("BBox")] = new PdfArray
                {
                    new PdfReal(0), new PdfReal(0), new PdfReal(w), new PdfReal(h)
                },
                [new PdfName("Matrix")] = new PdfArray
                {
                    new PdfInteger(1), new PdfInteger(0),
                    new PdfInteger(0), new PdfInteger(1),
                    new PdfInteger(0), new PdfInteger(0)
                }
            };

            var fontResources = new PdfDictionary
            {
                [new PdfName(resName)] = fontRef
            };
            var resources = new PdfDictionary
            {
                [new PdfName("Font")] = fontResources
            };
            xobjDict[new PdfName("Resources")] = resources;

            var xobjStream = new PdfStream(xobjDict, contentBytes);
            PdfIndirectReference apRef = doc.RegisterObject(xobjStream);

            var apDict = new PdfDictionary
            {
                [new PdfName("N")] = apRef
            };
            widget[new PdfName("AP")] = apDict;

            anyWidgetWritten = true;
        }

        if (anyWidgetWritten)
            SetNeedAppearancesFalse(doc);
    }

    // ─── Combo choice ─────────────────────────────────────────────────────────

    private static void RegenerateComboField(PdfDocument doc, PdfChoiceField field)
    {
        // Display text: look up the display for the first selected export value
        string displayText = string.Empty;
        if (field.SelectedValues.Count > 0)
        {
            string export = field.SelectedValues[0];
            (string Export, string Display) found = field.Options.FirstOrDefault(o => o.Export == export);
            displayText = found != default ? found.Display : export;
        }

        string? effectiveDa = GetEffectiveDa(doc, field);
        FieldDa da = FieldDaParser.Parse(effectiveDa);

        (string resName, PdfIndirectReference fontRef) =
            AppearanceFontResolver.Resolve(doc, da.FontName);

        bool anyWidgetWritten = false;

        foreach (PdfDictionary widget in field.WidgetDicts)
        {
            if (!TryGetRect(doc, widget, out double x0, out double y0, out double x1, out double y1))
                continue;

            double w = Math.Abs(x1 - x0);
            double h = Math.Abs(y1 - y0);

            if (w <= 0 || h <= 0)
                continue;

            double pad = 2.0;
            double size = da.FontSize > 0
                ? da.FontSize
                : AutoSize(displayText, w, h, pad);

            double tx = pad;
            double ty = (h - size) / 2.0 + size * 0.2;

            string sizeStr = FormatNumber(size);
            string txStr   = FormatNumber(tx);
            string tyStr   = FormatNumber(ty);
            string wStr    = FormatNumber(w);
            string hStr    = FormatNumber(h);

            string showToken = PdfString.FromText(displayText).ToPdfString();

            string content =
                "/Tx BMC\n" +
                "q\n" +
                "BT\n" +
                da.ColorOps + "\n" +
                "/" + resName + " " + sizeStr + " Tf\n" +
                "1 0 0 1 " + txStr + " " + tyStr + " Tm\n" +
                showToken + " Tj\n" +
                "ET\n" +
                "Q\n" +
                "EMC";

            byte[] contentBytes = Encoding.ASCII.GetBytes(content);

            var xobjDict = new PdfDictionary
            {
                [new PdfName("Type")]    = new PdfName("XObject"),
                [new PdfName("Subtype")] = new PdfName("Form"),
                [new PdfName("BBox")]    = new PdfArray
                {
                    new PdfReal(0), new PdfReal(0), new PdfReal(w), new PdfReal(h)
                },
                [new PdfName("Matrix")] = new PdfArray
                {
                    new PdfInteger(1), new PdfInteger(0),
                    new PdfInteger(0), new PdfInteger(1),
                    new PdfInteger(0), new PdfInteger(0)
                }
            };

            var fontResources = new PdfDictionary
            {
                [new PdfName(resName)] = fontRef
            };
            var resources = new PdfDictionary
            {
                [new PdfName("Font")] = fontResources
            };
            xobjDict[new PdfName("Resources")] = resources;

            var xobjStream = new PdfStream(xobjDict, contentBytes);
            PdfIndirectReference apRef = doc.RegisterObject(xobjStream);

            var apDict = new PdfDictionary
            {
                [new PdfName("N")] = apRef
            };
            widget[new PdfName("AP")] = apDict;

            anyWidgetWritten = true;
        }

        if (anyWidgetWritten)
            SetNeedAppearancesFalse(doc);
    }

    // ─── List choice ───────────────────────────────────────────────────────────

    private static void RegenerateListField(PdfDocument doc, PdfChoiceField field)
    {
        string? effectiveDa = GetEffectiveDa(doc, field);
        FieldDa da = FieldDaParser.Parse(effectiveDa);

        (string resName, PdfIndirectReference fontRef) =
            AppearanceFontResolver.Resolve(doc, da.FontName);

        bool anyWidgetWritten = false;

        foreach (PdfDictionary widget in field.WidgetDicts)
        {
            if (!TryGetRect(doc, widget, out double x0, out double y0, out double x1, out double y1))
                continue;

            double w = Math.Abs(x1 - x0);
            double h = Math.Abs(y1 - y0);

            if (w <= 0 || h <= 0)
                continue;

            double pad     = 2.0;
            double size    = da.FontSize > 0 ? da.FontSize : 12.0;
            double leading = 1.15 * size;

            // Collect rows that fit, top-down
            // First row baseline: h - pad - size
            var rows = new List<(string Display, bool Selected)>();
            double y = h - pad - size;
            foreach ((string export, string display) in field.Options)
            {
                if (y < pad) break;
                bool selected = field.SelectedValues.Contains(export);
                rows.Add((display, selected));
                y -= leading;
            }

            if (rows.Count == 0)
                continue;

            string sizeStr    = FormatNumber(size);
            string leadingStr = FormatNumber(-leading);
            string padStr     = FormatNumber(pad);
            string wStr       = FormatNumber(w);
            string hStr       = FormatNumber(h);

            // Build content stream:
            // /Tx BMC  q
            // For each row: optional highlight rect OUTSIDE BT..ET, then BT..ET text
            // Q  EMC
            var sb = new StringBuilder();
            sb.AppendLine("/Tx BMC");
            sb.AppendLine("q");

            double rowY = h - pad - size;
            for (int i = 0; i < rows.Count; i++)
            {
                (string display, bool isSelected) = rows[i];
                double rowBottomY = rowY - (leading - size); // bottom of the row box

                if (isSelected)
                {
                    // Highlight rect: full width (minus padding), height = leading
                    string rxStr = padStr;
                    string ryStr = FormatNumber(rowBottomY);
                    string rwStr = FormatNumber(w - 2 * pad);
                    string rhStr = FormatNumber(leading);

                    sb.AppendLine("0.6 0.6 0.6 rg");
                    sb.AppendLine(rxStr + " " + ryStr + " " + rwStr + " " + rhStr + " re");
                    sb.AppendLine("f");
                    // Restore to black for text
                    sb.AppendLine(da.ColorOps);
                }

                // Text row
                string tyStr = FormatNumber(rowY);
                string showToken = PdfString.FromText(display).ToPdfString();

                if (i == 0)
                {
                    sb.AppendLine("BT");
                    sb.AppendLine(da.ColorOps);
                    sb.AppendLine("/" + resName + " " + sizeStr + " Tf");
                    sb.AppendLine("1 0 0 1 " + padStr + " " + tyStr + " Tm");
                    sb.AppendLine(showToken + " Tj");
                    sb.AppendLine("ET");
                }
                else
                {
                    sb.AppendLine("BT");
                    sb.AppendLine("/" + resName + " " + sizeStr + " Tf");
                    sb.AppendLine("1 0 0 1 " + padStr + " " + tyStr + " Tm");
                    sb.AppendLine(showToken + " Tj");
                    sb.AppendLine("ET");
                }

                rowY -= leading;
            }

            sb.AppendLine("Q");
            sb.Append("EMC");

            byte[] contentBytes = Encoding.ASCII.GetBytes(sb.ToString());

            var xobjDict = new PdfDictionary
            {
                [new PdfName("Type")]    = new PdfName("XObject"),
                [new PdfName("Subtype")] = new PdfName("Form"),
                [new PdfName("BBox")]    = new PdfArray
                {
                    new PdfReal(0), new PdfReal(0), new PdfReal(w), new PdfReal(h)
                },
                [new PdfName("Matrix")] = new PdfArray
                {
                    new PdfInteger(1), new PdfInteger(0),
                    new PdfInteger(0), new PdfInteger(1),
                    new PdfInteger(0), new PdfInteger(0)
                }
            };

            var fontResources = new PdfDictionary
            {
                [new PdfName(resName)] = fontRef
            };
            var resources = new PdfDictionary
            {
                [new PdfName("Font")] = fontResources
            };
            xobjDict[new PdfName("Resources")] = resources;

            var xobjStream = new PdfStream(xobjDict, contentBytes);
            PdfIndirectReference apRef = doc.RegisterObject(xobjStream);

            var apDict = new PdfDictionary
            {
                [new PdfName("N")] = apRef
            };
            widget[new PdfName("AP")] = apDict;

            anyWidgetWritten = true;
        }

        if (anyWidgetWritten)
            SetNeedAppearancesFalse(doc);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Auto-size: start at min(12, floor(h-2*pad)), shrink until text fits, floor at 4.</summary>
    private static double AutoSize(string value, double w, double h, double pad)
    {
        double s = Math.Min(12.0, Math.Floor(h - 2.0 * pad));
        s = Math.Max(4.0, s);

        while (s > 4.0 && PdfFontMetrics.MeasureText(value, "Helvetica", s) > w - 2.0 * pad)
            s -= 0.5;

        return Math.Max(4.0, s);
    }

    private static bool TryGetRect(
        PdfDocument doc,
        PdfDictionary widget,
        out double x0, out double y0, out double x1, out double y1)
    {
        x0 = y0 = x1 = y1 = 0;

        PdfObject? rectRaw = widget.Get(new PdfName("Rect"));
        if (Resolve(doc, rectRaw) is not PdfArray rect || rect.Count < 4)
            return false;

        x0 = ToDouble(rect[0]);
        y0 = ToDouble(rect[1]);
        x1 = ToDouble(rect[2]);
        y1 = ToDouble(rect[3]);

        return true;
    }

    private static double ToDouble(PdfObject obj) => obj switch
    {
        PdfReal r => r.Value,
        PdfInteger i => i.Value,
        _ => 0.0
    };

    private static string? GetEffectiveDa(PdfDocument doc, PdfFormField field)
    {
        // Own field dict
        PdfObject? daOwn = field.Dict.Get(new PdfName("DA"));
        if (daOwn is PdfString s1 && !string.IsNullOrWhiteSpace(s1.Value))
            return s1.Value;

        // /AcroForm /DA
        PdfDictionary? catalog = doc.CatalogDictionary;
        if (catalog is null) return null;

        PdfObject? acroRaw = catalog.Get(new PdfName("AcroForm"));
        if (Resolve(doc, acroRaw) is not PdfDictionary acro) return null;

        PdfObject? daAcro = acro.Get(new PdfName("DA"));
        if (daAcro is PdfString s2 && !string.IsNullOrWhiteSpace(s2.Value))
            return s2.Value;

        return null;
    }

    private static void SetNeedAppearancesFalse(PdfDocument doc)
    {
        PdfDictionary? catalog = doc.CatalogDictionary;
        if (catalog is null) return;

        PdfObject? acroRaw = catalog.Get(new PdfName("AcroForm"));
        if (Resolve(doc, acroRaw) is not PdfDictionary acro) return;

        // Only set /NeedAppearances if it was already present (e.g., set by FlattenTests).
        // Bootstrap-generated AcroForms should remain clean of /NeedAppearances since we
        // generate appearance streams immediately.
        if (acro.ContainsKey(new PdfName("NeedAppearances")))
            acro[new PdfName("NeedAppearances")] = PdfBoolean.False;
    }

    /// <summary>Reads /Q from the field dict directly (so dict mutations are live).</summary>
    private static int GetQuadding(PdfDocument doc, PdfTextField field)
    {
        PdfObject? qRaw = field.Dict.Get(new PdfName("Q"));
        if (qRaw is PdfInteger qi) return qi.Value;
        return field.Quadding; // fallback to cached value
    }

    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : obj;

    /// <summary>Formats a number with invariant culture; omits trailing zeros for integers.</summary>
    private static string FormatNumber(double value)
    {
        // Use up to 4 decimal places, strip trailing zeros
        string s = value.ToString("0.####", CultureInfo.InvariantCulture);
        return s;
    }

    // ─── Button appearance generation ─────────────────────────────────────────

    /// <summary>
    /// Generates and stores Form-XObject appearances for a checkbox or radio widget that
    /// lacks an /AP /N entry. The on-state is drawn as a VECTOR PATH — a stroked check-mark for
    /// checkboxes, a filled circle for radio buttons — so it renders in any viewer without depending
    /// on dingbat-font support. The off-state appearance is always an empty content stream.
    /// If the widget already has /AP /N states, this method is a no-op.
    /// </summary>
    public static void EnsureButtonAppearance(
        PdfDocument doc,
        PdfDictionary widget,
        string onStateName,
        bool isRadio)
    {
        // ── Guard: already has /AP /N with usable states ────────────────────
        PdfObject? existingApRaw = widget.Get(new PdfName("AP"));
        if (Resolve(doc, existingApRaw) is PdfDictionary existingAp)
        {
            PdfObject? existingNRaw = existingAp.Get(new PdfName("N"));
            if (Resolve(doc, existingNRaw) is PdfDictionary existingN)
            {
                // Check for at least one non-Off state key
                foreach (KeyValuePair<PdfName, PdfObject> kvp in existingN)
                {
                    if (kvp.Key.Value != "Off")
                        return; // Already has an on-state — do not overwrite
                }
            }
        }

        // ── Read /Rect → widget dimensions ───────────────────────────────────
        if (!TryGetRect(doc, widget, out double x0, out double y0, out double x1, out double y1))
            return;

        double w = Math.Abs(x1 - x0);
        double h = Math.Abs(y1 - y0);

        if (w <= 0 || h <= 0)
            return;

        // ── Build on-state content: draw the mark as a VECTOR PATH ────────────
        // A path renders identically in every viewer; a ZapfDingbats glyph depends on the renderer
        // supporting the dingbat font's special encoding (ours draws the literal byte otherwise).
        string onContent;
        if (isRadio)
        {
            // Filled circle, centred, radius 0.3·min(w,h), via 4 Bézier quadrants (k = 0.5523·r).
            double cx = w / 2.0, cy = h / 2.0;
            double r = Math.Min(w, h) * 0.3;
            double k = r * 0.5523;
            string N(double v) => FormatNumber(v);
            onContent =
                "q\n0 g\n" +
                $"{N(cx + r)} {N(cy)} m\n" +
                $"{N(cx + r)} {N(cy + k)} {N(cx + k)} {N(cy + r)} {N(cx)} {N(cy + r)} c\n" +
                $"{N(cx - k)} {N(cy + r)} {N(cx - r)} {N(cy + k)} {N(cx - r)} {N(cy)} c\n" +
                $"{N(cx - r)} {N(cy - k)} {N(cx - k)} {N(cy - r)} {N(cx)} {N(cy - r)} c\n" +
                $"{N(cx + k)} {N(cy - r)} {N(cx + r)} {N(cy - k)} {N(cx + r)} {N(cy)} c\n" +
                "f\nQ";
        }
        else
        {
            // Checkmark: lower-left → bottom-middle → upper-right, stroked with round caps/joins.
            double lw = Math.Max(1.0, Math.Min(w, h) * 0.12);
            string N(double v) => FormatNumber(v);
            onContent =
                $"q\n{N(lw)} w\n1 J\n1 j\n0 G\n" +
                $"{N(w * 0.20)} {N(h * 0.52)} m\n" +
                $"{N(w * 0.42)} {N(h * 0.28)} l\n" +
                $"{N(w * 0.80)} {N(h * 0.78)} l\nS\nQ";
        }

        byte[] onBytes  = Encoding.ASCII.GetBytes(onContent);
        byte[] offBytes = Array.Empty<byte>();

        PdfArray bBox = new PdfArray
        {
            new PdfReal(0), new PdfReal(0), new PdfReal(w), new PdfReal(h)
        };
        PdfArray matrix = new PdfArray
        {
            new PdfInteger(1), new PdfInteger(0),
            new PdfInteger(0), new PdfInteger(1),
            new PdfInteger(0), new PdfInteger(0)
        };

        // On-state XObject (vector path, no /Resources needed)
        var onDict = new PdfDictionary
        {
            [new PdfName("Type")]      = new PdfName("XObject"),
            [new PdfName("Subtype")]   = new PdfName("Form"),
            [new PdfName("BBox")]      = bBox,
            [new PdfName("Matrix")]    = matrix
        };
        var onStream = new PdfStream(onDict, onBytes);
        PdfIndirectReference onRef = doc.RegisterObject(onStream);

        // Off-state XObject (empty)
        var offDict = new PdfDictionary
        {
            [new PdfName("Type")]    = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Form"),
            [new PdfName("BBox")]    = bBox,
            [new PdfName("Matrix")]  = matrix
        };
        var offStream = new PdfStream(offDict, offBytes);
        PdfIndirectReference offRef = doc.RegisterObject(offStream);

        // Build /AP /N << /<onStateName> <onRef> /Off <offRef> >>
        var nDict = new PdfDictionary
        {
            [new PdfName(onStateName)] = onRef,
            [new PdfName("Off")]       = offRef
        };

        var apDict = new PdfDictionary
        {
            [new PdfName("N")] = nDict
        };

        widget[new PdfName("AP")] = apDict;
    }
}
