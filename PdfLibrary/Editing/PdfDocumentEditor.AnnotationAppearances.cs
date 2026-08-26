using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Editing;

/// <summary>What kind of PDF/A clause 6.3.3 annotation-appearance repair a candidate or refusal
/// describes. R1 strips the <c>/AP</c> keys 6.3.3-t2 rejects from a widget whose appearance
/// dictionary already validly contains <c>/N</c>. R2 writes a blank normal appearance for a
/// value-less <c>/Tx</c>/<c>/Ch</c> widget. Both are classified by the same
/// <see cref="PdfDocumentEditor.ClassifyAnnotationAppearance"/> and applied by the same
/// <see cref="PdfDocumentEditor.RepairAnnotationAppearances"/>.</summary>
public enum AnnotationAppearanceRepairKind
{
    /// <summary>R1: delete the <c>/AP</c> keys ISO 19005-2 6.3.3-t2 rejects (<c>/D</c>, <c>/R</c>)
    /// from a widget's appearance dictionary that already validly contains <c>/N</c>. Never a
    /// wildcard "everything that is not /N" -- see
    /// <see cref="PdfDocumentEditor.ClassifyAnnotationAppearance"/>.</summary>
    StripRejectedKeys,

    /// <summary>R2: write a blank single-key <c>/AP /N</c> appearance for a <c>/Tx</c> or <c>/Ch</c>
    /// widget with no resolvable <c>/AP</c> at all, when its effective <c>/V</c> (own, or inherited
    /// from <c>/Parent</c>) is empty -- see
    /// <see cref="PdfDocumentEditor.ClassifyAnnotationAppearance"/> and
    /// <see cref="PdfDocumentEditor.WriteBlankAppearance"/>.</summary>
    WriteBlankAppearance,
}

/// <summary>One widget <see cref="PdfDocumentEditor.PreviewAnnotationAppearanceRepairs"/> found
/// repairable, and every repair kind that would apply to it.</summary>
public sealed record AnnotationAppearanceRepairCandidate(
    int ObjectNumber, IReadOnlyList<AnnotationAppearanceRepairKind> WouldApply);

/// <summary>One widget <see cref="PdfDocumentEditor.PreviewAnnotationAppearanceRepairs"/> found a
/// 6.3.3 defect on but declined to repair, with the reason a caller can surface verbatim.</summary>
public sealed record AnnotationAppearanceRefusal(
    int ObjectNumber, AnnotationAppearanceRepairKind Kind, string Reason);

/// <summary>What <see cref="PdfDocumentEditor.PreviewAnnotationAppearanceRepairs"/> found, read-only:
/// nothing has been written to the document.</summary>
public sealed record AnnotationAppearanceRepairPreview(
    IReadOnlyList<AnnotationAppearanceRepairCandidate> Candidates,
    IReadOnlyList<AnnotationAppearanceRefusal> Refused);

/// <summary>One widget <see cref="PdfDocumentEditor.RepairAnnotationAppearances"/> wrote to, and
/// every repair kind it actually applied -- past tense, unlike
/// <see cref="AnnotationAppearanceRepairCandidate.WouldApply"/>.
///
/// <para><b>Invariant a consuming domain may rely on:</b> an object appearing here is FULLY
/// 6.3.3-conformant when <see cref="PdfDocumentEditor.RepairAnnotationAppearances"/> returns --
/// never partially fixed. <see cref="PdfDocumentEditor.ClassifyAnnotationAppearance"/> only ever
/// adds <see cref="AnnotationAppearanceRepairKind.StripRejectedKeys"/> to a widget's repair list
/// when every key besides <c>/N</c> is <c>/D</c> and/or <c>/R</c> -- so stripping them always
/// leaves exactly <c>{/N}</c> behind. A widget with any OTHER stray key is a
/// <see cref="AnnotationAppearanceRefusal"/> instead, never a partial entry here (task 1 review
/// finding, fix round 1: the original classifier let a mixed <c>{/N, /D, /Zzz}</c> case through as
/// "repaired" while <c>/Zzz</c> survived and the object was still 6.3.3-violating).</para></summary>
public sealed record AnnotationAppearanceRepair(
    int ObjectNumber, IReadOnlyList<AnnotationAppearanceRepairKind> Applied);

