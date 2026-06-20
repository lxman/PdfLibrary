using System.Globalization;
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
        if (field is PdfTextField t)
        {
            if (t.IsComb && t.MaxLength.HasValue)
                RegenerateCombTextField(doc, t);
            else if (t.IsMultiline)
                RegenerateMultilineTextField(doc, t);
            else
                RegenerateTextField(doc, t);
            return;
        }

        // TODO later tasks: choice fields
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

        foreach (PdfDictionary widget in field.Widgets)
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

        foreach (PdfDictionary widget in field.Widgets)
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

            // Greedy word-wrap
            string[] words = value.Split(' ');
            var lines = new List<string>();
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
            if (current.Length > 0)
                lines.Add(current);

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

        foreach (PdfDictionary widget in field.Widgets)
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
}
