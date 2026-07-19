using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A (ISO 19005-2/3, clause 6.2.4.2 test 2), mirroring veraPDF's PDICCBasedCMYK rule
/// (<c>overprintFlag == false || OPM == 0</c>): when an ICCBased CMYK colour space is <b>painted</b>, the
/// overprint mode (OPM, from an ExtGState) shall not be 1 if the matching overprint is enabled — stroke
/// overprint (<c>/OP</c>) for a stroke, fill overprint (<c>/op</c>) for a fill.
///
/// The rule walks each page's content, tracking the graphics state the reference validator tracks: the fill
/// and stroke colour spaces (is each an ICCBased space whose profile stream declares <c>/N 4</c>?), the fill
/// and stroke overprint flags and the overprint mode (set only by the <c>gs</c> operator, pushed/popped by
/// <c>q</c>/<c>Q</c>), and evaluates the condition at each paint operator against the live state — exactly
/// where veraPDF constructs its PDICCBasedCMYK object. Form XObjects are not descended into: a form's
/// content runs under an implicit q/Q, so its graphics-state changes cannot leak into the page (a
/// form-internal violation is a safe under-report). CMYK-ness keys off the <c>/N</c> entry, not the ICC
/// header, matching the reference validator. Text-showing paints are left unchecked (a safe under-report).
/// </summary>
internal sealed class IccCmykOverprintRule : IConformanceRule
{
    public string RuleId => "icc-cmyk-overprint";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    /// <summary>The subset of the graphics state clause 6.2.4.2 test 2 depends on (a value type: copied by
    /// <c>q</c>/<c>Q</c>).</summary>
    private struct GraphicsState
    {
        public bool FillIccCmyk;
        public bool StrokeIccCmyk;
        public bool FillOverprint;
        public bool StrokeOverprint;
        public int OverprintMode;
    }

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        IReadOnlyList<PdfPage> pages;
        try { pages = context.Pages; }
        catch { yield break; }

        foreach (PdfPage page in pages)
        {
            if (!PageHasViolation(context, page))
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.2.4.2"),
                Message = "An ICCBased CMYK colour space is painted with overprinting enabled while the "
                          + "overprint mode (OPM) is 1; the overprint mode shall be 0 in that case.",
            };
            yield break; // one finding is enough to mark the document non-conformant
        }
    }

    private static bool PageHasViolation(ConformanceContext context, PdfPage page)
    {
        var resources = page.GetResources();

        var combined = new List<byte>();
        foreach (PdfStream content in page.GetContents())
        {
            try { combined.AddRange(content.GetDecodedData(context.Document.Decryptor)); }
            catch { /* an undecodable content stream is a different clause's concern */ }
            combined.Add((byte)'\n');
        }
        if (combined.Count == 0)
            return false;

        List<PdfOperator> operators;
        try { operators = PdfContentParser.Parse(combined.ToArray()); }
        catch { return false; } // unparseable content — never a false positive

        var gs = new GraphicsState();
        var stack = new Stack<GraphicsState>();

        foreach (PdfOperator op in operators)
        {
            switch (op.Name)
            {
                case "q": stack.Push(gs); break;
                case "Q": if (stack.Count > 0) gs = stack.Pop(); break;
                case "gs": ApplyExtGState(context, resources, NameOperand(op), ref gs); break;
                case "cs": gs.FillIccCmyk = IsIccCmyk(context, resources, NameOperand(op)); break;
                case "CS": gs.StrokeIccCmyk = IsIccCmyk(context, resources, NameOperand(op)); break;
                case "g" or "rg" or "k": gs.FillIccCmyk = false; break;   // a device fill colour space
                case "G" or "RG" or "K": gs.StrokeIccCmyk = false; break; // a device stroke colour space

                case "f" or "F" or "f*":
                    if (FillFails(gs)) return true;
                    break;
                case "S" or "s":
                    if (StrokeFails(gs)) return true;
                    break;
                case "B" or "B*" or "b" or "b*":
                    if (FillFails(gs) || StrokeFails(gs)) return true;
                    break;
            }
        }
        return false;
    }

    private static bool FillFails(GraphicsState gs) => gs.FillIccCmyk && gs.FillOverprint && gs.OverprintMode != 0;
    private static bool StrokeFails(GraphicsState gs) => gs.StrokeIccCmyk && gs.StrokeOverprint && gs.OverprintMode != 0;

    private static void ApplyExtGState(ConformanceContext context, PdfResources? resources, string? name, ref GraphicsState gs)
    {
        if (name is null || resources?.GetExtGState(name) is not { } ext)
            return;
        // /OP sets stroke overprint. Fill overprint comes from /op, or /OP when /op is absent. OPM from /OPM.
        // Each is applied only when the key is present, so an ExtGState leaves the inherited value untouched.
        if (Bool(context, ext, "OP") is { } strokeOp)
            gs.StrokeOverprint = strokeOp;
        if ((Bool(context, ext, "op") ?? Bool(context, ext, "OP")) is { } fillOp)
            gs.FillOverprint = fillOp;
        // /OPM is defined as an integer, but the reference validator reads it via getInteger(), which also
        // truncates a real toward zero — match that so a non-standard real /OPM does not diverge.
        switch (context.Resolve(ext.Get("OPM")))
        {
            case PdfInteger opm: gs.OverprintMode = opm.Value; break;
            case PdfReal opm: gs.OverprintMode = (int)opm.Value; break;
        }
    }

    private static bool? Bool(ConformanceContext context, PdfDictionary dict, string key) =>
        context.Resolve(dict.Get(key)) is PdfBoolean b ? b.Value : null;

    /// <summary>True when the named colour space resolves to an ICCBased space whose profile stream declares
    /// <c>/N 4</c> (CMYK). Keys off <c>/N</c>, not the embedded ICC header, matching the reference validator.</summary>
    private static bool IsIccCmyk(ConformanceContext context, PdfResources? resources, string? name)
    {
        if (name is null || resources?.GetColorSpaces() is not { } spaces)
            return false;
        if (context.Resolve(spaces.Get(name)) is not PdfArray array || array.Count < 2)
            return false;
        if (context.ResolveName(array[0]) != "ICCBased")
            return false;
        return context.Resolve(array[1]) is PdfStream stream
            && context.Resolve(stream.Dictionary.Get("N")) is PdfInteger n && n.Value == 4;
    }

    private static string? NameOperand(PdfOperator op) =>
        op.Operands.Count > 0 && op.Operands[0] is PdfName name ? name.Value : null;
}
