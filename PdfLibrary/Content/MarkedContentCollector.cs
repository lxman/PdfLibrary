using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Content;

/// <summary>
/// Walks a page's content stream (and, recursively, the Form XObjects it invokes) tracking the
/// marked-content structure that PDF/UA-1 (ISO 14289-1, 7.1) requires: every piece of <em>real</em> content
/// must be either tagged as a structure content item (enclosed in a marked-content sequence carrying an
/// <c>/MCID</c>, which links it to the logical structure tree) or marked as an <c>/Artifact</c>. Modelled on
/// <see cref="ToUnicodeUsageCollector"/> — it mirrors that collector's content-processor state tracking and
/// its Form XObject recursion, but instead of character codes it records:
/// <list type="bullet">
///   <item><b>Untagged real content</b> — a text-show, path-paint, shading, or image operator reached while
///     no marked-content sequence is open (effective nesting depth 0). Matterhorn checkpoint 01-004.</item>
///   <item><b>Artifact / tagged nesting</b> — an <c>/Artifact</c> sequence opened inside a tagged (MCID)
///     sequence, or vice versa; artifacts and real content must be sibling sequences, never nested. Matterhorn
///     checkpoints 01-005 / 01-006.</item>
///   <item><b>MCIDs whose sequence carries <c>/ActualText</c></b> — a content-stream text alternative, which a
///     Figure/Formula may rely on instead of an element-level <c>/Alt</c> or <c>/ActualText</c> (7.3).</item>
/// </list>
/// The effective nesting depth carries across Form XObject invocations, so a form wholly enclosed in a tagged
/// (or artifact) sequence is treated as tagged, while a form invoked outside any sequence whose own content is
/// not internally marked is correctly seen as untagged.
/// </summary>
internal sealed class MarkedContentCollector : PdfContentProcessor
{
    private readonly PdfResources? _resources;
    private readonly PdfDocument? _document;
    private readonly HashSet<int> _visitedForms; // shared across recursion — cycle guard on form object number

    // The marked-content sequences currently open in THIS stream, innermost last.
    private readonly List<Frame> _stack = new();

    // Marked-content context inherited from the Form XObject invocation that reached this stream (0 / false at
    // the page level). Lets a form's content be judged tagged/artifact by its invocation site.
    private readonly int _baseDepth;
    private readonly bool _baseArtifact;
    private readonly bool _baseTagged;

    private readonly record struct Frame(bool IsArtifact, bool IsTagged);

    public MarkedContentCollector(PdfResources? resources, PdfDocument? document)
        : this(resources, document, new HashSet<int>(), baseDepth: 0, baseArtifact: false, baseTagged: false)
    {
    }

    private MarkedContentCollector(PdfResources? resources, PdfDocument? document, HashSet<int> visitedForms,
        int baseDepth, bool baseArtifact, bool baseTagged)
    {
        _resources = resources;
        _document = document;
        _visitedForms = visitedForms;
        _baseDepth = baseDepth;
        _baseArtifact = baseArtifact;
        _baseTagged = baseTagged;
    }

    /// <summary>Real content was drawn while no marked-content sequence was open (untagged, not an artifact).</summary>
    public bool HasUntaggedContent { get; private set; }

    /// <summary>An <c>/Artifact</c> sequence and a tagged (MCID) sequence were nested within each other.</summary>
    public bool HasArtifactNesting { get; private set; }

    /// <summary>MCIDs whose marked-content sequence carries an <c>/ActualText</c> entry (a content-stream alt).</summary>
    public HashSet<int> ActualTextMcids { get; } = new();

    // ── real-content leaves — a violation only when nothing is open above them ────────────────────────

    private void OnRealContent()
    {
        if (_baseDepth + _stack.Count == 0)
            HasUntaggedContent = true;
    }

    private protected override void OnShowText(PdfString text) => OnRealContent();
    private protected override void OnShowTextWithPositioning(PdfArray array) => OnRealContent();
    protected override void OnFill(bool evenOdd) => OnRealContent();
    protected override void OnStroke() => OnRealContent();
    protected override void OnFillAndStroke() => OnRealContent();
    protected override void OnPaintShading(string name) => OnRealContent();
    private protected override void OnInlineImage(Operators.InlineImageOperator inlineImage) => OnRealContent();

