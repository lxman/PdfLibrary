using System.Linq;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing;

/// <summary>One ExtGState dictionary <see cref="PdfDocumentEditor.PreviewGraphicsStateRepairs"/> found
/// repairable under ISO 19005-2/3 6.2.5, with the key names it would delete.
/// <see cref="ObjectNumber"/> is the ExtGState's own object number -- deliberately the same number
/// <c>ExtGStateRule</c> puts on its <c>Finding</c>, so a caller can key one to the other.
///
/// <para><see cref="Keys"/> names keys on TWO different dictionaries, and the distinction matters when
/// the words are shown to a person: <c>TR</c>, <c>HTP</c> and <c>TR2</c> live on the ExtGState itself,
/// while <c>HalftoneName</c> lives on a halftone the ExtGState reaches through <c>/HT</c> -- which is
/// usually a separate indirect object (in the corpus, ExtGState 32 reaching halftone 31), and for a
/// Type 5 composite may be one of its per-colourant component halftones. A key appears once per
/// dictionary it would be deleted from, so a Type 5 composite carrying <c>HalftoneName</c> on itself
/// and on two components contributes three entries.</para></summary>
public sealed record GraphicsStateRepairCandidate(int ObjectNumber, IReadOnlyList<string> Keys);

/// <summary>One 6.2.5 defect reached through the ExtGState numbered <see cref="ObjectNumber"/> that
/// this editor will NOT repair, with the user-facing sentence saying why. Like
/// <see cref="StreamFilterRefusal"/> this is a plain reason string rather than a refusal-kind enum:
/// the reasons are prose a caller surfaces verbatim, and an enum would be vocabulary every
/// exhaustiveness test then has to carry.
///
/// <para>An ExtGState can appear in BOTH a candidate and a refusal -- one carrying a deletable
/// <c>/TR</c> and a halftone whose <c>HalftoneType</c> cannot be repaired produces one of each. That is
/// not a contradiction: 6.2.5 is several independent requirements and <c>ExtGStateRule</c> raises a
/// separate message for each, so a repair closing one of them while another stays open is the honest
/// answer rather than a disagreement.</para></summary>
public sealed record GraphicsStateRefusal(int ObjectNumber, string Reason);

/// <summary>Read-only classification of every <c>/Type /ExtGState</c> object in the document against
/// ISO 19005-2/3 6.2.5. Nothing has been written.</summary>
public sealed record GraphicsStateRepairPreview(
    IReadOnlyList<GraphicsStateRepairCandidate> Candidates,
    IReadOnlyList<GraphicsStateRefusal> Refused);

/// <summary>One ExtGState <see cref="PdfDocumentEditor.RepairGraphicsState"/> actually edited, with the
/// keys it deleted. <see cref="DeletedKeys"/> carries the same meaning
/// <see cref="GraphicsStateRepairCandidate.Keys"/> does, in the past tense.</summary>
public sealed record GraphicsStateRepair(int ObjectNumber, IReadOnlyList<string> DeletedKeys);

