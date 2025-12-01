using Logging;
using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering;

/// <summary>
/// Applies ExtGState (Extended Graphics State) dictionaries to PdfGraphicsState.
/// Handles all entries defined in ISO 32000-1:2008 Table 58.
/// </summary>
internal class ExtGStateApplier(PdfDocument? document, IRenderTarget target)
{
    /// <summary>
    /// Callback for rendering soft mask groups. Set by PdfRenderer.
    /// </summary>
    public Action<PdfSoftMask>? RenderSoftMaskGroupCallback { get; set; }

    /// <summary>
    /// Applies ExtGState dictionary parameters to the current graphics state.
    /// ISO 32000-1:2008 Table 58 - Entries in a graphics state parameter dictionary
    /// </summary>
    public void ApplyExtGState(PdfDictionary extGState, PdfGraphicsState currentState)
    {
        foreach (var entry in extGState)
        {
            var key = entry.Key.Value;
            var value = entry.Value;

            // Resolve indirect references
            if (value is PdfIndirectReference reference && document is not null)
                value = document.ResolveReference(reference);

            switch (key)
            {
                case "Type":
                    // Ignore - just identifies this as an ExtGState dictionary
                    break;

                // Line width (LW)
                case "LW":
                    if (value.ToDoubleOrNull() is double lw)
                    {
                        currentState.LineWidth = lw;
                        PdfLogger.Log(LogCategory.Graphics, $"  LW (LineWidth) = {lw}");
                    }
                    break;

                // Line cap (LC)
                case "LC":
                    if (value is PdfInteger lc)
                    {
                        currentState.LineCap = lc.Value;
                        PdfLogger.Log(LogCategory.Graphics, $"  LC (LineCap) = {lc.Value}");
                    }
                    break;

                // Line join (LJ)
                case "LJ":
                    if (value is PdfInteger lj)
                    {
                        currentState.LineJoin = lj.Value;
                        PdfLogger.Log(LogCategory.Graphics, $"  LJ (LineJoin) = {lj.Value}");
                    }
                    break;

                // Miter limit (ML)
                case "ML":
                    if (value.ToDoubleOrNull() is double ml)
                    {
                        currentState.MiterLimit = ml;
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
                                dashPattern[i] = pattern[i].ToDouble();
                            }
                            currentState.DashPattern = dashPattern.Length > 0 ? dashPattern : null;
                        }
                        currentState.DashPhase = dashArray[1].ToDouble();
                        PdfLogger.Log(LogCategory.Graphics, $"  D (DashPattern) = [{string.Join(", ", currentState.DashPattern ?? [])}] {currentState.DashPhase}");
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
                        currentState.StrokeOverprint = op.Value;
                        PdfLogger.Log(LogCategory.Graphics, $"  OP (StrokeOverprint) = {op.Value}");
                    }
                    break;

                // Overprint for non-stroking (op)
                case "op":
                    if (value is PdfBoolean opFill)
                    {
                        currentState.FillOverprint = opFill.Value;
                        PdfLogger.Log(LogCategory.Graphics, $"  op (FillOverprint) = {opFill.Value}");
                    }
                    break;

                // Overprint mode (OPM)
                case "OPM":
                    if (value is PdfInteger opm)
                    {
                        currentState.OverprintMode = opm.Value;
                        PdfLogger.Log(LogCategory.Graphics, $"  OPM (OverprintMode) = {opm.Value}");
                    }
                    break;

                // Font (Font) - array of [fontRef size]
                case "Font":
                    if (value is PdfArray fontArray && fontArray.Count >= 2)
                    {
                        // Get font reference and size
                        var fontRef = fontArray[0];
                        if (fontRef is PdfIndirectReference fRef && document is not null)
                            fontRef = document.ResolveReference(fRef);

                        var fontSize = fontArray[1].ToDoubleOrNull();
                        if (fontSize.HasValue)
                        {
                            currentState.FontSize = fontSize.Value;
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
                    if (value.ToDoubleOrNull() is double fl)
                    {
                        currentState.Flatness = fl;
                        PdfLogger.Log(LogCategory.Graphics, $"  FL (Flatness) = {fl}");
                    }
                    break;

                // Smoothness tolerance (SM)
                case "SM":
                    if (value.ToDoubleOrNull() is double sm)
                    {
                        currentState.Smoothness = sm;
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
                    var blendMode = value switch
                    {
                        PdfName bmName => bmName.Value,
                        PdfArray bmArray when bmArray.Count > 0 && bmArray[0] is PdfName firstName => firstName.Value,
                        _ => "Normal"
                    };
                    currentState.BlendMode = blendMode;
                    PdfLogger.Log(LogCategory.Graphics, $"  BM (BlendMode) = {blendMode}");
                    break;

                // Soft mask (SMask)
                case "SMask":
                    ApplySoftMask(value, currentState);
                    break;

                // Stroking alpha (CA)
                case "CA":
                    if (value.ToDoubleOrNull() is double ca)
                    {
                        currentState.StrokeAlpha = ca;
                        PdfLogger.Log(LogCategory.Graphics, $"  CA (StrokeAlpha) = {ca}");
                    }
                    break;

                // Non-stroking alpha (ca)
                case "ca":
                    if (value.ToDoubleOrNull() is double caFill)
                    {
                        currentState.FillAlpha = caFill;
                        PdfLogger.Log(LogCategory.Graphics, $"  ca (FillAlpha) = {caFill}");
                    }
                    break;

                // Alpha is shape (AIS)
                case "AIS":
                    if (value is PdfBoolean ais)
                    {
                        currentState.AlphaIsShape = ais.Value;
                        PdfLogger.Log(LogCategory.Graphics, $"  AIS (AlphaIsShape) = {ais.Value}");
                    }
                    break;

                // Text knockout (TK)
                case "TK":
                    if (value is PdfBoolean tk)
                    {
                        currentState.TextKnockout = tk.Value;
                        PdfLogger.Log(LogCategory.Graphics, $"  TK (TextKnockout) = {tk.Value}");
                    }
                    break;

                default:
                    PdfLogger.Log(LogCategory.Graphics, $"  Unknown ExtGState key: {key}");
                    break;
            }
        }

        // Notify render target of graphics state change
        target.OnGraphicsStateChanged(currentState);
    }

    /// <summary>
    /// Applies a soft mask from the SMask entry
    /// </summary>
    private void ApplySoftMask(PdfObject value, PdfGraphicsState currentState)
    {
        if (value is PdfName nameValue)
        {
            if (nameValue.Value == "None")
            {
                // Clear the soft mask
                currentState.SoftMask = null;
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
        if (smaskDict.TryGetValue(new PdfName("S"), out var sObj) && sObj is PdfName subtype)
        {
            softMask = softMask with { Subtype = subtype.Value };
        }

        // Get transparency group (G) - Form XObject
        if (smaskDict.TryGetValue(new PdfName("G"), out var gObj))
        {
            var groupStream = gObj switch
            {
                PdfStream stream => stream,
                PdfIndirectReference gRef when document is not null => document.ResolveReference(gRef) as PdfStream,
                _ => null
            };
            softMask = softMask with { Group = groupStream };
        }

        // Get backdrop color (BC)
        if (smaskDict.TryGetValue(new PdfName("BC"), out var bcObj) && bcObj is PdfArray bcArray)
        {
            var backdropColor = new double[bcArray.Count];
            for (var i = 0; i < bcArray.Count; i++)
            {
                backdropColor[i] = bcArray[i].ToDouble();
            }
            softMask = softMask with { BackdropColor = backdropColor };
        }

        // Get transfer function (TR)
        if (smaskDict.TryGetValue(new PdfName("TR"), out var trObj))
        {
            softMask = softMask with { TransferFunction = trObj };
        }

        currentState.SoftMask = softMask;
        PdfLogger.Log(LogCategory.Graphics, $"  SMask: Subtype={softMask.Subtype}, HasGroup={softMask.Group is not null}");

        // If we have a Group, render the mask using the callback
        if (softMask.Group is not null)
        {
            RenderSoftMaskGroupCallback?.Invoke(softMask);
        }
    }
}
