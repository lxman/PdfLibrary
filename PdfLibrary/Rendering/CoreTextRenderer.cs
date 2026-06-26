using System.Numerics;
using Logging;
using PdfLibrary.Content;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Rendering;

/// <summary>
/// Renders text glyphs by resolving each character to a path and emitting FillPath/StrokePath
/// to the render target — the SkiaSharp-free core text path. For embedded fonts the glyph comes
/// from the font program; for non-embedded fonts a substitute font resolved via ISystemFontProvider
/// is used instead. Returns false only when no rendering path is available (no embedded metrics AND
/// no usable substitute); returns false to indicate the run was not rendered.
/// </summary>
internal sealed class CoreTextRenderer(IRenderTarget target, GlyphPathService glyphPaths, ISystemFontProvider fontProvider)
{
    private readonly SubstituteFontResolver _substitutes = new(fontProvider);

    public bool Render(string text, List<double> glyphWidths, PdfGraphicsState state,
        PdfFont? font, List<int>? charCodes)
    {
        if (font is null) return false;
        try
        {
            EmbeddedFontMetrics? metrics = font.GetEmbeddedMetrics();
            if (metrics is not { IsValid: true })
                return RenderWithSubstitute(text, glyphWidths, state, font);

            bool applyBold = ShouldApplyFauxBold(font);
            var tHs = (float)state.HorizontalScaling / 100f;
            double currentX = 0;
            int loopCount = charCodes?.Count ?? text.Length;

            for (var i = 0; i < loopCount; i++)
            {
                ushort charCode = charCodes is not null && i < charCodes.Count
                    ? (ushort)charCodes[i]
                    : text[i];

                ushort glyphId = ResolveGlyphId(metrics, font, charCode, out string? resolvedGlyphName);

                if (glyphId == 0) { Advance(ref currentX, glyphWidths, i, state); continue; }

                GlyphOutline? outline = metrics.IsType1Font && resolvedGlyphName is not null
                    ? metrics.GetGlyphOutlineByName(resolvedGlyphName)
                    : metrics.GetGlyphOutline(glyphId);

                if (outline is null)
                {
                    if (charCode == 151 && i < glyphWidths.Count && glyphWidths[i] > 0.1)
                        RenderEmDash(glyphWidths[i], state, currentX, tHs);
                    Advance(ref currentX, glyphWidths, i, state);
                    continue;
                }
                if (outline.IsEmpty) { Advance(ref currentX, glyphWidths, i, state); continue; }

                IPathBuilder glyphSpace =
                    glyphPaths.GetGlyphPath(metrics, glyphId, (float)state.FontSize, outline, resolvedGlyphName);
                // GlyphToUser positions the glyph in text-user space; * Ctm bakes the current
                // transform into the path, exactly as regular paths are built (PdfRenderer
                // transforms every point by Ctm) and as Type3 glyphs end with "* Ctm". FillPath
                // applies ONLY the page initial-transform, never the Ctm — so without this, text
                // inside a cm-transformed context (a Form XObject figure) loses the transform and
                // collapses toward the origin.
                Matrix3x2 toUser = GlyphPlacement.GlyphToUser(state, currentX, tHs) * state.Ctm;
                IPathBuilder userPath = glyphSpace.Transform(toUser);

                try { EmitGlyph(userPath, state, toUser, applyBold); }
                catch (Exception ex) { PdfLogger.Log(LogCategory.Text, () => $"glyph emit failed (embedded): {ex.Message}"); }
                Advance(ref currentX, glyphWidths, i, state);
            }

            return true;
        }
        catch
        {
            // Setup failure (font parsing, glyph resolution): report not-handled so the
            // Setup failure: returns false. Per-glyph emit failures are caught inside the loop.
            return false;
        }
    }