/// <summary>What <see cref="PdfDocumentEditor.RepairGraphicsState"/> did and declined to do, restricted
/// to the set it was given.</summary>
public sealed record GraphicsStateRepairReport(
    IReadOnlyList<GraphicsStateRepair> Applied,
    IReadOnlyList<GraphicsStateRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    /// <summary>One key deletion the shared classifier has decided on: the dictionary to remove it
    /// from, the key, and the label reported to a caller. This is the classifier's OUTPUT, not a hint
    /// -- <see cref="RepairGraphicsState"/> applies exactly these pairs and never re-derives them, so
    /// the preview does not merely share a predicate with the repair, it shares the plan. The pattern
    /// <c>ClassifyAnnotationTypes</c> arrived at by correction (engine b85d661) after a write side that
    /// re-decided things for itself produced refusals no preview could predict.</summary>
    private readonly record struct GraphicsStateDeletion(PdfDictionary Owner, PdfName Key, string Label);

    private static readonly PdfName TransferKey = new("TR");
    private static readonly PdfName HalftonePhaseKey = new("HTP");
    private static readonly PdfName Transfer2Key = new("TR2");
    private static readonly PdfName HalftoneNameKey = new("HalftoneName");

    /// <summary>Every ExtGState dictionary in the document, in the SAME walk <c>ExtGStateRule.Check</c>
    /// uses: materialise, then scan the indirect object table for dictionaries whose (resolved)
    /// <c>/Type</c> is <c>ExtGState</c>. Two properties of that walk are load-bearing here, and both
    /// were verified against the corpus rather than assumed.
    ///
    /// <para>First, the <c>/Type</c> test is not an accident of the rule's implementation to be
    /// "improved" on. <c>Faces.pdf</c> object 49 is a live ExtGState -- resource <c>/R1</c>, invoked by
    /// <c>gs</c> twice -- that omits <c>/Type</c> entirely, so an object scan and a page-resource walk
    /// genuinely disagree about it. The rule never reports it, therefore this editor never touches it:
    /// a repair that found objects the detector cannot see would be editing a document over a defect
    /// nothing told the user about. (Object 49's <c>/HT</c> is <c>/Default</c>, so it is not in
    /// violation anyway -- but the guard is the point, not the corpus.)</para>
    ///
    /// <para>Second, <c>OfType&lt;PdfDictionary&gt;</c> excludes a <see cref="PdfStream"/> whose
    /// dictionary says <c>/Type /ExtGState</c>, because <see cref="PdfStream"/> is not a
    /// <see cref="PdfDictionary"/>. That is the rule's behaviour too, and it is mirrored rather than
    /// widened for the same reason.</para>
    ///
    /// <para>The list is built EAGERLY rather than yielded, because both callers resolve indirect
    /// references while walking it and a resolution can add to the object table -- iterating
    /// <c>_document.Objects.Values</c> lazily across that would be a "collection was modified" waiting
    /// for the first document that triggers it.</para></summary>
    private List<PdfDictionary> CollectExtGStates()
    {
        _document.MaterializeAllObjects();
        return
        [
            .. _document.Objects.Values.OfType<PdfDictionary>()
                .Where(d => ResolveObject(d.Get("Type")) is PdfName { Value: "ExtGState" }),
        ];
    }

    /// <summary>The halftone dictionary at <paramref name="htObj"/> -- a dictionary, or the dictionary
    /// of a halftone stream (a Type 10/16 halftone is a stream, and ISO 32000-1 Table 130's keys live
    /// on its dictionary either way). Mirrors <c>ExtGStateRule.HalftoneViolations</c>'s own switch, and
    /// returns the RESOLVED object alongside it so the caller can put that exact reference into the
    /// cycle-guard set the rule guards with.</summary>
    private (PdfObject Resolved, PdfDictionary Dictionary)? ResolveHalftone(PdfObject? htObj)
    {
        PdfObject? resolved = ResolveObject(htObj);
        PdfDictionary? ht = resolved switch
        {
            PdfDictionary d => d,
            PdfStream s => s.Dictionary,
            _ => null,
        };
        return ht is null ? null : (resolved!, ht);
    }

    /// <summary>Classifies the halftone at <paramref name="htObj"/> -- and, for a Type 5 composite,
    /// each of its per-colourant component halftones -- appending planned deletions to
    /// <paramref name="deletions"/> and refusals to <paramref name="refusals"/>. A step-for-step mirror
    /// of <c>ExtGStateRule.HalftoneViolations</c>: same resolution switch, same
    /// <paramref name="visited"/> cycle guard on the resolved object, same
    /// <paramref name="colorantName"/> convention (<see langword="null"/> for the standalone halftone
    /// named straight from <c>/HT</c>), same <c>Type</c>/<c>HalftoneType</c>/<c>HalftoneName</c> keys
    /// skipped when recursing, and the same primary-colourant split read from
    /// <see cref="ExtGStateRule.PrimaryColourants"/> itself rather than from a second copy of the list.
    ///
    /// <para><b>Three of the rule's messages become refusals here, not repairs.</b> A
    /// <c>HalftoneType</c> outside {1, 5} cannot be corrected by deleting anything: the type IS the
    /// screening, there is nothing in the document to infer a replacement from, and rewriting it to 1
    /// would invent a screen the author never specified. A <c>TransferFunction</c> present where Table
    /// 130 forbids one could be deleted, but deleting a transfer function changes tone reproduction on
    /// any device that honours it -- the opposite of the ExtGState <c>/TR</c> case below, where the key
    /// is an OVERRIDE whose removal restores the device's own curve. And a <c>TransferFunction</c>
    /// MISSING where Table 130 requires one cannot be repaired at all: synthesising a curve is
    /// invention, not remediation. None of the three has a witness anywhere in the 708-document corpus
    /// -- every halftone there is Type 1 with no <c>TransferFunction</c> -- so they are proven
    /// synthetically and are here to satisfy the closure contract, which says every violation the rule
    /// can raise must land in a candidate or a refusal rather than silently in neither.</para></summary>
    private void ClassifyHalftone(
        int objectNumber, PdfObject? htObj, string? colorantName, HashSet<PdfObject> visited,
        List<GraphicsStateDeletion> deletions, List<GraphicsStateRefusal> refusals)
    {
        if (ResolveHalftone(htObj) is not { } halftone || !visited.Add(halftone.Resolved))
            return;

        PdfDictionary ht = halftone.Dictionary;
        int? type = (ResolveObject(ht.Get("HalftoneType")) as PdfInteger)?.Value;
        if (type is not null and not 1 and not 5)
            refusals.Add(new GraphicsStateRefusal(objectNumber,
                $"A halftone used by this graphics state has HalftoneType {type}, but PDF/A permits "
                + "only 1 or 5. The halftone type IS the screening the author chose, and there is "
                + "nothing in the document to derive a permitted replacement from, so Pellucid leaves "
                + "it alone and the finding stays open."));

        if (ht.Get(HalftoneNameKey) is not null)
            deletions.Add(new GraphicsStateDeletion(ht, HalftoneNameKey, "HalftoneName"));

        // TransferFunction (ISO 32000-1 Table 130): required for a non-primary colourant, forbidden for
        // a primary CMYK colourant or a standalone (non-component) halftone; the Default component is
        // exempt. Both directions refuse -- see this method's doc comment.
        if (colorantName != "Default")
        {
            bool primaryOrStandalone =
                colorantName is null || ExtGStateRule.PrimaryColourants.Contains(colorantName);
            bool hasTransfer = ht.Get("TransferFunction") is not null;
            if (primaryOrStandalone && hasTransfer)
                refusals.Add(new GraphicsStateRefusal(objectNumber, colorantName is null
                    ? "A halftone used by this graphics state carries a TransferFunction, which PDF/A "
                      + "does not permit. Deleting a transfer function changes how tones are "
                      + "reproduced, so Pellucid leaves it alone and the finding stays open."
                    : $"The '{colorantName}' component of a Type 5 halftone used by this graphics "
                      + "state carries a TransferFunction, which PDF/A does not permit for a primary "
                      + "(CMYK) colourant. Deleting a transfer function changes how tones are "
                      + "reproduced, so Pellucid leaves it alone and the finding stays open."));
            else if (!primaryOrStandalone && !hasTransfer)
                refusals.Add(new GraphicsStateRefusal(objectNumber,
                    $"The '{colorantName}' component of a Type 5 halftone used by this graphics state "
                    + "is missing the TransferFunction PDF/A requires for a non-primary colourant. "
                    + "Pellucid cannot invent a transfer curve, so it leaves the halftone alone and "
                    + "the finding stays open."));
        }

        if (type != 5)
            return;

        foreach (PdfName key in ht.Keys.ToList())
        {
            // Structural keys aside, every entry of a Type 5 halftone is a colourant component (or
            // Default). Same three exclusions the rule makes, for the same reason.
            if (key.Value is "Type" or "HalftoneType" or "HalftoneName")
                continue;
            ClassifyHalftone(objectNumber, ht.Get(key), key.Value, visited, deletions, refusals);
        }
    }

    /// <summary>The ONE classifier <see cref="PreviewGraphicsStateRepairs"/> and
    /// <see cref="RepairGraphicsState"/> share, so the preview and the repair can never disagree about
    /// what would happen to a given ExtGState. It does not merely share a predicate with the write: it
    /// produces the write's actual plan (<see cref="GraphicsStateDeletion"/> pairs the repair applies
    /// verbatim), which is the strongest form of the invariant <c>ClassifyAnnotationTypes</c> had to be
    /// retrofitted with after a write side that decided things for itself produced apply-time refusals
    /// the preview never predicted and no surface ever showed.
    ///
    /// <para>Every branch is one row of the classification table in
    /// <c>docs/superpowers/specs/2026-08-24-graphics-state-and-optional-content-design.md</c> §4, and
    /// between them they cover every message <c>ExtGStateRule.Check</c> can raise: <c>/TR</c>,
    /// <c>/HTP</c> and a non-<c>Default</c> <c>/TR2</c> here, and the four halftone messages in
    /// <see cref="ClassifyHalftone"/>. A violation producing neither a deletion nor a refusal is the
    /// closure defect this family had to correct <c>image-dictionary</c> out of -- it reads as "nothing
    /// wrong" to a caller looking only at those two lists.</para>
    ///
    /// <para><b>The <c>/TR</c> deletion is scoped to this dictionary and nothing below it.</b> A
    /// soft-mask dictionary reached through <c>/SMask</c> has its own <c>/TR</c> (ISO 32000-1 Table
    /// 144) which is LEGAL under PDF/A and which our renderer implements -- <c>ExtGStateApplier</c>
    /// stores it on the SoftMask and applies it. A recursive "strip every /TR" would silently change
    /// transparency output. Nothing here descends into <c>/SMask</c>; the only dictionary other than
    /// the ExtGState itself that this classifier ever writes to is a halftone reached through
    /// <c>/HT</c>, and the only key it deletes there is <c>HalftoneName</c>.</para>
    ///
    /// <para><b>What deleting each key costs.</b> <c>/TR</c> is always an OVERRIDE: ISO 32000-1 Table
    /// 53 gives the transfer parameter's initial value as a device-dependent one, so an explicit
    /// transfer function -- even the identity -- suppresses the device's own curve, and deleting it
    /// restores that curve, which is what 6.2.5 is for. <c>/HTP</c> is a halftone-phase key from PDF
    /// 1.3 that no current reader honours. A <c>/TR2</c> other than <c>Default</c> is the same override
    /// story as <c>/TR</c>, and deleting it restores the <c>Default</c> the clause permits.
    /// <c>HalftoneName</c> is a byte-string LABEL (Table 130) whose only consumer is PostScript's
    /// <c>findcolorrendering</c>; the screen itself is <c>Frequency</c>/<c>Angle</c>/
    /// <c>SpotFunction</c>, none of which is touched.</para></summary>
    private void ClassifyGraphicsState(
        PdfDictionary gs, List<GraphicsStateDeletion> deletions, List<GraphicsStateRefusal> refusals)
    {
        int objectNumber = gs.ObjectNumber;

        if (gs.Get(TransferKey) is not null)
            deletions.Add(new GraphicsStateDeletion(gs, TransferKey, "TR"));

        if (gs.Get(HalftonePhaseKey) is not null)
            deletions.Add(new GraphicsStateDeletion(gs, HalftonePhaseKey, "HTP"));

        // Only a /TR2 that is NOT the name Default is a violation -- 6.2.5 permits Default explicitly,
        // and 18 documents in the corpus carry exactly that. Same test the rule makes, so a conforming
        // /TR2 Default is left in place rather than "tidied".
        if (gs.Get(Transfer2Key) is not null
            && (ResolveObject(gs.Get(Transfer2Key)) as PdfName)?.Value != "Default")
            deletions.Add(new GraphicsStateDeletion(gs, Transfer2Key, "TR2"));

        ClassifyHalftone(objectNumber, gs.Get("HT"), colorantName: null, new HashSet<PdfObject>(),
            deletions, refusals);
    }

    /// <summary>Read-only preview of every ISO 19005-2/3 6.2.5 extended-graphics-state defect this
    /// editor would repair right now, without writing anything. Calling it twice returns the same
    /// answer; there is no idempotency guard to trip because nothing here is ever written. This is what
    /// a Pellucid domain's <c>Propose</c> calls -- <c>Propose</c> must never call a mutating write
    /// counterpart to learn its answer, which a sibling domain once did and had graded Critical.
    ///
    /// <para><b>A caller staging by <c>Finding.ObjectNumber</c> alone will under-repair.</b>
    /// <c>ExtGStateRule</c> deduplicates by MESSAGE per document, first object wins, so one finding can
    /// stand for several offending ExtGStates: <c>allmand-backhoe-loaders-spec-e15132.pdf</c> carries
    /// <c>/TR</c> on objects 19 AND 20 but raises a single finding naming 19, and <c>Faces.pdf</c>
    /// carries <c>HalftoneName</c> under ExtGStates 32 and 45 but raises a single finding naming 32.
    /// The object number on such a finding is an EXAMPLE, not an address. That is why
    /// <see cref="RepairGraphicsState"/> takes a nullable set and why a caller for this rule should
    /// normally pass <see langword="null"/> (whole document) rather than the finding's object
    /// number.</para></summary>
    public GraphicsStateRepairPreview PreviewGraphicsStateRepairs()
    {
        var candidates = new List<GraphicsStateRepairCandidate>();
        var refusals = new List<GraphicsStateRefusal>();

        foreach (PdfDictionary gs in CollectExtGStates())
        {
            var deletions = new List<GraphicsStateDeletion>();
            ClassifyGraphicsState(gs, deletions, refusals);
            if (deletions.Count > 0)
                candidates.Add(new GraphicsStateRepairCandidate(
                    gs.ObjectNumber, [.. deletions.Select(d => d.Label)]));
        }

        return new GraphicsStateRepairPreview(candidates, refusals);
    }

    /// <summary>Deletes every key <see cref="PreviewGraphicsStateRepairs"/> reports as a candidate,
    /// restricted to the ExtGState object numbers in <paramref name="objectNumbers"/> -- or every
    /// offending ExtGState in the document when it is <see langword="null"/>. Shares
    /// <see cref="CollectExtGStates"/> and <see cref="ClassifyGraphicsState"/> with the preview, and
    /// applies the classifier's own <see cref="GraphicsStateDeletion"/> plan rather than re-deriving
    /// one, so the write and the preview cannot disagree.
    ///
    /// <para><b><see langword="null"/> is the expected argument for this rule</b>, unlike
    /// <see cref="RepairStreamFilters"/> where it is the whole-document batch escape hatch.
    /// <c>ExtGStateRule</c> raises one finding per distinct MESSAGE per document rather than one per
    /// object, so its <c>Finding.ObjectNumber</c> names the first offender and not the only one -- see
    /// <see cref="PreviewGraphicsStateRepairs"/> for the two corpus documents where staging by that
    /// number would leave a second ExtGState unrepaired and the finding still open after the save. The
    /// explicit set is kept for a caller that genuinely has one, and because a repair that could only
    /// ever run document-wide would be a worse building block than one that can do either.</para>
    ///
    /// <para>Nothing is written for a refused defect: a refusal never produces a
    /// <see cref="GraphicsStateDeletion"/>, so an ExtGState whose only 6.2.5 problem is an unrepairable
    /// halftone is reported and left byte-for-byte alone. An ExtGState carrying both a deletable key
    /// and an unrepairable halftone has the key deleted and the refusal reported -- 6.2.5 is several
    /// independent requirements, and closing one of them is not a claim about the others.</para></summary>
    public GraphicsStateRepairReport RepairGraphicsState(IReadOnlySet<int>? objectNumbers = null)
    {
        var applied = new List<GraphicsStateRepair>();
        var refusals = new List<GraphicsStateRefusal>();

        foreach (PdfDictionary gs in CollectExtGStates())
        {
            if (objectNumbers is not null && !objectNumbers.Contains(gs.ObjectNumber)) continue;

            var deletions = new List<GraphicsStateDeletion>();
            ClassifyGraphicsState(gs, deletions, refusals);
            if (deletions.Count == 0) continue;

            foreach (GraphicsStateDeletion deletion in deletions)
                deletion.Owner.Remove(deletion.Key);

            applied.Add(new GraphicsStateRepair(gs.ObjectNumber, [.. deletions.Select(d => d.Label)]));
        }

        return new GraphicsStateRepairReport(applied, refusals);
    }
}
