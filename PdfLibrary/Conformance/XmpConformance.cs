using System;
using System.Collections.Generic;
using PdfLibrary.Conformance.Xmp;
using PdfLibrary.Metadata;
using PdfLibrary.Xmp;

namespace PdfLibrary.Conformance;

/// <summary>One property's standing under ISO 19005-2 clause 6.6.2.3.1, as the rules judge it.
/// <paramref name="ExpectedType"/> is the type that governs — predefined wins over an extension
/// declaration, exactly as <c>XmpPropertyTypeRule</c> resolves it — and is null when neither knows
/// the property, in which case <paramref name="TypeConforms"/> is true because there is no type to
/// violate (the predefined rule, not the type rule, owns that case).</summary>
public readonly record struct XmpPropertyVerdict(
    string NamespaceUri, string Prefix, string LocalName,
    bool IsPredefined, bool IsDeclaredByExtension,
    string? ExpectedType, bool TypeConforms);

/// <summary>Public read-only view of what the XMP conformance rules conclude about a packet's
/// properties. Exists so a remediation fixer can identify offending properties structurally instead
/// of regex-parsing a <c>Finding</c>'s free-text message — the same reason
/// <c>ConformanceClaim.Read</c> was factored out for the scan CLI.
///
/// <para>Mirrors <see cref="Rules.XmpPropertyPredefinedRule"/> and <see cref="Rules.XmpPropertyTypeRule"/>
/// exactly (same structural-namespace exclusion set, same predefined-beats-extension precedence, same
/// <see cref="XmpTypeContainer"/> dispatch) so the classifier cannot drift from what those rules
/// report. There is deliberately no profile parameter: <see cref="XmpPredefinedSchemas"/>'s predefined
/// table is one unconditional union with no per-profile branching, and both rules declare
/// <c>AppliesToProfiles => ConformanceProfile.AllPdfA</c> — a parameter here could not be honoured.</para>
///
/// <para><b>Multi-island packets: verdicts describe the packet as it would be SAVED, not necessarily
/// the packet as the rules see it today.</b> A packet may legally carry more than one sibling
/// <c>rdf:RDF</c> island under <c>x:xmpmeta</c> (the "DWC FX Generator" shape the official ZUGFeRD 2.5
/// examples use). <see cref="XmpPacket.Parse"/> has always merged every island into one property set —
/// that merged set is <see cref="XmpPacket"/>'s only model, and <see cref="XmpPacket.Serialize"/> always
/// writes it back as ONE island — so <see cref="ClassifyProperties"/>, which classifies exactly that
/// model via <see cref="XmpPacket.Nodes"/>, necessarily classifies every island merged together. The two
/// rules this mirrors instead read <c>ConformanceContext.XmpTree</c>, which parses the FIRST island
/// only. On a single-island packet (the overwhelming majority) the two trees are identical and verdicts
/// agree with findings exactly. On a genuine multi-island packet, this classifier's verdicts are a
/// SUPERSET of what the rules currently report: a property that lives only in island 2 is invisible to
/// today's rules but IS classified here — deliberately, because the next <c>Serialize()</c> merges it
/// into island 1, at which point the rules WOULD report it. A remediation fixer built on this classifier
/// is therefore correct to act on every verdict now, pre-empting a finding the rules have not raised yet
/// but will raise on the saved document. A caller that instead wants exactly what the rules report today,
/// island-for-island, must not use this method on a multi-island packet.</para>
/// </summary>
public static class XmpConformance
{
    // Same set as XmpPropertyPredefinedRule.StructuralNamespaces, copied verbatim so the two cannot
    // drift: RDF/XML plumbing plus the PDF/A extension-schema description namespaces (the standard's
    // own container, not stray user properties).
    private static readonly HashSet<string> StructuralNamespaces = new(StringComparer.Ordinal)
    {
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
        "http://www.w3.org/XML/1998/namespace",
        "adobe:ns:meta/",
        "http://www.aiim.org/pdfa/ns/extension/",
        "http://www.aiim.org/pdfa/ns/schema#",
        "http://www.aiim.org/pdfa/ns/property#",
        "http://www.aiim.org/pdfa/ns/type#",
        "http://www.aiim.org/pdfa/ns/field#",
    };

    /// <summary>Classifies every top-level property of <paramref name="packet"/>'s merged node tree
    /// (see the class doc comment for the multi-island / superset contract this implies).</summary>
    public static IReadOnlyList<XmpPropertyVerdict> ClassifyProperties(XmpPacket packet)
    {
        if (packet is null) throw new ArgumentNullException(nameof(packet));

        // packet.Nodes is XmpPacket's one model — already every rdf:RDF island merged (XmpPacket.Parse
        // always parses with allRdfIslands: true). See the class doc comment: this is deliberately a
        // superset of ConformanceContext.XmpTree (first island only) on a multi-island packet.
        IReadOnlyList<XmpNode> topLevel = packet.Nodes;
        XmpExtensionSchemas extensions = XmpExtensionSchemas.Parse(topLevel);

        var verdicts = new List<XmpPropertyVerdict>();
        foreach (XmpNode node in topLevel)
        {
            // Same exclusion as XmpPropertyPredefinedRule: RDF plumbing and the extension-schema
            // description namespaces are not packet properties.
            if (string.IsNullOrEmpty(node.NamespaceUri) || IsStructural(node.NamespaceUri))
                continue;

            bool predefined = XmpPredefinedSchemas.IsPredefined(node.NamespaceUri, node.LocalName);
            bool declared = extensions.IsDeclared(node.NamespaceUri, node.LocalName);

            // Same resolution order as XmpPropertyTypeRule: predefined wins over an extension
            // declaration.
            string? type = null;
            XmpTypeContainer? container = null;
            if (XmpPredefinedSchemas.TypeOf(node.NamespaceUri, node.LocalName) is { } predefinedType)
            {
                type = predefinedType;
                container = XmpTypeContainer.Predefined23;
            }
            else if (extensions.TryGetType(node.NamespaceUri, node.LocalName, out string t, out XmpTypeContainer c))
            {
                type = t;
                container = c;
            }

            bool conforms = type is null || container!.Validate(node, type);

            verdicts.Add(new XmpPropertyVerdict(
                node.NamespaceUri, node.Prefix, node.LocalName, predefined, declared, type, conforms));
        }

        return verdicts;
    }

    private static bool IsStructural(string namespaceUri) => StructuralNamespaces.Contains(namespaceUri);
}
