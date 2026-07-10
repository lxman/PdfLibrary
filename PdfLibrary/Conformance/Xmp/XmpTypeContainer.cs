using System.Text.RegularExpressions;

namespace PdfLibrary.Conformance.Xmp;

/// <summary>
/// The XMP value-type validator container: a recursive dispatcher from a type name (e.g. <c>"integer"</c>,
/// <c>"seq resourceevent"</c>, <c>"lang alt"</c>, <c>"resourceref"</c>) to a check on an
/// <see cref="XmpNode"/>. It is a faithful port of the reference validator's container — simple-type
/// regexes, array (bag/seq/alt) shape + element recursion, lang-alt / uri / url / date checks, and
/// structured-type field validation — assembled here into the PDF/A-2/3 predefined set in
/// <em>no closed-choice</em> mode (restricted struct fields fold to their permissive base type).
///
/// <para>Structured validators close over the container they were registered in, so a cloned container
/// (an extension schema's) resolves its custom types while predefined structs still resolve their
/// predefined fields — matching the reference's object graph. Recursion into array elements uses the
/// current container, which is at worst more permissive than the reference (never a false positive).</para>
/// </summary>
internal sealed class XmpTypeContainer
{
    private readonly Dictionary<string, Func<XmpNode, bool>> _validators;

    private XmpTypeContainer(bool empty)
    {
        _validators = new Dictionary<string, Func<XmpNode, bool>>(StringComparer.Ordinal);
        if (!empty)
            RegisterBaseSimpleTypes();
    }

    /// <summary>Clones an existing container (copies the validator map; structured validators keep
    /// their original container reference, exactly as the reference does).</summary>
    private XmpTypeContainer(XmpTypeContainer source) =>
        _validators = new Dictionary<string, Func<XmpNode, bool>>(source._validators, StringComparer.Ordinal);

    /// <summary>The predefined PDF/A-2/3 container (no closed-choice), built once.</summary>
    public static XmpTypeContainer Predefined23 { get; } = BuildPredefined23();

    /// <summary>A mutable copy of this container, for extending with extension-schema custom types.</summary>
    public XmpTypeContainer Clone() => new(this);

    // ── Validation ──────────────────────────────────────────────────────────────────────────────

    /// <summary>True when <paramref name="node"/> satisfies the value type <paramref name="typeName"/>.</summary>
    public bool Validate(XmpNode node, string typeName)
    {
        string type = Simplify(typeName);
        if (type == "any")
            return true;

        foreach ((string prefix, ArrayKind kind) in ArrayPrefixes)
            if (type.StartsWith(prefix, StringComparison.Ordinal))
                return ValidateArray(kind, node, type[prefix.Length..]);

        return _validators.TryGetValue(type, out Func<XmpNode, bool>? v) && v(node);
    }

    /// <summary>True when the container knows how to validate <paramref name="typeName"/> (used to decide
    /// whether an extension-schema-declared property registers).</summary>
    public bool IsKnownType(string typeName)
    {
        string type = Simplify(typeName);
        bool stripped;
        do
        {
            stripped = false;
            foreach ((string prefix, _) in ArrayPrefixes)
                if (type.StartsWith(prefix, StringComparison.Ordinal))
                {
                    type = type[prefix.Length..];
                    stripped = true;
                    break;
                }
        }
        while (stripped);
        return _validators.ContainsKey(type);
    }

    private bool ValidateArray(ArrayKind kind, XmpNode node, string childType)
    {
        bool shapeOk = kind switch
        {
            ArrayKind.Alt => node.IsArrayAlternate,
            ArrayKind.Seq => node.IsArrayOrdered && !node.IsArrayAlternate,
            ArrayKind.Bag => node.IsArray && !node.IsArrayOrdered && !node.IsArrayAlternate,
            _ => false,
        };
        if (!shapeOk)
            return false;
        foreach (XmpNode child in node.Children)
            if (!Validate(child, childType))
                return false;
        return true;
    }

    // ── Extension-schema registration ────────────────────────────────────────────────────────────

    /// <summary>Registers a custom simple value type (always accepts a simple node), mirroring the
    /// reference's fallback when a custom type declares no fields.</summary>
    public void RegisterSimpleText(string typeName) =>
        _validators[Simplify(typeName)] = static node => node.IsSimple;

    /// <summary>Registers a custom structured value type; the validator resolves its field types
    /// against <c>this</c> container (so nested custom types are visible).</summary>
    public void RegisterStruct(string typeName, string childNamespaceUri, IReadOnlyDictionary<string, string> fields) =>
        _validators[Simplify(typeName)] = MakeStructValidator(childNamespaceUri, fields, this);

    // ── Construction of the predefined container ─────────────────────────────────────────────────