    private bool RenderWithSubstitute(string text, List<double> glyphWidths,
        PdfGraphicsState state, PdfFont font)
    {
        EmbeddedFontMetrics? sub = _substitutes.Resolve(font.BaseFont, font.GetDescriptor());
        if (sub is not { IsValid: true }) return false; // no substitute available → nothing to draw

        if (state.RenderingMode is 3 or 7) return true;  // invisible — no glyphs needed

        bool applyBold = ShouldApplyFauxBold(font);
        var tHs = (float)state.HorizontalScaling / 100f;
        bool flipX = state.FontSize < 0 != state.TextMatrix.M11 < 0;
        double currentX = 0;

        for (var i = 0; i < text.Length; i++)
        {
            string ch = DecomposeLigature(text[i]);
            double w = i < glyphWidths.Count ? glyphWidths[i] : 0;
            double subW = ch.Length > 0 ? w / ch.Length : 0;

            foreach (char c in ch)
            {
                try
                {
                    ushort glyphId = sub.GetGlyphId(c);
                    if (glyphId != 0)
                    {
                        GlyphOutline? outline = sub.GetGlyphOutline(glyphId);
                        if (outline is { IsEmpty: false })
                        {
                            IPathBuilder glyphSpace = glyphPaths.GetGlyphPath(
                                sub, glyphId, (float)state.FontSize, outline, resolvedGlyphName: null);
                            // CTM must be baked into the path — FillPath applies only the page
                            // initial-transform. Omitting * state.Ctm reproduces the figure-label
                            // bug fixed in commit 8429607.
                            Matrix3x2 toUser = GlyphPlacement.GlyphToUser(state, currentX, tHs) * state.Ctm;
                            IPathBuilder userPath = glyphSpace.Transform(toUser);
                            EmitGlyph(userPath, state, toUser, applyBold);
                        }
                    }
                }
                catch (Exception ex)
                {
                    PdfLogger.Log(LogCategory.Text, () => $"glyph emit failed (substitute): {ex.Message}");
                }
                currentX += subW * (flipX ? -1.0 : 1.0);
            }
        }
        return true;
    }

    // Port of TextRenderer.cs ligature decomposition (preserves glyph advance width split).
    private static string DecomposeLigature(char c) => c switch
    {
        'ﬀ' => "ff", 'ﬁ' => "fi", 'ﬂ' => "fl",
        'ﬃ' => "ffi", 'ﬄ' => "ffl",
        _ => c.ToString()
    };

    private void EmitGlyph(IPathBuilder userPath, PdfGraphicsState state, Matrix3x2 toUser, bool applyBold)
    {
        int rm = state.RenderingMode;
        bool fill = rm is 0 or 2 or 4 or 6;
        bool stroke = rm is 1 or 2 or 5 or 6;
        bool invisible = rm is 3 or 7;
        if (invisible) return;

        if (fill) target.FillPath(userPath, state, evenOdd: true);

        if (stroke)
        {
            target.StrokePath(userPath, state);
        }
        else if (applyBold && fill)
        {
            // Synthetic bold: stroke the fill outline with the FILL color, ~4% em in user space.
            double scaleU = Math.Sqrt(Math.Abs(toUser.M11 * toUser.M22 - toUser.M12 * toUser.M21));
            double boldWidthUser = state.FontSize * 0.04 * scaleU;

            PdfGraphicsState bold = state.Clone();
            bold.LineWidth = boldWidthUser;
            bold.ResolvedStrokeColor = state.ResolvedFillColor;
            bold.ResolvedStrokeColorSpace = state.ResolvedFillColorSpace;
            bold.StrokeAlpha = state.FillAlpha;
            target.StrokePath(userPath, bold);
        }
    }

