using System.Text;
using System.Xml;
using System.Xml.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing;

/// <summary>The document-level form configuration entries that can be removed without inventing content.</summary>
public sealed record FormConfigurationRepairCandidate(
    bool RemovesNeedAppearances,
    bool RemovesXfa,
    int XfaPacketCount,
    int PreservedFieldCount,
    bool InvalidatesUsageRightsSignature);

/// <summary>A form configuration condition the editor cannot prove safe to remove.</summary>
public sealed record FormConfigurationRefusal(string Reason);

/// <summary>Read-only result of classifying the current document-level form configuration.</summary>
public sealed record FormConfigurationRepairPreview(
    FormConfigurationRepairCandidate? Candidate,
    IReadOnlyList<FormConfigurationRefusal> Refused);

/// <summary>The exact form configuration entries removed by one repair.</summary>
public sealed record FormConfigurationRepair(
    bool RemovedNeedAppearances,
    bool RemovedXfa,
    int RemovedXfaPacketCount,
    int PreservedFieldCount,
    bool InvalidatedUsageRightsSignature);

/// <summary>What the current-document reclassification changed and refused.</summary>
public sealed record FormConfigurationRepairReport(
    FormConfigurationRepair? Repaired,
    IReadOnlyList<FormConfigurationRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    private sealed record FormConfigurationClassification(
        PdfDictionary? AcroForm,
        FormConfigurationRepairCandidate? Candidate,
        IReadOnlyList<FormConfigurationRefusal> Refused);

    private sealed record TerminalField(
        string Name,
        string FieldType,
        PdfObject? EffectiveValue,
        IReadOnlyList<PdfDictionary> Widgets);

    private sealed record FieldInventory(
        IReadOnlyList<TerminalField> Terminals,
        IReadOnlyDictionary<PdfDictionary, int> PagePlacements,
        IReadOnlySet<PdfDictionary> FieldTreeWidgets);

    private sealed record XfaPacket(string Name, PdfStream Stream);

    private static readonly PdfName FormConfigurationNeedAppearancesKey = new("NeedAppearances");
    private static readonly PdfName FormConfigurationXfaKey = new("XFA");
    private static readonly PdfName FormConfigurationNeedsRenderingKey = new("NeedsRendering");
    private static readonly PdfName FormConfigurationPermsKey = new("Perms");
    private static readonly PdfName FormConfigurationDocMdpKey = new("DocMDP");
    private static readonly PdfName FormConfigurationUr3Key = new("UR3");

    /// <summary>
    /// Classifies <c>/NeedAppearances</c>, static XFAF, and <c>/NeedsRendering</c> without mutation.
    /// Preview and write deliberately share the same classifier; the write never trusts a stale preview.
    /// Raw XFA XML, field names, and field values are not exposed in the result.
    /// </summary>
    public FormConfigurationRepairPreview PreviewFormConfigurationRepair()
    {
        FormConfigurationClassification classification = ClassifyFormConfigurationRepair();
        return new FormConfigurationRepairPreview(classification.Candidate, classification.Refused);
    }

    /// <summary>
    /// Reclassifies the live document and removes only the proven-safe <c>/NeedAppearances</c> and/or
    /// <c>/XFA</c> entries. It never clears <c>/NeedsRendering</c>, edits fields or Widgets, or deletes
    /// signature permission dictionaries.
    /// </summary>
    public FormConfigurationRepairReport RepairFormConfiguration()
    {
        FormConfigurationClassification classification = ClassifyFormConfigurationRepair();
        FormConfigurationRepairCandidate? candidate = classification.Candidate;
        if (candidate is null || classification.AcroForm is null)
            return new FormConfigurationRepairReport(null, classification.Refused);

        bool removedNeedAppearances = candidate.RemovesNeedAppearances
                                      && classification.AcroForm.Remove(FormConfigurationNeedAppearancesKey);
        bool removedXfa = candidate.RemovesXfa
                          && classification.AcroForm.Remove(FormConfigurationXfaKey);
        if (!removedNeedAppearances && !removedXfa)
            return new FormConfigurationRepairReport(null, classification.Refused);

        return new FormConfigurationRepairReport(
            new FormConfigurationRepair(
                removedNeedAppearances,
                removedXfa,
                removedXfa ? candidate.XfaPacketCount : 0,
                candidate.PreservedFieldCount,
                candidate.InvalidatesUsageRightsSignature),
            classification.Refused);
    }

    private FormConfigurationClassification ClassifyFormConfigurationRepair()
    {
        var context = new ConformanceContext(_document, ConformanceProfile.PdfA2b);
        PdfDictionary? catalog = context.Catalog?.Dictionary;
        PdfDictionary? acroForm = context.Catalog?.GetAcroForm();
        bool hasNeedAppearances = acroForm is not null
                                  && context.Resolve(acroForm.Get(FormConfigurationNeedAppearancesKey))
                                  is PdfBoolean { Value: true };
        bool hasXfa = acroForm?.ContainsKey(FormConfigurationXfaKey) == true;
        bool needsRendering = catalog is not null
                              && context.Resolve(catalog.Get(FormConfigurationNeedsRenderingKey))
                              is PdfBoolean { Value: true };

        if (!hasNeedAppearances && !hasXfa && !needsRendering)
            return new FormConfigurationClassification(acroForm, null, []);

        var refusals = new List<FormConfigurationRefusal>();
        if (needsRendering)
            refusals.Add(new FormConfigurationRefusal(
                "The catalog sets /NeedsRendering true. Pellucid cannot synthesize the page content "
              + "a dynamic XFA shell requires and never removes this entry."));

        if (acroForm is null)
            return new FormConfigurationClassification(null, null, refusals);

        if (!TryBuildFieldInventory(context, acroForm, out FieldInventory inventory, out string? inventoryReason))
        {
            if (hasNeedAppearances || hasXfa)
                refusals.Add(new FormConfigurationRefusal(inventoryReason!));
            return new FormConfigurationClassification(acroForm, null, refusals);
        }

        bool protectedDocument = HasConfigurationDocMdp(context, catalog)
                                 || HasConfigurationSignedSignature(inventory.Terminals);
        if (protectedDocument)
        {
            if (hasNeedAppearances || hasXfa)
                refusals.Add(new FormConfigurationRefusal(
                    "Form configuration was left unchanged because the document carries a signed "
                  + "signature field value or DocMDP permission. Pellucid performs a full rewrite and "
                  + "does not claim to preserve that protection."));
            return new FormConfigurationClassification(acroForm, null, refusals);
        }

        bool removeNeedAppearances = false;
        if (hasNeedAppearances)
        {
            if (TryProveWidgetContract(context, inventory, requireAllAppearances: true, out string? reason))
                removeNeedAppearances = true;
            else
                refusals.Add(new FormConfigurationRefusal(
                    "The /NeedAppearances entry was left in place because " + reason));
        }

        bool removeXfa = false;
        int xfaPacketCount = 0;
        if (hasXfa && needsRendering && inventory.Terminals.Count == 0)
        {
            refusals.Add(new FormConfigurationRefusal(
                "The /XFA entry was left in place because the XFA template or AcroForm has no terminal fields to preserve."));
        }
        else if (hasXfa && !needsRendering)
        {
            if (TryClassifyStaticXfa(
                    context, acroForm, inventory, out xfaPacketCount, out string? reason))
            {
                removeXfa = true;
            }
            else
            {
                refusals.Add(new FormConfigurationRefusal(
                    "The /XFA entry was left in place because " + reason));
            }
        }

        bool hasUr3 = HasConfigurationUr3(context, catalog);
        if (hasUr3 && removeNeedAppearances && !removeXfa)
        {
            removeNeedAppearances = false;
            refusals.Add(new FormConfigurationRefusal(
                "The /NeedAppearances entry was left in place because the document carries /Perms /UR3 "
              + "usage rights and no eligible XFA removal candidate is available for the required "
              + "consented safe-copy workflow."));
        }

        if (!removeNeedAppearances && !removeXfa)
            return new FormConfigurationClassification(acroForm, null, refusals);

        var candidate = new FormConfigurationRepairCandidate(
            removeNeedAppearances,
            removeXfa,
            removeXfa ? xfaPacketCount : 0,
            inventory.Terminals.Count,
            hasUr3);
        return new FormConfigurationClassification(acroForm, candidate, refusals);
    }

    private bool TryBuildFieldInventory(
        ConformanceContext context,
        PdfDictionary acroForm,
        out FieldInventory inventory,
        out string? reason)
    {
        inventory = null!;
        reason = null;
        string? failure = null;
        if (context.Resolve(acroForm.Get("Fields")) is not PdfArray roots)
        {
            reason = "the AcroForm /Fields entry is missing or is not an array.";
            return false;
        }

        var terminals = new List<TerminalField>();
        var seenFields = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var treeWidgets = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var active = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var budget = 100_000;

        bool Visit(PdfObject raw, string prefix, string? inheritedType, PdfObject? inheritedValue)
        {
            if (--budget < 0)
            {
                failure = "the AcroForm field tree exceeds the bounded 100,000-node inspection limit.";
                return false;
            }
            if (context.Resolve(raw) is not PdfDictionary field)
            {
                failure = "the AcroForm field tree contains an entry that is not a dictionary.";
                return false;
            }
            if (!active.Add(field))
            {
                failure = "the AcroForm field tree contains a cycle.";
                return false;
            }
            if (!seenFields.Add(field))
            {
                active.Remove(field);
                failure = "one AcroForm field dictionary is reached through more than one parent path.";
                return false;
            }

            string? partialName = (context.Resolve(field.Get("T")) as PdfString)?.GetText();
            string fullName = string.IsNullOrEmpty(partialName)
                ? prefix
                : string.IsNullOrEmpty(prefix) ? partialName : prefix + "." + partialName;
            string? fieldType = context.ResolveName(field.Get("FT")) ?? inheritedType;
            PdfObject? effectiveValue = ResolveOwnOrInheritedValue(context, field, inheritedValue);
            bool mergedWidget = context.ResolveName(field.Get("Subtype")) == "Widget";

            var childFields = new List<PdfObject>();
            var widgets = new List<PdfDictionary>();
            if (mergedWidget)
                widgets.Add(field);

            PdfObject? kidsObject = context.Resolve(field.Get("Kids"));
            if (kidsObject is not null and not PdfNull)
            {
                if (kidsObject is not PdfArray kids)
                {
                    active.Remove(field);
                    failure = "an AcroForm field /Kids entry is not an array.";
                    return false;
                }
                foreach (PdfObject kidRaw in kids)
                {
                    if (context.Resolve(kidRaw) is not PdfDictionary kid)
                    {
                        active.Remove(field);
                        failure = "an AcroForm field /Kids entry does not resolve to a dictionary.";
                        return false;
                    }
                    bool widgetOnly = context.ResolveName(kid.Get("Subtype")) == "Widget"
                                      && !kid.ContainsKey(new PdfName("T"))
                                      && !kid.ContainsKey(new PdfName("FT"))
                                      && !kid.ContainsKey(new PdfName("Kids"));
                    if (widgetOnly)
                        widgets.Add(kid);
                    else
                        childFields.Add(kidRaw);
                }
            }

            if (childFields.Count == 0 && fieldType is not null)
            {
                if (string.IsNullOrEmpty(fullName))
                {
                    active.Remove(field);
                    failure = "a terminal AcroForm field has no fully qualified name.";
                    return false;
                }
                foreach (PdfDictionary widget in widgets)
                    if (!treeWidgets.Add(widget))
                    {
                        active.Remove(field);
                        failure = "one Widget is associated with more than one terminal field.";
                        return false;
                    }
                terminals.Add(new TerminalField(fullName, fieldType, effectiveValue, widgets));
            }

            foreach (PdfObject child in childFields)
                if (!Visit(child, fullName, fieldType, effectiveValue))
                {
                    active.Remove(field);
                    return false;
                }

            active.Remove(field);
            return true;
        }

        foreach (PdfObject root in roots)
            if (!Visit(root, "", null, null))
            {
                reason = failure;
                return false;
            }

        if (terminals.Select(field => field.Name).Distinct(StringComparer.Ordinal).Count() != terminals.Count)
        {
            reason = "the AcroForm contains duplicate fully qualified terminal field names.";
            return false;
        }

        var placements = new Dictionary<PdfDictionary, int>(ReferenceEqualityComparer.Instance);
        foreach (var page in context.Pages)
        {
            if (page.GetAnnotations() is not { } annotations)
                continue;
            foreach (PdfObject annotationRaw in annotations)
            {
                if (context.Resolve(annotationRaw) is not PdfDictionary annotation
                    || context.ResolveName(annotation.Get("Subtype")) != "Widget")
                    continue;
                placements.TryGetValue(annotation, out int count);
                placements[annotation] = count + 1;
            }
        }

        inventory = new FieldInventory(terminals, placements, treeWidgets);
        return true;
    }

    private static PdfObject? ResolveOwnOrInheritedValue(
        ConformanceContext context, PdfDictionary field, PdfObject? inherited)
    {
        if (!field.TryGetValue(new PdfName("V"), out PdfObject raw))
            return inherited;
        PdfObject? resolved = context.Resolve(raw);
        return resolved is null or PdfNull ? inherited : resolved;
    }

    private static bool TryProveWidgetContract(
        ConformanceContext context,
        FieldInventory inventory,
        bool requireAllAppearances,
        out string? reason)
    {
        reason = null;
        if (inventory.PagePlacements.Keys.Any(widget => !inventory.FieldTreeWidgets.Contains(widget)))
        {
            reason = "a page contains a Widget that is not reachable from the AcroForm field tree.";
            return false;
        }
        if (inventory.FieldTreeWidgets.Any(widget => !inventory.PagePlacements.ContainsKey(widget)))
        {
            reason = "a field-tree Widget is not placed on a page.";
            return false;
        }

        foreach (TerminalField field in inventory.Terminals)
        {
            if (field.FieldType is not "Tx" and not "Ch" and not "Btn" and not "Sig")
            {
                reason = "a terminal field has an unsupported /FT.";
                return false;
            }
            if (field.Widgets.Count != 1)
            {
                reason = "a terminal field does not have exactly one Widget.";
                return false;
            }

            PdfDictionary widget = field.Widgets[0];
            if (!widget.IsIndirect)
            {
                reason = "a Widget is direct rather than an independently stageable indirect object.";
                return false;
            }
            if (!inventory.PagePlacements.TryGetValue(widget, out int placementCount) || placementCount != 1)
            {
                reason = "a Widget is not placed on exactly one page.";
                return false;
            }
            if (context.Resolve(widget.Get("Rect")) is not PdfArray { Count: 4 } rect
                || rect.Any(value => context.Resolve(value) is not (PdfInteger or PdfReal)))
            {
                reason = "a Widget has no usable four-number /Rect.";
                return false;
            }

            PdfObject? normal = ResolveNormalAppearance(context, widget);
            switch (field.FieldType)
            {
                case "Tx":
                case "Ch":
                    if (requireAllAppearances || IsConfigurationValueNonEmpty(field.EffectiveValue))
                    {
                        if (normal is not PdfStream)
                        {
                            reason = "a text or choice Widget lacks a usable current normal appearance stream.";
                            return false;
                        }
                    }
                    else if (normal is not null and not PdfStream)
                    {
                        reason = "a blank text or choice Widget has a malformed normal appearance.";
                        return false;
                    }
                    break;

                case "Btn":
                    if (normal is not PdfDictionary states
                        || !TryValidateButtonState(
                            context, widget, field.EffectiveValue, states, requireAllAppearances))
                    {
                        reason = "a button Widget lacks a normal appearance state for its current value/state.";
                        return false;
                    }
                    break;

                case "Sig":
                    if (IsConfigurationValueNonEmpty(field.EffectiveValue))
                    {
                        reason = "a signature field carries a signed value.";
                        return false;
                    }
                    if (requireAllAppearances && normal is not PdfStream)
                    {
                        reason = "an unsigned signature Widget lacks a usable current normal appearance stream.";
                        return false;
                    }
                    if (!requireAllAppearances && normal is not null and not PdfStream)
                    {
                        reason = "an unsigned signature Widget has a malformed normal appearance.";
                        return false;
                    }
                    break;
            }
        }
        return true;
    }

    private static PdfObject? ResolveNormalAppearance(ConformanceContext context, PdfDictionary widget)
    {
        if (context.Resolve(widget.Get("AP")) is not PdfDictionary appearance)
            return null;
        return context.Resolve(appearance.Get("N"));
    }

    private static bool TryValidateButtonState(
        ConformanceContext context,
        PdfDictionary widget,
        PdfObject? effectiveValue,
        PdfDictionary states,
        bool requireAllAppearances)
    {
        string? appearanceState = context.ResolveName(widget.Get("AS"));
        string? valueState = context.ResolveName(effectiveValue);
        if (appearanceState is not null)
        {
            if (appearanceState != "Off")
                return HasUsableButtonState(context, states, appearanceState);

            // Static XFAF documents commonly omit an explicit /Off stream: the fixed PDF page already
            // carries the empty control chrome and an unselected Widget intentionally draws no mark.
            // That is safe for XFA removal, which requires only CURRENT non-empty appearances. It is
            // not enough to remove /NeedAppearances, whose stronger contract requires every current
            // Widget state to resolve to a stream.
            return !requireAllAppearances || HasUsableButtonState(context, states, "Off");
        }

        if (valueState is not null && valueState != "Off")
            return HasUsableButtonState(context, states, valueState);
        return !requireAllAppearances || HasUsableButtonState(context, states, "Off");
    }

    private static bool HasUsableButtonState(
        ConformanceContext context, PdfDictionary states, string state) =>
        states.TryGetValue(new PdfName(state), out PdfObject raw)
        && context.Resolve(raw) is PdfStream;

    private bool TryClassifyStaticXfa(
        ConformanceContext context,
        PdfDictionary acroForm,
        FieldInventory inventory,
        out int packetCount,
        out string? reason)
    {
        packetCount = 0;
        reason = null;
        if (inventory.Terminals.Count == 0)
        {
            reason = "the XFA template or AcroForm has no terminal fields to preserve.";
            return false;
        }
        if (!TryReadXfaPackets(context, acroForm.Get(FormConfigurationXfaKey), out List<XfaPacket> packets, out reason))
            return false;
        packetCount = packets.Count;

        XfaPacket[] configs = [.. packets.Where(packet => packet.Name == "config")];
        XfaPacket[] templates = [.. packets.Where(packet => packet.Name == "template")];
        if (configs.Length != 1 || templates.Length != 1)
        {
            reason = "the packet array does not contain exactly one named config and one named template packet.";
            return false;
        }

        if (!TryParseXfaXml(context, configs[0].Stream, out XDocument config, out reason)
            || !TryParseXfaXml(context, templates[0].Stream, out XDocument template, out reason))
            return false;
        if (config.Root?.Name.LocalName != "config" || template.Root?.Name.LocalName != "template")
        {
            reason = "the named config or template packet has an unexpected XML root element.";
            return false;
        }

        string[] dynamicRenderValues =
        [
            .. config.Descendants()
                .Where(element => element.Name.LocalName == "dynamicRender")
                .Select(element => element.Value.Trim()),
        ];
        if (dynamicRenderValues.Length != 1
            || !string.Equals(dynamicRenderValues[0], "forbidden", StringComparison.Ordinal))
        {
            reason = "the config packet's dynamicRender control is missing, duplicated, or not exactly 'forbidden'.";
            return false;
        }

        if (!TryGenerateSomPaths(template.Root, out IReadOnlyList<string> templateNames, out reason))
            return false;
        if (templateNames.Count == 0)
        {
            reason = "the XFA template or AcroForm has no terminal fields to preserve.";
            return false;
        }

        var acroNames = new HashSet<string>(inventory.Terminals.Select(field => field.Name), StringComparer.Ordinal);
        var xfaNames = new HashSet<string>(templateNames, StringComparer.Ordinal);
        if (xfaNames.Count != templateNames.Count || !xfaNames.SetEquals(acroNames))
        {
            reason = "the exact XFA-SOM terminal-name set does not equal the AcroForm terminal-name set.";
            return false;
        }

        return TryProveWidgetContract(context, inventory, requireAllAppearances: false, out reason);
    }

    private bool TryReadXfaPackets(
        ConformanceContext context,
        PdfObject? xfaRaw,
        out List<XfaPacket> packets,
        out string? reason)
    {
        packets = null!;
        reason = null;
        if (context.Resolve(xfaRaw) is not PdfArray array || array.Count == 0 || array.Count % 2 != 0)
        {
            reason = "the XFA value is not a non-empty, even-length packet array.";
            return false;
        }

        var result = new List<XfaPacket>(array.Count / 2);
        for (var index = 0; index < array.Count; index += 2)
        {
            if (context.Resolve(array[index]) is not PdfString name
                || context.Resolve(array[index + 1]) is not PdfStream stream)
            {
                reason = "an XFA packet name is not a string or its value is not a stream.";
                return false;
            }
            string packetName = name.GetText();
            if (string.IsNullOrEmpty(packetName))
            {
                reason = "an XFA packet has an empty name.";
                return false;
            }
            result.Add(new XfaPacket(packetName, stream));
        }
        packets = result;
        return true;
    }

    private bool TryParseXfaXml(
        ConformanceContext context,
        PdfStream stream,
        out XDocument document,
        out string? reason)
    {
        document = null!;
        reason = null;
        byte[] bytes;
        try { bytes = stream.GetDecodedData(context.Document.Decryptor); }
        catch (Exception exception)
        {
            reason = $"an XFA packet cannot be decoded ({exception.GetType().Name}).";
            return false;
        }
        const int maxPacketBytes = 32 * 1024 * 1024;
        if (bytes.Length > maxPacketBytes)
        {
            reason = "an XFA XML packet exceeds the bounded 32 MiB inspection limit.";
            return false;
        }

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = maxPacketBytes,
                MaxCharactersFromEntities = 0,
            };
            using var input = new MemoryStream(bytes, writable: false);
            using XmlReader reader = XmlReader.Create(input, settings);
            document = XDocument.Load(reader, LoadOptions.None);
            return true;
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            reason = $"an XFA packet is not bounded, well-formed XML ({exception.GetType().Name}).";
            return false;
        }
    }

    private static bool TryGenerateSomPaths(
        XElement root,
        out IReadOnlyList<string> paths,
        out string? reason)
    {
        var result = new List<string>();
        var nodeBudget = 100_000;
        string? failure = null;
        reason = null;

        bool Visit(XElement parent, IReadOnlyList<string> prefix, int depth)
        {
            if (depth > 256)
            {
                failure = "the XFA template exceeds the bounded 256-level traversal depth.";
                return false;
            }
            XElement[] siblings = [.. parent.Elements()];
            for (var childIndex = 0; childIndex < siblings.Length; childIndex++)
            {
                if (--nodeBudget < 0)
                {
                    failure = "the XFA template exceeds the bounded 100,000-element inspection limit.";
                    return false;
                }

                XElement child = siblings[childIndex];
                string kind = child.Name.LocalName;
                IReadOnlyList<string> nextPrefix = prefix;
                if (kind is "subform" or "field" or "exclGroup")
                {
                    string? declaredName = child.Attribute("name")?.Value;
                    string peerName = string.IsNullOrEmpty(declaredName) ? "#" + kind : declaredName;
                    int index = siblings.Take(childIndex).Count(previous =>
                        previous.Name.LocalName == kind
                        && (string.IsNullOrEmpty(previous.Attribute("name")?.Value)
                            ? "#" + kind
                            : previous.Attribute("name")!.Value) == peerName);
                    nextPrefix = [.. prefix, $"{peerName}[{index}]"];
                    if (kind is "field" or "exclGroup")
                        result.Add(string.Join('.', nextPrefix));
                }

                if (!Visit(child, nextPrefix, depth + 1))
                    return false;
            }
            return true;
        }

        if (!Visit(root, [], 0))
        {
            paths = null!;
            reason = failure;
            return false;
        }
        paths = result;
        return true;
    }

    private static bool IsConfigurationValueNonEmpty(PdfObject? value) => value switch
    {
        null or PdfNull => false,
        PdfString text => text.GetText().Length != 0,
        PdfArray array => array.Count != 0,
        PdfName name => name.Value != "Off",
        _ => true,
    };

    private static bool HasConfigurationSignedSignature(IReadOnlyList<TerminalField> fields) =>
        fields.Any(field => field.FieldType == "Sig" && IsConfigurationValueNonEmpty(field.EffectiveValue));

    private static bool HasConfigurationDocMdp(ConformanceContext context, PdfDictionary? catalog) =>
        catalog is not null
        && context.Resolve(catalog.Get(FormConfigurationPermsKey)) is PdfDictionary permissions
        && permissions.ContainsKey(FormConfigurationDocMdpKey);

    private static bool HasConfigurationUr3(ConformanceContext context, PdfDictionary? catalog) =>
        catalog is not null
        && context.Resolve(catalog.Get(FormConfigurationPermsKey)) is PdfDictionary permissions
        && permissions.ContainsKey(FormConfigurationUr3Key);
}