    private static XmpTypeContainer BuildPredefined23()
    {
        var c = new XmpTypeContainer(empty: false);

        // Basic structured types.
        c.RegisterStructure("dimensions", XmpStructTypes.Dimensions);
        c.RegisterStructureWithRestricted("thumbnail", XmpStructTypes.ThumbnailBase, XmpStructTypes.ThumbnailRestricted);
        c.RegisterStructure("resourceevent", XmpStructTypes.ResourceEvent);
        c.RegisterStructure("resourceref", XmpStructTypes.ResourceRef);
        c.RegisterStructure("version", XmpStructTypes.Version);
        c.RegisterStructure("job", XmpStructTypes.Job);
        c.RegisterStructureWithRestricted("flash", XmpStructTypes.FlashBase, XmpStructTypes.FlashRestricted);
        c.RegisterStructure("oecf/sfr", XmpStructTypes.OecfSfr);
        c.RegisterStructure("cfapattern", XmpStructTypes.CfaPattern);
        c.RegisterStructure("devicesettings", XmpStructTypes.DeviceSettings);

        // PDF/A-2/3 additional structured types.
        c.RegisterStructureWithRestricted("colorant", XmpStructTypes.ColorantBase, XmpStructTypes.ColorantRestricted);
        c.RegisterStructure("font", XmpStructTypes.Font);
        c.RegisterStructure("beatsplicestretch", XmpStructTypes.BeatSpliceStretch);
        c.RegisterStructureWithRestricted("marker", XmpStructTypes.MarkerBase, XmpStructTypes.MarkerRestricted);
        c.RegisterStructure("media", XmpStructTypes.Media);
        c.RegisterStructureWithRestricted("projectlink", XmpStructTypes.ProjectLinkBase, XmpStructTypes.ProjectLinkRestricted);
        c.RegisterStructureWithRestricted("resamplestretch", XmpStructTypes.ResampleStretchBase, XmpStructTypes.ResampleStretchRestricted);
        c.RegisterStructure("time", XmpStructTypes.Time);
        c.RegisterStructureWithRestricted("timecode", XmpStructTypes.TimecodeBase, XmpStructTypes.TimecodeRestricted);
        c.RegisterStructureWithRestricted("timescalestretch", XmpStructTypes.TimeScaleStretchBase, XmpStructTypes.TimeScaleStretchRestricted);

        return c;
    }

    private void RegisterBaseSimpleTypes()
    {
        _validators["date"] = static n => n.IsSimple && XmpDate.IsValid(n.Value);
        _validators["lang alt"] = static n => n.IsArrayAltText || (n.Children.Count == 0 && n.IsArrayAlternate);
        _validators["uri"] = IsSimpleNode;
        _validators["url"] = IsSimpleNode;

        // Simple types whose value is unconstrained accept any simple node; the four restrictive ones
        // additionally match their regex (whole-string, like the reference's Matcher.matches()).
        _validators["text"] = IsSimpleNode;
        _validators["propername"] = IsSimpleNode;
        _validators["agentname"] = IsSimpleNode;
        _validators["rational"] = IsSimpleNode;
        _validators["renditionclass"] = IsSimpleNode;
        _validators["locale"] = IsSimpleNode;
        _validators["boolean"] = MakeSimpleValidator(Anchored("^True$|^False$"));
        _validators["integer"] = MakeSimpleValidator(Anchored(@"^[+-]?\d+$"));
        _validators["real"] = MakeSimpleValidator(Anchored(@"^[+-]?\d+\.?\d*|[+-]?\d*\.?\d+$"));
        _validators["mimetype"] = MakeSimpleValidator(Anchored(@"^[-\w+\.]+/[-\w+\.]+$"));
        _validators["gpscoordinate"] = MakeSimpleValidator(Anchored(@"^\d{1,3},\d{1,2}(,\d{1,2}|\.\d+)[NSEW]$"));
    }

    // {ns, name, type, name, type, …}
    private void RegisterStructure(string typeName, string[] structure)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < structure.Length; i += 2)
            fields[structure[i]] = structure[i + 1];
        _validators[typeName] = MakeStructValidator(structure[0], fields, this);
    }

    // No closed-choice mode: fold restricted fields ({name, baseType, regex, …}) in as their base type.
    private void RegisterStructureWithRestricted(string typeName, string[] structure, string[] restricted)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < structure.Length; i += 2)
            fields[structure[i]] = structure[i + 1];
        for (int i = 0; i < restricted.Length; i += 3)
            fields[restricted[i]] = restricted[i + 1];
        _validators[typeName] = MakeStructValidator(structure[0], fields, this);
    }

    private static Func<XmpNode, bool> MakeStructValidator(
        string childNamespaceUri, IReadOnlyDictionary<string, string> fields, XmpTypeContainer container) =>
        node =>
        {
            if (!node.IsStruct)
                return false;
            foreach (XmpNode child in node.Children)
            {
                if (fields.TryGetValue(child.LocalName, out string? fieldType)
                    && string.Equals(childNamespaceUri, child.NamespaceUri, StringComparison.Ordinal)
                    && container.Validate(child, fieldType))
                {
                    continue;
                }
                return false;
            }
            return true;
        };

    private static Func<XmpNode, bool> MakeSimpleValidator(Regex pattern) =>
        node => node.IsSimple && Matches(pattern, node.Value);

    // A simple node of any value (text/propername/uri/… whose content the reference does not constrain).
    private static bool IsSimpleNode(XmpNode node) => node.IsSimple;

    private static bool Matches(Regex pattern, string? value)
    {
        if (value is null)
            return false;
        return pattern.IsMatch(value) || pattern.IsMatch(value.Trim());
    }

    private static Regex Anchored(string javaRegex) =>
        new(@"\A(?:" + javaRegex + @")\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ── Type-name normalization (mirrors the reference getSimplifiedType) ─────────────────────────

    private enum ArrayKind { Bag, Seq, Alt }

    private static readonly (string Prefix, ArrayKind Kind)[] ArrayPrefixes =
        { ("bag ", ArrayKind.Bag), ("seq ", ArrayKind.Seq), ("alt ", ArrayKind.Alt) };

    private static readonly Regex ChoiceNoise =
        new(@"(open |closed )?(choice |choice$)(of )?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string Simplify(string type)
    {
        string res = ChoiceNoise.Replace(type.ToLowerInvariant(), string.Empty).Trim();
        if (res.Length == 0)
            return "text";
        if (res.EndsWith("lang alt", StringComparison.Ordinal))
            return res;
        foreach (string arr in new[] { "bag", "seq", "alt" })
            if (res.EndsWith(arr, StringComparison.Ordinal))
                return res + " text";
        return res;
    }
}
