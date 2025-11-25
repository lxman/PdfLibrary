using PdfLibrary.Content.Operators;
using PdfLibrary.Core.Primitives;
using Logging;

namespace PdfLibrary.Content;

/// <summary>
/// Processes PDF content stream operators and maintains graphics state
/// Base class for text extraction, content analysis, and rendering
/// </summary>
public abstract class PdfContentProcessor
{
    private readonly Stack<PdfGraphicsState> _stateStack = new();
    protected PdfGraphicsState CurrentState { get; private set; } = new();

    /// <summary>
    /// Tracks whether a clipping operator (W or W*) is pending.
    /// Per PDF spec, W/W* modify the clipping path but don't apply it until
    /// a path painting or path ending operator is encountered.
    /// </summary>
    protected bool PendingClip { get; private set; }
    protected bool PendingClipEvenOdd { get; private set; }

    /// <summary>
    /// Processes a list of operators from a content stream
    /// </summary>
    public void ProcessOperators(List<PdfOperator> operators)
    {
        foreach (PdfOperator op in operators)
        {
            ProcessOperator(op);
        }
    }

    /// <summary>
    /// Processes a single operator and updates graphics state
    /// </summary>
    protected virtual void ProcessOperator(PdfOperator op)
    {
        switch (op)
        {
            // Graphics state operators
            case SaveGraphicsStateOperator:
                _stateStack.Push(CurrentState.Clone());
                break;

            case RestoreGraphicsStateOperator:
                if (_stateStack.Count > 0)
                    CurrentState = _stateStack.Pop();
                break;

            case ConcatenateMatrixOperator cm:
                CurrentState.ConcatenateMatrix(cm.A, cm.B, cm.C, cm.D, cm.E, cm.F);
                OnMatrixChanged();
                break;

            case SetLineWidthOperator w:
                CurrentState.LineWidth = w.Width;
                break;

            case SetLineCapOperator cap:
                CurrentState.LineCap = cap.Style;
                break;

            case SetLineJoinOperator join:
                CurrentState.LineJoin = join.Style;
                break;

            case SetMiterLimitOperator miter:
                CurrentState.MiterLimit = miter.Limit;
                break;

            case SetDashPatternOperator dash:
                // Convert PdfArray to double array
                var dashArray = new double[dash.DashArray.Count];
                for (var i = 0; i < dash.DashArray.Count; i++)
                {
                    dashArray[i] = dash.DashArray[i] switch
                    {
                        PdfInteger pi => pi.Value,
                        PdfReal pr => pr.Value,
                        _ => 0
                    };
                }
                CurrentState.DashPattern = dashArray.Length > 0 ? dashArray : null;
                CurrentState.DashPhase = dash.DashPhase;
                break;

            case SetFlatnessOperator flatness:
                CurrentState.Flatness = flatness.Flatness;
                break;

            // Text object operators
            case BeginTextOperator:
                CurrentState.BeginText();
                OnBeginText();
                break;

            case EndTextOperator:
                OnEndText();
                break;

            // Text state operators
            case SetTextFontOperator tf:
                CurrentState.FontName = tf.Font;
                CurrentState.FontSize = tf.Size;
                OnFontChanged();
                break;

            case SetCharSpacingOperator tc:
                CurrentState.CharacterSpacing = tc.Spacing;
                break;

            case SetWordSpacingOperator tw:
                CurrentState.WordSpacing = tw.Spacing;
                break;

            case SetHorizontalScalingOperator tz:
                CurrentState.HorizontalScaling = tz.Scale;
                break;

            case SetTextLeadingOperator tl:
                CurrentState.Leading = tl.Leading;
                break;

            case SetTextRenderingModeOperator tr:
                CurrentState.RenderingMode = tr.Mode;
                break;

            case SetTextRiseOperator ts:
                CurrentState.TextRise = ts.Rise;
                break;

            // Text positioning operators
            case SetTextMatrixOperator tm:
                CurrentState.SetTextMatrix(tm.A, tm.B, tm.C, tm.D, tm.E, tm.F);
                OnTextPositionChanged();
                break;

            case MoveTextPositionOperator td:
                CurrentState.MoveTextPosition(td.Tx, td.Ty);
                OnTextPositionChanged();
                break;

            case MoveTextPositionAndSetLeadingOperator td:
                CurrentState.Leading = -td.Ty;
                CurrentState.MoveTextPosition(td.Tx, td.Ty);
                OnTextPositionChanged();
                break;

            case MoveToNextLineOperator:
                CurrentState.MoveToNextLine();
                OnTextPositionChanged();
                break;

            // Text showing operators
            case ShowTextOperator tj:
                OnShowText(tj.Text);
                break;

            case ShowTextWithPositioningOperator tj:
                OnShowTextWithPositioning(tj.Array);
                break;

            case MoveToNextLineAndShowTextOperator quote:
                CurrentState.MoveToNextLine();
                OnTextPositionChanged();
                OnShowText(quote.Text);
                break;

            case SetSpacingMoveAndShowTextOperator dquote:
                CurrentState.WordSpacing = dquote.WordSpacing;
                CurrentState.CharacterSpacing = dquote.CharSpacing;
                CurrentState.MoveToNextLine();
                OnTextPositionChanged();
                OnShowText(dquote.Text);
                break;

            // Path construction operators
            case MoveToOperator m:
                OnMoveTo(m.X, m.Y);
                break;

            case LineToOperator l:
                OnLineTo(l.X, l.Y);
                break;

            case CurveToOperator c:
                OnCurveTo(c.X1, c.Y1, c.X2, c.Y2, c.X3, c.Y3);
                break;

            case RectangleOperator re:
                OnRectangle(re.X, re.Y, re.Width, re.Height);
                break;

            case ClosePathOperator:
                OnClosePath();
                break;

            // Path painting operators
            case StrokeOperator:
                OnStroke();
                break;

            case CloseAndStrokeOperator:
                OnClosePath();
                OnStroke();
                break;

            case FillOperator:
                OnFill(evenOdd: false);
                break;

            case FillEvenOddOperator:
                OnFill(evenOdd: true);
                break;

            case FillAndStrokeOperator:
                OnFillAndStroke();
                break;

            case FillAndStrokeEvenOddOperator:
                OnFillAndStroke();
                break;

            case CloseAndFillAndStrokeOperator:
                OnClosePath();
                OnFillAndStroke();
                break;

            case CloseAndFillAndStrokeEvenOddOperator:
                OnClosePath();
                OnFillAndStroke();
                break;

            case EndPathOperator:
                OnEndPath();
                break;

            // Clipping path operators
            case ClipOperator:
                PendingClip = true;
                PendingClipEvenOdd = false;
                break;

            case ClipEvenOddOperator:
                PendingClip = true;
                PendingClipEvenOdd = true;
                break;

            // XObject operators
            case InvokeXObjectOperator xobj:
                OnInvokeXObject(xobj.XObjectName);
                break;

            // Inline image operator
            case InlineImageOperator inlineImg:
                OnInlineImage(inlineImg);
                break;

            // Color operators - Grayscale
            case SetStrokeGrayOperator g:
                CurrentState.SetStrokeGray(g.Gray);
                OnColorChanged();
                break;

            case SetFillGrayOperator g:
                CurrentState.SetFillGray(g.Gray);
                OnColorChanged();
                break;

            // Color operators - RGB
            case SetStrokeRgbOperator rg:
                CurrentState.SetStrokeRgb(rg.R, rg.G, rg.B);
                OnColorChanged();
                break;

            case SetFillRgbOperator rg:
                PdfLogger.Log(LogCategory.Graphics, $"[rg OPERATOR] R={rg.R:F2}, G={rg.G:F2}, B={rg.B:F2}");
                CurrentState.SetFillRgb(rg.R, rg.G, rg.B);
                OnColorChanged();
                break;

            // Color operators - CMYK
            case SetStrokeCmykOperator cmyk:
                CurrentState.SetStrokeCmyk(cmyk.C, cmyk.M, cmyk.Y, cmyk.K);
                OnColorChanged();
                break;

            case SetFillCmykOperator cmyk:
                CurrentState.SetFillCmyk(cmyk.C, cmyk.M, cmyk.Y, cmyk.K);
                OnColorChanged();
                break;

            // Color space operators
            case SetStrokeColorSpaceOperator cs:
                CurrentState.StrokeColorSpace = cs.ColorSpace;
                // Initialize default color components for the new color space
                CurrentState.StrokeColor = cs.ColorSpace switch
                {
                    "DeviceGray" => [0.0],
                    "DeviceRGB" => [0.0, 0.0, 0.0],
                    "DeviceCMYK" => [0.0, 0.0, 0.0, 1.0],
                    _ => [0.0]
                };
                OnColorChanged();
                break;

            case SetFillColorSpaceOperator cs:
                PdfLogger.Log(LogCategory.Graphics, $"[cs OPERATOR] ColorSpace={cs.ColorSpace}");
                CurrentState.FillColorSpace = cs.ColorSpace;
                // Initialize default color components for the new color space
                CurrentState.FillColor = cs.ColorSpace switch
                {
                    "DeviceGray" => [0.0],
                    "DeviceRGB" => [0.0, 0.0, 0.0],
                    "DeviceCMYK" => [0.0, 0.0, 0.0, 1.0],
                    _ => [0.0]
                };
                OnColorChanged();
                break;

            // Generic color operators
            case SetStrokeColorOperator sc:
                CurrentState.StrokeColor = sc.Components;
                OnColorChanged();
                break;

            case SetFillColorOperator sc:
                PdfLogger.Log(LogCategory.Graphics, $"[sc OPERATOR] Components=[{string.Join(", ", sc.Components.Select(c => c.ToString("F2")))}]");
                CurrentState.FillColor = sc.Components;
                OnColorChanged();
                break;

            case SetStrokeColorExtendedOperator scn:
                // Only update color if we have components (0 components means use current color)
                if (scn.Components.Count > 0)
                    CurrentState.StrokeColor = scn.Components;
                OnColorChanged();
                break;

            case SetFillColorExtendedOperator scn:
                PdfLogger.Log(LogCategory.Graphics, $"[scn OPERATOR] Operands={scn.Operands.Count}, Components=[{string.Join(", ", scn.Components.Select(c => c.ToString("F2")))}], Pattern={scn.PatternName}");
                // Only update color if we have components (0 components means use current color)
                if (scn.Components.Count > 0)
                    CurrentState.FillColor = scn.Components;
                OnColorChanged();
                break;

            // Generic operators
            case GenericOperator generic:
                OnGenericOperator(generic);
                break;
        }
    }

