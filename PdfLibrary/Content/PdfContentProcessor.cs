using PdfLibrary.Content.Operators;

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

            case FillOperator:
                OnFill(evenOdd: false);
                break;

            case FillEvenOddOperator:
                OnFill(evenOdd: true);
                break;

            case FillAndStrokeOperator:
                OnFillAndStroke();
                break;

            case EndPathOperator:
                OnEndPath();
                break;

            // XObject operators
            case InvokeXObjectOperator xobj:
                OnInvokeXObject(xobj.XObjectName);
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
    protected virtual void OnShowText(Core.Primitives.PdfString text) { }
    protected virtual void OnShowTextWithPositioning(Core.Primitives.PdfArray array) { }
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
    protected virtual void OnGenericOperator(GenericOperator op) { }
}
