using PdfLibrary.Conformance;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Editing;

/// <summary>The kind of content-stream owner whose omitted /Resources entry can be materialized.</summary>
public enum ExplicitResourceOwnerKind
{
    Page,
    FormXObject,
    Type3Font,
}

/// <summary>One safely repairable owner of an inherited-resource conformance finding.</summary>
public sealed record ExplicitResourceRepairCandidate(int ObjectNumber, ExplicitResourceOwnerKind OwnerKind);

/// <summary>One owner the editor deliberately declines to change.</summary>
public sealed record ExplicitResourceRefusal(int ObjectNumber, string Reason);

/// <summary>Read-only classification of explicit-resource repairs in the current document graph.</summary>
public sealed record ExplicitResourceRepairPreview(
    IReadOnlyList<ExplicitResourceRepairCandidate> Candidates,
    IReadOnlyList<ExplicitResourceRefusal> Refused);

/// <summary>One owner on which the effective /Resources value was materialized as-is.</summary>
public sealed record ExplicitResourceRepair(int ObjectNumber, ExplicitResourceOwnerKind OwnerKind);

/// <summary>What <see cref="PdfDocumentEditor.RepairExplicitResources"/> applied and refused.</summary>
public sealed record ExplicitResourceRepairReport(
    IReadOnlyList<ExplicitResourceRepair> Applied,
    IReadOnlyList<ExplicitResourceRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    private const int ExplicitResourceMaxDepth = 24;

    private static readonly HashSet<string> ExplicitResourceDeviceColourSpaces =
        new(StringComparer.Ordinal) { "DeviceGray", "DeviceRGB", "DeviceCMYK", "Pattern" };

    /// <summary>
    /// Classifies every <c>explicit-resources</c> owner without mutating the document. A candidate has
    /// no direct /Resources entry and resolves every offending invocation through the same effective
    /// resource dictionary. Existing dictionaries are never merged or replaced.
    /// </summary>
    public ExplicitResourceRepairPreview PreviewExplicitResourceRepairs()
    {
        IReadOnlyList<ExplicitResourceClassification> classified = ClassifyExplicitResourceRepairs();
        return new ExplicitResourceRepairPreview(
            [.. classified.Where(c => c.Refusal is null)
                .Select(c => new ExplicitResourceRepairCandidate(c.ObjectNumber, c.OwnerKind))],
            [.. classified.Where(c => c.Refusal is not null)
                .Select(c => new ExplicitResourceRefusal(c.ObjectNumber, c.Refusal!))]);
    }

    /// <summary>
    /// Materializes each selected owner's already-effective /Resources value. <paramref name="objectNumbers"/>
    /// null means every safe candidate; a non-null set is an exact staged selection.
    /// </summary>
    public ExplicitResourceRepairReport RepairExplicitResources(ISet<int>? objectNumbers = null)
    {
        IReadOnlyList<ExplicitResourceClassification> classified = ClassifyExplicitResourceRepairs();
        var applied = new List<ExplicitResourceRepair>();
        var refused = new List<ExplicitResourceRefusal>();
        var accountedFor = new HashSet<int>();

        foreach (ExplicitResourceClassification item in classified)
        {
            if (objectNumbers is not null && !objectNumbers.Contains(item.ObjectNumber))
                continue;

            accountedFor.Add(item.ObjectNumber);
            if (item.Refusal is { } reason)
            {
                refused.Add(new ExplicitResourceRefusal(item.ObjectNumber, reason));
                continue;
            }

            // Reclassification above is the write-time guard. This second local check protects against
            // accidental future mutation inside the classification loop itself.
            if (item.Owner.Get("Resources") is not null || item.Source is null)
            {
                refused.Add(new ExplicitResourceRefusal(
                    item.ObjectNumber,
                    "The owner no longer has an omitted /Resources entry with a stable effective value."));
                continue;
            }

            item.Owner.Set("Resources", item.Source);
            applied.Add(new ExplicitResourceRepair(item.ObjectNumber, item.OwnerKind));
        }

        if (objectNumbers is not null)
        {
            foreach (int objectNumber in objectNumbers.Order())
            {
                if (accountedFor.Contains(objectNumber)) continue;
                refused.Add(new ExplicitResourceRefusal(
                    objectNumber,
                    "The object no longer presents a safely repairable explicit-resources violation."));
            }
        }

        return new ExplicitResourceRepairReport(applied, refused);
    }

    private IReadOnlyList<ExplicitResourceClassification> ClassifyExplicitResourceRepairs()
    {
        var context = new ConformanceContext(_document, ConformanceProfile.PdfA2b);
        var owners = new Dictionary<int, ExplicitResourceAccumulator>();

        foreach (PdfPage page in context.Pages)
        {
            ExplicitResourceScope direct = ExplicitResourceScopeOf(context, page.Dictionary.Get("Resources"));
            ExplicitResourceScope inherited = ExplicitResourceInheritedPageScope(context, page.Dictionary);
            IReadOnlyList<PdfOperator> operators = context.PageContentOperators(page);
            WalkExplicitResourceStream(
                context, operators, direct, inherited, page.Dictionary,
                page.Dictionary.IsIndirect ? page.Dictionary.ObjectNumber : null,
                ExplicitResourceOwnerKind.Page, 0, new HashSet<int>(), owners);
        }

        return [.. owners.Values.Select(a => a.ToClassification())];
    }

    private void WalkExplicitResourceStream(
        ConformanceContext context,
        IReadOnlyList<PdfOperator> operators,
        ExplicitResourceScope direct,
        ExplicitResourceScope inherited,
        PdfDictionary owner,
        int? ownerObjectNumber,
        ExplicitResourceOwnerKind ownerKind,
        int depth,
        HashSet<int> activeObjects,
        Dictionary<int, ExplicitResourceAccumulator> owners)
    {
        if (depth > ExplicitResourceMaxDepth) return;

        if (ExplicitResourceOffenders(context, operators, direct, inherited).Count > 0
            && ownerObjectNumber is { } number)
        {
            if (!owners.TryGetValue(number, out ExplicitResourceAccumulator? accumulator))
            {
                accumulator = new ExplicitResourceAccumulator(number, ownerKind, owner);
                owners.Add(number, accumulator);
            }
            accumulator.Observe(context, direct.Raw, inherited.Raw, inherited.Dictionary);
        }

        ExplicitResourceScope effective = direct.Dictionary is not null ? direct : inherited;

        foreach (PdfOperator op in operators)
        {
            if (op is not InvokeXObjectOperator invoke
                || ExplicitResourceNamedObject(context, effective, "XObject", invoke.XObjectName) is not PdfStream form
                || context.ResolveName(form.Dictionary.Get("Subtype")) != "Form")
            {
                continue;
            }

            if (form.IsIndirect && !activeObjects.Add(form.ObjectNumber))
                continue;

            byte[] data;
            try { data = form.GetDecodedData(context.Document.Decryptor); }
            catch { data = []; }

            if (data.Length > 0)
            {
                IReadOnlyList<PdfOperator>? formOperators = null;
                try { formOperators = PdfContentParser.Parse(data); }
                catch { /* A malformed stream is another rule's concern. */ }

                if (formOperators is not null)
                {
                    WalkExplicitResourceStream(
                        context, formOperators,
                        ExplicitResourceScopeOf(context, form.Dictionary.Get("Resources")), effective,
                        form.Dictionary, form.IsIndirect ? form.ObjectNumber : null,
                        ExplicitResourceOwnerKind.FormXObject, depth + 1, activeObjects, owners);
                }
            }

            if (form.IsIndirect) activeObjects.Remove(form.ObjectNumber);
        }

        foreach (PdfOperator op in operators)
        {
            if (op.Name != "Tf" || ExplicitResourceNameOperand(op, 0) is not { } fontName)
                continue;
            WalkExplicitResourceType3(
                context, effective, fontName, depth, activeObjects, owners);
        }
    }

    private void WalkExplicitResourceType3(
        ConformanceContext context,
        ExplicitResourceScope effective,
        string fontName,
        int depth,
        HashSet<int> activeObjects,
        Dictionary<int, ExplicitResourceAccumulator> owners)
    {
        if (ExplicitResourceNamedObject(context, effective, "Font", fontName) is not PdfDictionary font
            || context.ResolveName(font.Get("Subtype")) != "Type3"
            || context.Resolve(font.Get("CharProcs")) is not PdfDictionary charProcs)
        {
            return;
        }

        if (font.IsIndirect && !activeObjects.Add(font.ObjectNumber))
            return;

        ExplicitResourceScope direct = ExplicitResourceScopeOf(context, font.Get("Resources"));
        foreach (PdfObject value in charProcs.Values.ToList())
        {
            if (context.Resolve(value) is not PdfStream charProc) continue;

            byte[] data;
            try { data = charProc.GetDecodedData(context.Document.Decryptor); }
            catch { continue; }
            if (data.Length == 0) continue;

            IReadOnlyList<PdfOperator> operators;
            try { operators = PdfContentParser.Parse(data); }
            catch { continue; }

            WalkExplicitResourceStream(
                context, operators, direct, effective, font,
                font.IsIndirect ? font.ObjectNumber : null,
                ExplicitResourceOwnerKind.Type3Font, depth + 1, activeObjects, owners);
        }

        if (font.IsIndirect) activeObjects.Remove(font.ObjectNumber);
    }

    private static ExplicitResourceScope ExplicitResourceScopeOf(
        ConformanceContext context, PdfObject? raw) =>
        raw is null or PdfNull
            ? default
            : new(raw, context.Resolve(raw) as PdfDictionary);

    private static ExplicitResourceScope ExplicitResourceInheritedPageScope(
        ConformanceContext context, PdfDictionary page)
    {
        var seen = new HashSet<int>();
        PdfDictionary? node = context.Resolve(page.Get("Parent")) as PdfDictionary;
        for (int budget = 100_000; node is not null && budget > 0; budget--)
        {
            if (node.IsIndirect && !seen.Add(node.ObjectNumber)) break;
            PdfObject? raw = node.Get("Resources");
            if (context.Resolve(raw) is PdfDictionary dictionary)
                return new ExplicitResourceScope(raw, dictionary);
            node = context.Resolve(node.Get("Parent")) as PdfDictionary;
        }
        return default;
    }

    private static PdfObject? ExplicitResourceNamedObject(
        ConformanceContext context, ExplicitResourceScope scope, string category, string name)
    {
        if (scope.Dictionary is null
            || context.Resolve(scope.Dictionary.Get(category)) is not PdfDictionary entries
            || !entries.TryGetValue(new PdfName(name), out PdfObject? value))
        {
            return null;
        }
        return context.Resolve(value);
    }

    private static List<string> ExplicitResourceOffenders(
        ConformanceContext context,
        IReadOnlyList<PdfOperator> operators,
        ExplicitResourceScope direct,
        ExplicitResourceScope inherited)
    {
        var offenders = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PdfOperator op in operators)
        {
            if (ExplicitResourceReference(op) is not { } reference) continue;
            (string category, string name) = reference;
            if (ExplicitResourceNamedObject(context, direct, category, name) is not null) continue;
            if (ExplicitResourceNamedObject(context, inherited, category, name) is null) continue;
            if (seen.Add($"{category}/{name}")) offenders.Add(name);
        }
        return offenders;
    }

    private static (string Category, string Name)? ExplicitResourceReference(PdfOperator op) =>
        op.Name switch
        {
            "Tf" when ExplicitResourceNameOperand(op, 0) is { } name => ("Font", name),
            "Do" when ExplicitResourceNameOperand(op, 0) is { } name => ("XObject", name),
            "sh" when ExplicitResourceNameOperand(op, 0) is { } name => ("Shading", name),
            "gs" when ExplicitResourceNameOperand(op, 0) is { } name => ("ExtGState", name),
            "cs" or "CS" when ExplicitResourceNameOperand(op, 0) is { } name
                && !ExplicitResourceDeviceColourSpaces.Contains(name) => ("ColorSpace", name),
            "scn" or "SCN" when ExplicitResourceTrailingNameOperand(op) is { } name => ("Pattern", name),
            _ => null,
        };

    private static string? ExplicitResourceNameOperand(PdfOperator op, int index) =>
        op.Operands.Count > index && op.Operands[index] is PdfName name ? name.Value : null;

    private static string? ExplicitResourceTrailingNameOperand(PdfOperator op) =>
        op.Operands.Count > 0 && op.Operands[^1] is PdfName name ? name.Value : null;

    private readonly record struct ExplicitResourceScope(PdfObject? Raw, PdfDictionary? Dictionary);

    private sealed class ExplicitResourceAccumulator(
        int objectNumber, ExplicitResourceOwnerKind ownerKind, PdfDictionary owner)
    {
        private PdfObject? _source;
        private PdfDictionary? _resolvedSource;
        private string? _refusal;

        public void Observe(
            ConformanceContext context,
            PdfObject? directRaw,
            PdfObject? inheritedRaw,
            PdfDictionary? inheritedDictionary)
        {
            if (directRaw is not null)
            {
                _refusal ??= "The owner already has a /Resources entry; Pellucid will not merge or replace it.";
                return;
            }
            if (inheritedRaw is null || inheritedDictionary is null)
            {
                _refusal ??= "No complete effective /Resources dictionary can be materialized safely.";
                return;
            }

            if (_source is null)
            {
                _source = inheritedRaw;
                _resolvedSource = inheritedDictionary;
                return;
            }

            if (!ReferenceEquals(_resolvedSource, context.Resolve(inheritedRaw)))
            {
                _refusal ??=
                    "The owner is invoked from scopes with different effective /Resources dictionaries.";
            }
        }

        public ExplicitResourceClassification ToClassification() =>
            new(objectNumber, ownerKind, owner, _source, _refusal);
    }

    private sealed record ExplicitResourceClassification(
        int ObjectNumber,
        ExplicitResourceOwnerKind OwnerKind,
        PdfDictionary Owner,
        PdfObject? Source,
        string? Refusal);
}