    protected override void OnInvokeXObject(string name)
    {
        PdfStream? xobject = _resources?.GetXObject(name);
        if (xobject is null)
            return;

        if (PdfImage.IsImageXObject(xobject))
        {
            OnRealContent(); // an image is a real-content leaf, like a text show
            return;
        }

        if (!IsFormXObject(xobject))
            return;
        if (xobject.IsIndirect && !_visitedForms.Add(xobject.ObjectNumber))
            return; // a form already walked (or a recursion cycle) — its content contributes once

        // The form's content inherits this invocation's marked-content context, so tagging that encloses the
        // Do (or lives inside the form) is honoured without resetting depth at the form boundary.
        var nested = new MarkedContentCollector(
            ResolveFormResources(xobject), _document, _visitedForms,
            baseDepth: _baseDepth + _stack.Count,
            baseArtifact: _baseArtifact || _stack.Any(f => f.IsArtifact),
            baseTagged: _baseTagged || _stack.Any(f => f.IsTagged));

        try { nested.ProcessOperators(PdfContentParser.Parse(xobject.GetDecodedData(_document?.Decryptor))); }
        catch (Exception) { return; } // undecodable/unparseable form content — skip it, not the whole page

        HasUntaggedContent |= nested.HasUntaggedContent;
        HasArtifactNesting |= nested.HasArtifactNesting;
        ActualTextMcids.UnionWith(nested.ActualTextMcids);
    }

    // ── marked-content operators (BDC/BMC/EMC arrive as generic operators) ───────────────────────────

    private protected override void OnGenericOperator(Operators.GenericOperator op)
    {
        switch (op.Name)
        {
            case "BDC":
            case "BMC":
                Push(op);
                break;
            case "EMC":
                if (_stack.Count > 0)
                    _stack.RemoveAt(_stack.Count - 1);
                break;
            // MP / DP are marked-content POINTS, not sequences — they open no scope and are ignored.
        }
    }

    private void Push(Operators.GenericOperator op)
    {
        bool isArtifact = op.Operands.Count > 0 && op.Operands[0] is PdfName { Value: "Artifact" };
        PdfDictionary? props = ResolveProperties(op);

        int? mcid = props is null ? null : McidOf(props);
        bool isTagged = mcid is not null;
        if (mcid is int m && props!.Get("ActualText") is not null)
            ActualTextMcids.Add(m);

        // An artifact must not be nested in tagged content, nor tagged content in an artifact (checked before
        // the frame is pushed so it sees only its ancestors).
        bool insideArtifact = _baseArtifact || _stack.Any(f => f.IsArtifact);
        bool insideTagged = _baseTagged || _stack.Any(f => f.IsTagged);
        if ((isArtifact && insideTagged) || (isTagged && insideArtifact))
            HasArtifactNesting = true;

        _stack.Add(new Frame(isArtifact, isTagged));
    }

    // The property list of a BDC is either an inline dictionary operand or a name resolved through the
    // resource dictionary's /Properties sub-dictionary (ISO 32000-1, 14.6.1). BMC carries no property list.
    private PdfDictionary? ResolveProperties(Operators.GenericOperator op)
    {
        if (op.Operands.Count < 2)
            return null;

        switch (op.Operands[1])
        {
            case PdfDictionary inline:
                return inline;
            case PdfName name when _resources?.GetProperties() is { } properties
                                   && properties.TryGetValue(new PdfName(name.Value), out PdfObject? value):
                if (value is PdfIndirectReference reference && _document is not null)
                    value = _document.ResolveReference(reference);
                return value as PdfDictionary;
            default:
                return null;
        }
    }

    private int? McidOf(PdfDictionary props)
    {
        PdfObject? value = props.Get("MCID");
        if (value is PdfIndirectReference reference && _document is not null)
            value = _document.ResolveReference(reference);
        return value is PdfInteger i ? i.Value : null;
    }

    private PdfResources? ResolveFormResources(PdfStream form)
    {
        if (!form.Dictionary.TryGetValue(new PdfName("Resources"), out PdfObject? resObj))
            return _resources; // a form without its own /Resources inherits the invoking stream's
        if (resObj is PdfIndirectReference r && _document is not null)
            resObj = _document.ResolveReference(r);
        return resObj is PdfDictionary resDict ? new PdfResources(resDict, _document) : _resources;
    }

    private static bool IsFormXObject(PdfStream stream) =>
        stream.Dictionary.TryGetValue(new PdfName("Subtype"), out PdfObject obj) && obj is PdfName { Value: "Form" };
}