    // Virtual methods for derived classes to override

    protected virtual void OnMatrixChanged() { }
    protected virtual void OnBeginText() { }
    protected virtual void OnEndText() { }
    protected virtual void OnFontChanged() { }
    protected virtual void OnTextPositionChanged() { }
    protected virtual void OnShowText(PdfString text) { }
    protected virtual void OnShowTextWithPositioning(PdfArray array) { }
    protected virtual void OnMoveTo(double x, double y) { }
    protected virtual void OnLineTo(double x, double y) { }
    protected virtual void OnCurveTo(double x1, double y1, double x2, double y2, double x3, double y3) { }
    protected virtual void OnRectangle(double x, double y, double width, double height) { }
    protected virtual void OnClosePath() { }
    protected virtual void OnStroke() { }
    protected virtual void OnFill(bool evenOdd) { }
    protected virtual void OnFillAndStroke() { }
    protected virtual void OnEndPath() { }
    protected virtual void OnInvokeXObject(string name) { }
    protected virtual void OnInlineImage(InlineImageOperator inlineImage) { }
    protected virtual void OnColorChanged() { }
    protected virtual void OnGenericOperator(GenericOperator op) { }

    /// <summary>
    /// Clears the pending clip flag. Should be called after applying clipping
    /// or after path-terminating operators.
    /// </summary>
    protected void ClearPendingClip()
    {
        PendingClip = false;
        PendingClipEvenOdd = false;
    }
}