/// <summary>What <see cref="PdfDocumentEditor.RepairAnnotationAppearances"/> did and declined to do --
/// the ONE report shape for this whole remediation program (R1 and R2 together), so a consuming
/// domain has exactly one report to map into save-refusal entries rather than two.
///
/// <para>A consuming domain may treat membership in <see cref="Repaired"/> as proof the object is
/// now fully 6.3.3-conformant -- see the invariant documented on <see cref="AnnotationAppearanceRepair"/>.
/// An object that is only partially fixable is always reported in <see cref="Refused"/>, never split
/// across both lists.</para></summary>
public sealed record AnnotationAppearanceRepairReport(
    IReadOnlyList<AnnotationAppearanceRepair> Repaired,
    IReadOnlyList<AnnotationAppearanceRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    private static readonly PdfName ApNormalKey = new("N");
    private static readonly PdfName ApDownKey = new("D");
    private static readonly PdfName ApRolloverKey = new("R");

    /// <summary>The ONE classifier <see cref="PreviewAnnotationAppearanceRepairs"/> uses, and
    /// <see cref="RepairAnnotationAppearances"/> shares, so preview and repair can never disagree
    /// about what would happen to a given widget -- the same factoring
    /// <c>ClassifyImageDictionary</c>/<c>ClassifyAnnotationTypes</c> use for their own domains. Both
    /// R1 (strip rejected <c>/AP</c> keys, below) and R2 (<see cref="ClassifyBlankAppearance"/>, a
    /// value-less <c>/Tx</c>/<c>/Ch</c> widget with no <c>/AP</c> at all) are classified here.
    ///
    /// <para>Scope is deliberately narrower than the rule itself: ISO 19005-2 6.3.3-t2
    /// (<c>AnnotationAppearanceRule.cs:48-55</c>) applies to any annotation with an <c>/AP</c>, but
    /// the measured population behind R1 (design doc
    /// 2026-08-26-annotation-appearance-remediation-design.md §2) is <b>entirely</b> <c>/Widget</c> --
    /// 108 findings, all <c>/Btn</c>. Staying within that measured shape rather than reasoning about
    /// every annotation subtype is deliberate, not an oversight; a non-widget annotation with the same
    /// <c>{/D, /N}</c> shape is left alone.</para>
    ///
    /// <para>Reuses <see cref="EnumerateIndirectAnnotations"/> (the same page-walk
    /// <c>ConformanceContext.CollectAnnotations</c> uses) rather than a fresh walk, so a Finding the
    /// rule raised always has a candidate or refusal here, keyed by the same object number.</para></summary>
    private void ClassifyAnnotationAppearance(
        PdfDictionary annot, List<AnnotationAppearanceRepairKind> repairs, List<AnnotationAppearanceRefusal> refusals)
    {
        if (ResolveObject(annot.Get("Subtype")) is not PdfName { Value: "Widget" })
            return; // out of the measured scope this repair targets -- see the method doc above

        if (ResolveObject(annot.Get("AP")) is not PdfDictionary appearance)
        {
            // No /AP at all is 6.3.3-t1's business, not t2's -- and for a value-less /Tx or /Ch
            // widget it is exactly R2's target (design doc §2: 108 findings, all blank /Tx). Any
            // other shape reaching here (a /Btn missing /AP, an unknown /FT, a value-bearing
            // /Tx/Ch) is left alone or refused by ClassifyBlankAppearance -- see its doc comment.
            ClassifyBlankAppearance(annot, repairs, refusals);
            return;
        }

        if (appearance.Count == 1 && appearance.ContainsKey(ApNormalKey))
            return; // already conforms to 6.3.3-t2 -- no gratuitous rewrite

        if (!appearance.ContainsKey(ApNormalKey))
        {
            // No /N to keep: deleting the other keys would not "fix" this appearance, it would
            // remove the only entry that could ever have been kept. Refuse rather than guess.
            refusals.Add(new AnnotationAppearanceRefusal(
                annot.ObjectNumber, AnnotationAppearanceRepairKind.StripRejectedKeys,
                "This widget's /AP has no /N (normal) appearance entry, so Pellucid will not delete "
                + "the other keys to force the dictionary into shape -- there is no normal appearance "
                + "to fall back to, and doing so would leave the annotation with no appearance at all."));
            return;
        }

        // /N is present and the dictionary carries at least one other key -- 6.3.3-t2's actual
        // violation. Only /D and /R are ever deleted: ISO 32000-1 Table 168 names /N, /R, and /D as
        // the ENTIRE key vocabulary an appearance dictionary can carry, so those two ARE "the keys
        // the rule rejects" here -- never a wildcard "everything that is not /N".
        //
        // The repair is offered ONLY when EVERY other key is /D or /R -- not merely when /D or /R is
        // PRESENT. Fix round 1 (task 1 review): the original check was `hasDown || hasRollover`,
        // which let a mixed case like {/N, /D, /Zzz} through as a candidate; the write side then
        // stripped only /D and /R (the one key it knows how to delete), leaving a STILL-VIOLATING
        // {/N, /Zzz} reported as Repaired -- a false "fixed" claim RepairAnnotationAppearances's own
        // invariant forbids (see that method's doc comment). A key this repair does not recognize
        // now refuses the WHOLE dictionary, leaving it byte-for-byte untouched and the finding open,
        // rather than a partial strip (design doc §8, "Over-broad R1" -- degrade safely on
        // unmeasured input).
        bool hasDown = appearance.ContainsKey(ApDownKey);
        bool hasRollover = appearance.ContainsKey(ApRolloverKey);
        int recognizedKeyCount = 1 + (hasDown ? 1 : 0) + (hasRollover ? 1 : 0); // /N + /D? + /R?

        if (recognizedKeyCount == appearance.Count)
        {
            repairs.Add(AnnotationAppearanceRepairKind.StripRejectedKeys);
            return;
        }

        List<string> unrecognizedKeys = appearance.Keys
            .Where(k => !k.Equals(ApNormalKey) && !k.Equals(ApDownKey) && !k.Equals(ApRolloverKey))
            .Select(k => "/" + k.Value)
            .ToList();

        refusals.Add(new AnnotationAppearanceRefusal(
            annot.ObjectNumber, AnnotationAppearanceRepairKind.StripRejectedKeys,
            "This widget's /AP fails PDF/A clause 6.3.3 (it carries a key other than /N), but it "
            + "also carries " + string.Join(", ", unrecognizedKeys) + ", which Pellucid does not "
            + "recognize as safe to delete (only /D and /R are), so it leaves the whole dictionary "
            + "alone rather than strip part of it and report a still-violating /AP as repaired."));
    }

    /// <summary>R2: classifies a Widget annotation with no resolvable <c>/AP</c> at all -- 6.3.3-t1's
    /// blank-appearance case (<c>AnnotationAppearanceRule.cs:37-46</c>) -- for
    /// <see cref="AnnotationAppearanceRepairKind.WriteBlankAppearance"/>. Scope is deliberately
    /// narrower than the rule: only <c>/Tx</c> and <c>/Ch</c> widgets are ever offered this repair
    /// (design doc §3 "In" -- the measured population is entirely blank <c>/Tx</c>; <c>/Ch</c> is
    /// covered for completeness even though it has zero corpus findings). Any other effective
    /// <c>/FT</c> (a <c>/Btn</c> missing <c>/AP</c>, an unknown or absent field type) is left alone
    /// with no candidate or refusal, the same way a non-<c>/Widget</c> annotation is left alone in
    /// <see cref="ClassifyAnnotationAppearance"/> above.
    ///
    /// <para>The effective <c>/FT</c> and <c>/V</c> are resolved by walking <c>/Parent</c> in
    /// <see cref="ResolveEffectiveField"/>, exactly as
    /// <c>PdfLibrary.Conformance.Rules.AnnotationAppearanceRule.EffectiveFieldType</c> (<c>:96-108</c>)
    /// resolves <c>/FT</c> alone -- own value wins, else the nearest ancestor's. This is the sharpest
    /// correctness risk in the whole program (design doc §8): reading only the widget's own <c>/V</c>
    /// would misclassify a filled child -- one whose value lives on its <c>/Parent</c> field -- as
    /// blank and overwrite a real value with an empty box. See
    /// <c>Repair_treats_a_widget_whose_V_is_inherited_from_Parent_as_value_bearing_not_blank</c>
    /// (<c>AnnotationAppearanceRepairTests.cs</c>) for the dedicated regression test.</para></summary>
    private void ClassifyBlankAppearance(
        PdfDictionary annot, List<AnnotationAppearanceRepairKind> repairs, List<AnnotationAppearanceRefusal> refusals)
    {
        (string? ft, int _, PdfObject? effectiveValue) = ResolveEffectiveField(annot);

        if (ft is not "Tx" and not "Ch")
            return; // out of R2's measured scope -- see the method doc above

        if (!IsEffectivelyBlank(effectiveValue))
        {
            // The deferred value-bearing case (design doc §3 "Out"): 21 findings in one document
            // that is not sole-cause, so closing it moves the board by zero. Refused, per the
            // Global Constraints, never silently skipped -- a caller must be told this widget still
            // needs a human or a future value-aware appearance generator.
            refusals.Add(new AnnotationAppearanceRefusal(
                annot.ObjectNumber, AnnotationAppearanceRepairKind.WriteBlankAppearance,
                $"This /{ft} widget has no /AP, which PDF/A clause 6.3.3 requires, but its effective "
                + "/V (its own, or inherited from /Parent) is not empty. Writing a blank appearance "
                + "would visibly erase a real value, so Pellucid leaves it for a value-aware "
                + "appearance generator rather than guess at how to render it."));
            return;
        }

        repairs.Add(AnnotationAppearanceRepairKind.WriteBlankAppearance);
    }

    /// <summary>Resolves the effective <c>/FT</c>, <c>/Ff</c>, and <c>/V</c> for <paramref name="annot"/>
    /// in one pass up its <c>/Parent</c> chain: own value wins at every node, falling back to the
    /// nearest ancestor's only when the node itself lacks the key -- ISO 32000-1 7.7.3.4's
    /// field-inheritance rule, the same precedence
    /// <c>PdfLibrary.Conformance.Rules.AnnotationAppearanceRule.EffectiveFieldType</c> applies to
    /// <c>/FT</c> alone and <c>FormFieldTree.MergeInherited</c> applies to all three. Cycle-guarded
    /// on <c>/Parent</c> the same way the rule's own walk is. <c>/V</c> is resolved by key PRESENCE
    /// (<see cref="PdfDictionary.TryGetValue"/>), not by whether the value is non-null -- a node that
    /// carries <c>/V</c> pointing at <see cref="PdfNull"/> still "has" the key and stops the walk
    /// there, exactly as <c>FormFieldTree.MergeInherited</c> treats it.</summary>
    private (string? Ft, int Ff, PdfObject? V) ResolveEffectiveField(PdfDictionary annot)
    {
        string? ft = null;
        int? ff = null;
        PdfObject? v = null;
        var vFound = false;

        var seen = new HashSet<int>();
        for (PdfDictionary? node = annot; node is not null;)
        {
            if (ft is null && ResolveObject(node.Get("FT")) is PdfName ftName)
                ft = ftName.Value;
            if (ff is null && ResolveObject(node.Get("Ff")) is PdfInteger ffInt)
                ff = (int)ffInt.Value;
            if (!vFound && node.TryGetValue(new PdfName("V"), out PdfObject vRaw))
            {
                v = ResolveObject(vRaw);
                vFound = true;
            }

            if (ft is not null && ff is not null && vFound)
                break;

            if (node.IsIndirect && !seen.Add(node.ObjectNumber))
                break;
            node = ResolveObject(node.Get("Parent")) as PdfDictionary;
        }

        return (ft, ff ?? 0, v);
    }

    /// <summary>True when a resolved <c>/V</c> carries no real value: absent, <see cref="PdfNull"/>,
    /// an empty <see cref="PdfString"/>, or an empty <see cref="PdfArray"/> (an unselected
    /// multi-select choice field). Anything else -- a non-empty string, a populated array, a name --
    /// is a value the widget must not be silently overwritten with a blank appearance.</summary>
    private static bool IsEffectivelyBlank(PdfObject? effectiveValue) => effectiveValue switch
    {
        null => true,
        PdfNull => true,
        PdfString s => s.GetText().Length == 0,
        PdfArray a => a.Count == 0,
        _ => false,
    };

    /// <summary>Read-only preview of every PDF/A 6.3.3 annotation-appearance defect this editor
    /// would repair right now, without writing anything. Calling it twice returns the same answer;
    /// there is no idempotency guard to trip because nothing here is ever written.</summary>
    public AnnotationAppearanceRepairPreview PreviewAnnotationAppearanceRepairs()
    {
        var candidates = new List<AnnotationAppearanceRepairCandidate>();
        var refusals = new List<AnnotationAppearanceRefusal>();

        foreach ((PdfDictionary annot, int _) in EnumerateIndirectAnnotations())
        {
            var repairs = new List<AnnotationAppearanceRepairKind>();
            ClassifyAnnotationAppearance(annot, repairs, refusals);
            if (repairs.Count > 0)
                candidates.Add(new AnnotationAppearanceRepairCandidate(annot.ObjectNumber, repairs));
        }

        return new AnnotationAppearanceRepairPreview(candidates, refusals);
    }

    /// <summary>Applies the PDF/A 6.3.3 annotation-appearance repairs
    /// <see cref="PreviewAnnotationAppearanceRepairs"/> would report, to the widgets named by
    /// <paramref name="objectNumbers"/> -- or to every offending widget in the document when it is
    /// null (the batch/CLI case, mirroring <c>RepairImageDictionaries</c>). Shares
    /// <see cref="EnumerateIndirectAnnotations"/> and <see cref="ClassifyAnnotationAppearance"/> with
    /// <see cref="PreviewAnnotationAppearanceRepairs"/>, so the write and the preview can never
    /// disagree about what would happen to a given widget.
    ///
    /// <para>R1 is a dictionary-key removal; R2 (<see cref="WriteBlankAppearance"/>) registers a new
    /// appearance-stream object but never touches a page or another annotation's own dictionary. Both
    /// stop short of the object-graph rewrite <c>RepairAnnotationTypes</c>'s page-mutating flatten
    /// does, so an optional filter (like <c>RepairImageDictionaries</c>'s) is the right risk level
    /// here, not that method's mandatory staged set.</para>
    ///
    /// <para><b>Invariant:</b> every object this method places in the returned report's
    /// <see cref="AnnotationAppearanceRepairReport.Repaired"/> is FULLY 6.3.3-conformant once this
    /// call returns -- never partially fixed. See <see cref="AnnotationAppearanceRepair"/>'s own doc
    /// comment for why that always holds (it is enforced in <see cref="ClassifyAnnotationAppearance"/>,
    /// not here -- this method only ever executes a repair that classifier already vetted).</para></summary>
    public AnnotationAppearanceRepairReport RepairAnnotationAppearances(IReadOnlySet<int>? objectNumbers = null)
    {
        var repaired = new List<AnnotationAppearanceRepair>();
        var refusals = new List<AnnotationAppearanceRefusal>();

        foreach ((PdfDictionary annot, int _) in EnumerateIndirectAnnotations())
        {
            if (objectNumbers is not null && !objectNumbers.Contains(annot.ObjectNumber)) continue;

            var repairs = new List<AnnotationAppearanceRepairKind>();
            ClassifyAnnotationAppearance(annot, repairs, refusals);
            if (repairs.Count == 0) continue;

            foreach (AnnotationAppearanceRepairKind kind in repairs)
                switch (kind)
                {
                    case AnnotationAppearanceRepairKind.StripRejectedKeys:
                        PdfDictionary appearance = ResolveObject(annot.Get("AP")) as PdfDictionary
                            ?? throw new InvalidOperationException(
                                $"ClassifyAnnotationAppearance classified object {annot.ObjectNumber} "
                                + "as strippable (a resolvable /AP dictionary), but "
                                + "RepairAnnotationAppearances could not re-resolve it moments later "
                                + "on the same, unmutated-in-between annotation. This is a bug: the "
                                + "two must never disagree.");
                        appearance.Remove(ApDownKey);
                        appearance.Remove(ApRolloverKey);
                        break;

                    case AnnotationAppearanceRepairKind.WriteBlankAppearance:
                        WriteBlankAppearance(annot);
                        break;

                    // Throw-on-unknown, the discipline this codebase already adopted at the
                    // ImageDictionaryRepairKind write switch (2026-08-21) and the 13-type DrawCommand
                    // canary before it: a kind added to AnnotationAppearanceRepairKind and to
                    // ClassifyAnnotationAppearance's `repairs` list but not to this switch would
                    // otherwise be reported as Applied while writing nothing at all. A backstop for a
                    // hypothetical future third kind now that both R1 and R2 are wired up above.
                    default:
                        throw new NotSupportedException(
                            $"No write is implemented for annotation-appearance repair kind '{kind}' "
                            + $"on object {annot.ObjectNumber}. ClassifyAnnotationAppearance "
                            + "classified it as repairable, so either add the write here or make the "
                            + "kind refusal-only.");
                }

            repaired.Add(new AnnotationAppearanceRepair(annot.ObjectNumber, repairs));
        }

        return new AnnotationAppearanceRepairReport(repaired, refusals);
    }

    /// <summary>Writes R2's blank normal appearance for <paramref name="annot"/>, reusing
    /// <see cref="FieldAppearanceGenerator.Regenerate"/> -- the same writer a <c>FormFieldTree</c>-
    /// built field uses -- rather than a second appearance-stream writer (design doc §5: its blank
    /// case, <c>string value = field.Value ?? string.Empty</c>, is already natural, and every writer
    /// path assigns <c>/AP</c> a fresh single-key <c>{N: …}</c> dict, so the output is t2-clean by
    /// construction).
    ///
    /// <para>The <see cref="PdfFormField"/> view built here scopes <c>WidgetDicts</c> to ONLY
    /// <paramref name="annot"/> -- never every widget a full <c>FormFieldTree</c> read would attach
    /// to the owning field -- because <see cref="RepairAnnotationAppearances"/> may be called with an
    /// <c>objectNumbers</c> filter that excludes a sibling widget of the same field (two widgets can
    /// share one field dict, e.g. the same field shown on two pages); <c>Regenerate</c> must never
    /// write to a widget the caller did not name.</para>
    ///
    /// <para>Verifies afterward that a single-key <c>/AP /N</c> actually landed.
    /// <see cref="ClassifyBlankAppearance"/> vets <c>/FT</c> and the effective <c>/V</c> but not
    /// <c>/Rect</c>; <c>FieldAppearanceGenerator</c> silently writes nothing for a malformed or
    /// non-positive-area <c>/Rect</c> (each writer path's own rect-parsing / <c>w &lt;= 0 || h &lt;= 0</c>
    /// guard) -- not in the measured population, since the rule itself exempts a genuinely
    /// zero-sized annotation from needing <c>/AP</c> at all. Throwing here rather than reporting a
    /// false <see cref="AnnotationAppearanceRepair"/> follows the same discipline as the re-resolve
    /// check in the <c>StripRejectedKeys</c> case above: an invariant violation is a bug to surface
    /// loudly, never a silent partial fix.</para></summary>
    private void WriteBlankAppearance(PdfDictionary annot)
    {
        (string? ft, int ff, PdfObject? _) = ResolveEffectiveField(annot);

        PdfFormField field = ft switch
        {
            "Tx" => BuildBlankTextFieldView(annot, ff),
            "Ch" => BuildBlankChoiceFieldView(annot, ff),
            _ => throw new InvalidOperationException(
                $"ClassifyBlankAppearance classified object {annot.ObjectNumber} for "
                + $"WriteBlankAppearance with effective /FT '{ft ?? "(none)"}', but only /Tx and /Ch "
                + "are ever classified for this repair. This is a bug: the two must never disagree."),
        };

        FieldAppearanceGenerator.Regenerate(_document, field);

        if (ResolveObject(annot.Get("AP")) is not PdfDictionary appearance
            || appearance.Count != 1 || !appearance.ContainsKey(ApNormalKey))
            throw new InvalidOperationException(
                $"WriteBlankAppearance asked FieldAppearanceGenerator to regenerate object "
                + $"{annot.ObjectNumber}'s appearance, but no single-key /AP /N resulted -- most "
                + "likely a malformed or non-positive-area /Rect FieldAppearanceGenerator silently "
                + "skips. This widget should never have been classified for this repair.");
    }

    /// <summary>Builds a <see cref="PdfTextField"/> view of <paramref name="annot"/> scoped to just
    /// this one widget, with its value forced blank (this repair's entire point) via
    /// <see cref="PdfTextField.SetValueInternal"/> -- never the public <c>Value</c> setter, which
    /// would write <c>/V</c> onto the widget and immediately re-invoke
    /// <see cref="FieldAppearanceGenerator.Regenerate"/> itself.</summary>
    private PdfTextField BuildBlankTextFieldView(PdfDictionary annot, int ff)
    {
        var field = new PdfTextField
        {
            IsComb = FieldFlags.Has(ff, FieldFlags.Comb),
            IsPassword = FieldFlags.Has(ff, FieldFlags.Password),
            Dict = annot,
            Doc = _document,
            WidgetDicts = new[] { annot },
        };
        field.SetMaxLengthInternal(
            ResolveObject(annot.Get("MaxLen")) is PdfInteger maxLenInt ? (int)maxLenInt.Value : null);
        field.SetIsMultilineInternal(FieldFlags.Has(ff, FieldFlags.Multiline));
        field.SetQuaddingInternal(ResolveObject(annot.Get("Q")) is PdfInteger qInt ? (int)qInt.Value : 0);
        field.SetValueInternal(null);
        return field;
    }

    /// <summary>Builds a <see cref="PdfChoiceField"/> view of <paramref name="annot"/> scoped to just
    /// this one widget, with no selection (this repair's entire point) via the internal setters --
    /// never <c>SelectedValues</c>/<c>SelectedIndices</c>, which would write <c>/V</c>/<c>/I</c> onto
    /// the widget and re-invoke <see cref="FieldAppearanceGenerator.Regenerate"/> itself.</summary>
    private PdfChoiceField BuildBlankChoiceFieldView(PdfDictionary annot, int ff)
    {
        var field = new PdfChoiceField
        {
            IsCombo = FieldFlags.Has(ff, FieldFlags.Combo),
            IsMultiSelect = FieldFlags.Has(ff, FieldFlags.MultiSelect),
            Dict = annot,
            Doc = _document,
            WidgetDicts = new[] { annot },
        };
        field.SetOptionsInternal(ReadOptions(annot));
        field.SetSelectedValuesInternal(Array.Empty<string>());
        field.SetSelectedIndicesInternal(Array.Empty<int>());
        return field;
    }

    /// <summary>Reads <paramref name="annot"/>'s own <c>/Opt</c> (export/display pairs, or bare
    /// strings where export == display) the same way <c>FormFieldTree</c> does. Not walked up
    /// <c>/Parent</c> -- unlike <c>/FT</c>/<c>/Ff</c>/<c>/V</c> -- because a blank field's content is
    /// identical whether or not its list rows are populated with the true inherited options; this
    /// only matters for a real list box's row count, which the corpus's zero blank-<c>/Ch</c>
    /// findings never exercise (design doc §2), so own-dict-only is the proportionate scope.</summary>
    private List<(string Export, string Display)> ReadOptions(PdfDictionary annot)
    {
        var options = new List<(string, string)>();
        if (ResolveObject(annot.Get("Opt")) is not PdfArray optArr)
            return options;

        foreach (PdfObject item in optArr)
        {
            PdfObject? resolved = ResolveObject(item) ?? item;
            if (resolved is PdfArray { Count: >= 2 } pair)
                options.Add((StringFromPdf(ResolveObject(pair[0])), StringFromPdf(ResolveObject(pair[1]))));
            else
                options.Add((StringFromPdf(resolved), StringFromPdf(resolved)));
        }
        return options;
    }

    private static string StringFromPdf(PdfObject? obj) => obj switch
    {
        PdfString s => s.GetText(),
        PdfName n => n.Value,
        _ => string.Empty,
    };
}