    private void RenderEmDash(double glyphWidth, PdfGraphicsState state, double currentX, float tHs)
    {
        // Em dash fallback (port of RenderEmDashFallback): a rectangle in glyph space, positioned
        // through the same glyph->user matrix (which applies the Y-flip).
        var emDashY = (float)state.FontSize * 0.35f;
        var emDashHeight = (float)state.FontSize * 0.06f;
        var emDashWidth = (float)glyphWidth * (float)state.FontSize;

        var rect = new PathBuilder();
        // Original SKRect(0, -emDashY-emDashHeight, emDashWidth, -emDashY) => x,y,w,h:
        rect.Rectangle(0, -emDashY - emDashHeight, emDashWidth, emDashHeight);

        Matrix3x2 toUser = GlyphPlacement.GlyphToUser(state, currentX, tHs) * state.Ctm;
        IPathBuilder userPath = rect.Transform(toUser);
        target.FillPath(userPath, state, evenOdd: true);
    }

    private static bool ShouldApplyFauxBold(PdfFont font)
    {
        PdfFontDescriptor? descriptor = font.GetDescriptor();
        if (descriptor is null) return false;
        bool isForceBoldFlag = descriptor.IsBold;
        bool isBoldName = font.BaseFont?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true;
        bool isBoldStemV = descriptor.StemV >= 120;
        bool embeddedOutlineAlreadyBold = isBoldName || isBoldStemV;
        return isForceBoldFlag && !embeddedOutlineAlreadyBold;
    }

    private static void Advance(ref double currentX, List<double> glyphWidths, int i, PdfGraphicsState state)
    {
        if (i >= glyphWidths.Count) return;
        // XOR: FontSize<0 and TextMatrix.M11<0 each flip; two flips cancel.
        bool flipX = state.FontSize < 0 != state.TextMatrix.M11 < 0;
        currentX += glyphWidths[i] * (flipX ? -1.0 : 1.0);
    }

    // === Verbatim port of TextRenderer.ResolveGlyphId (603-702) ===
    private static ushort ResolveGlyphId(EmbeddedFontMetrics metrics, PdfFont font, ushort charCode,
        out string? resolvedGlyphName)
    {
        ushort glyphId;
        resolvedGlyphName = null;

        if ((metrics.IsCffFont || metrics.IsType1Font) && font.Encoding is not null)
        {
            resolvedGlyphName = font.Encoding.GetGlyphName(charCode);
            glyphId = resolvedGlyphName is not null ? metrics.GetGlyphIdByName(resolvedGlyphName) : (ushort)0;

            if (glyphId == 0 && metrics.IsType1Font)
            {
                string? builtInName = metrics.GetType1GlyphNameByCharCode(charCode);
                if (builtInName is not null)
                {
                    resolvedGlyphName = builtInName;
                    glyphId = metrics.GetGlyphIdByName(builtInName);
                }
            }
        }
        else if (font is Type0Font type0Font && metrics.IsType1Font && type0Font.ToUnicode is not null)
        {
            string? unicode = type0Font.ToUnicode.Lookup(charCode);
            if (unicode is not null)
            {
                resolvedGlyphName = GlyphList.GetGlyphName(unicode);
                if (resolvedGlyphName is not null)
                {
                    glyphId = metrics.GetGlyphIdByName(resolvedGlyphName);
                }
                else if (unicode.Length == 1 && char.IsAscii(unicode[0]))
                {
                    resolvedGlyphName = unicode;
                    glyphId = metrics.GetGlyphIdByName(resolvedGlyphName);
                }
                else
                {
                    glyphId = 0;
                }
            }
            else if (type0Font.DescendantFont is CidFont cidFont)
            {
                glyphId = (ushort)cidFont.MapCidToGid(charCode);
            }
            else
            {
                glyphId = metrics.GetGlyphId(charCode);
            }
        }
        else
        {
            if (font is Type0Font { DescendantFont: CidFont cidFont })
            {
                int cidAfterMap = cidFont.MapCidToGid(charCode);
                glyphId = metrics.IsCffFont
                    ? metrics.GetGlyphIdByCid((ushort)cidAfterMap)
                    : (ushort)cidAfterMap;
            }
            else
            {
                glyphId = metrics.GetGlyphId(charCode);
            }
        }

        return glyphId;
    }
}
