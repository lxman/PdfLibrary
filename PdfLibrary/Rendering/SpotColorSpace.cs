using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering;

/// <summary>
/// One parsed <c>[/Separation name alternateSpace tintTransform]</c> or
/// <c>[/DeviceN [names] alternateSpace tintTransform attributes?]</c> colour space.
///
/// <para>Before Pass 1 this shape was re-derived positionally by five separate ColorSpaceResolver
/// members, each with slightly different strictness. This parser is the PERMISSIVE union of them — it
/// accepts arrays too short to carry an alternate or tint transform, and records a Separation or
/// DeviceN name element that is not a <see cref="PdfName"/> as <c>null</c> rather than rejecting the
/// whole space. Callers keep their own strictness via <see cref="AllNamesResolved"/> and null/count
/// checks, so unifying the parse changes no behaviour — this is the property the whole task exists to
/// protect; see <see cref="TryParse"/> for the specific cases that make it true.</para>
///
/// <para><c>Indexed</c> is deliberately NOT modelled here. The members that handle it recurse into the
/// base space themselves, which keeps that recursion where its callers can see it.</para>
///
/// <para><b>The alternate space, tint transform, and /Attributes members are resolved LAZILY, on first
/// property access, and cached from then on.</b> <see cref="Family"/> and <see cref="Names"/> are
/// parsed eagerly in <see cref="TryParse"/> because every caller needs them just to decide "is this
/// /None" or "which plates does this mark". The other five members are read by only SOME callers (the
/// tint-ramp builders) — and ISO 32000-2 §8.6.6.4 row 4-10 requires that for /All and /None "the
/// alternateSpace and tintTransform shall be ignored", i.e. never even looked at. Pre-Pass-1,
/// <see cref="ColorSpaceResolver.PaintsNothing(PdfObject?,PdfDocument?)"/> and
/// <see cref="ColorSpaceResolver.PlatesForColorSpaceObject"/> only ever read element 1 of the source
/// array. Eagerly dereferencing elements 2-4 here — as an earlier version of this class did — would
/// (a) fetch and parse indirect objects those callers never used to touch, so a corrupt
/// alternate/tint/attributes object newly throws <c>PdfParseException</c> out of a render path that
/// used to render fine, and (b) violate the §8.6.6.4 "ignored" rule this file itself quotes elsewhere
/// (see <c>ColorSpaceResolver.BuildTintToRgb</c>). Do NOT "tidy" these back into eager positional
/// record members — that reintroduces both problems.</para>
/// </summary>
internal sealed record SpotColorSpace
{
    private readonly PdfArray _source;
    private readonly PdfDocument? _doc;

    private bool _alternateComputed;
    private PdfObject? _alternateObject;
    private string _alternateSpaceName = string.Empty;

    private bool _tintComputed;
    private PdfObject? _tintTransformObject;

    private bool _attributesComputed;
    private string _subtype = "DeviceN";
    private PdfDictionary? _colorants;
    private PdfDictionary? _process;

    private SpotColorSpace(string family, IReadOnlyList<string?> names, PdfArray source, PdfDocument? doc)
    {
        Family = family;
        Names = names;
        _source = source;
        _doc = doc;
    }

    /// <summary>"Separation" or "DeviceN".</summary>
    public string Family { get; }

    /// <summary>One entry for Separation, one per colorant for DeviceN. An entry is null when
    /// that element did not resolve to a name; the COUNT is always the declared colorant count.</summary>
    public IReadOnlyList<string?> Names { get; }

    /// <summary>The dereferenced alternate space object, or null when the array is shorter than three
    /// elements. Resolved lazily on first access and cached — see the class remarks.</summary>
    public PdfObject? AlternateObject
    {
        get { EnsureAlternate(); return _alternateObject; }
    }

    /// <summary>The alternate's family name ("DeviceCMYK", "Lab", "CalRGB", …), or the empty string
    /// when absent or unrecognised. Resolved lazily on first access and cached — see the class
    /// remarks.</summary>
    public string AlternateSpaceName
    {
        get { EnsureAlternate(); return _alternateSpaceName; }
    }

    /// <summary>The dereferenced tint transform object, or null when the array is shorter than four
    /// elements. Deliberately NOT a built <c>PdfFunction</c>: building one per call is today's
    /// behaviour, and caching a shared instance is a thread-safety question this pass does not answer
    /// (see the Pass 1 plan's scope note). Resolved lazily on first access and cached — see the class
    /// remarks.</summary>
    public PdfObject? TintTransformObject
    {
        get { EnsureTint(); return _tintTransformObject; }
    }

    /// <summary>True when the source array has at least four elements, i.e. a tint transform element
    /// is present. This is the deref-free way to ask the arity question that <c>TintTransformObject is
    /// null</c> would otherwise answer at the cost of resolving element 3 via <c>EnsureTint</c> (which
    /// is normally an indirect stream object — a corrupt one throws <c>PdfParseException</c> out of
    /// that dereference). Callers that only need to preserve the pre-Pass-1 "Count >= 4" rule — without
    /// triggering that dereference — must read this property, not <see cref="TintTransformObject"/>.
    /// </summary>
    internal bool HasTintTransform => _source.Count >= 4;

    /// <summary>/Attributes /Subtype, defaulting to "DeviceN" per ISO 32000-2 Table 70. Always
    /// "DeviceN" for a Separation space. Resolved lazily on first access and cached — see the class
    /// remarks.</summary>
    public string Subtype
    {
        get { EnsureAttributes(); return _subtype; }
    }

    /// <summary>/Attributes /Colorants, or null. Required to be present for NChannel spaces that carry
    /// spot colourants. Parsed but not yet consumed — Pass 2 (G-4) is its consumer. Resolved lazily on
    /// first access and cached — see the class remarks.</summary>
    public PdfDictionary? Colorants
    {
        get { EnsureAttributes(); return _colorants; }
    }

    /// <summary>/Attributes /Process, or null. Parsed but not yet consumed. Resolved lazily on first
    /// access and cached — see the class remarks.</summary>
    public PdfDictionary? Process
    {
        get { EnsureAttributes(); return _process; }
    }

    /// <summary>True when every entry in <see cref="Names"/> resolved to a name. The members that
    /// refuse to answer for a malformed name list gate on this; the ones that need only the count
    /// (the tint-transform builders) ignore it.
    ///
    /// <para>Vacuously TRUE for an empty <see cref="Names"/> list — a DeviceN with a zero-length names
    /// array parses successfully with <c>Names.Count == 0</c> and <c>AllNamesResolved == true</c>, even
    /// though every current ColorSpaceResolver member rejects that array outright. A caller migrating
    /// "every component is /None"-style logic onto this record must check <c>Names.Count == 0</c>
    /// separately; this property alone does not distinguish "no colorants" from "all colorants
    /// resolved".</para></summary>
    internal bool AllNamesResolved
    {
        get
        {
            for (var i = 0; i < Names.Count; i++)
                if (Names[i] is null)
                    return false;
            return true;
        }
    }

    /// <summary>ISO 32000-2 §8.6.6.5: NChannel spaces evaluate their components individually. Nothing
    /// consumes this yet — Pass 2 does.</summary>
    internal bool IsNChannel => Subtype == "NChannel";

    /// <summary>Parses a colour-space object into a <see cref="SpotColorSpace"/>. Returns false for
    /// every other family (including Indexed and ICCBased), for a null object, and for an array
    /// shorter than two elements. Does NOT reject a Separation whose colorant name fails to resolve to
    /// a <see cref="PdfName"/>, nor a DeviceN whose names array is empty — both parse successfully,
    /// with the caller responsible for its own strictness via <see cref="AllNamesResolved"/> and
    /// <c>Names.Count</c> (which is vacuously "all resolved" for an empty DeviceN names array; see
    /// <see cref="AllNamesResolved"/>).
    ///
    /// <para>Only <see cref="Family"/> and <see cref="Names"/> are computed here — element 1 is all
    /// that <c>PaintsNothing</c> and <c>PlatesForColorSpaceObject</c> read pre-Pass-1. Elements 2-4
    /// (alternate, tint transform, /Attributes) are resolved lazily; see the class remarks.</para>
    /// </summary>
    internal static bool TryParse(PdfObject? csObj, PdfDocument? doc, out SpotColorSpace? space)
    {
        space = null;
        if (csObj is null) return false;

        PdfObject resolved = ColorSpaceResolver.Deref(csObj, doc);
        if (resolved is not PdfArray { Count: >= 2 } arr || arr[0] is not PdfName family)
            return false;

        List<string?> names;
        switch (family.Value)
        {
            case "Separation":
                // BuildTintToRgb/BuildTintToCmyk set inputComponents = 1 for a Separation without ever
                // requiring element 1 to be a name (they deref it only to test for /All), so a
                // non-name colorant must still parse — rejecting it here would be unrecoverable at
                // those call sites once they migrate onto TryParse. Mirror the DeviceN entry: null when
                // unresolved, count always 1.
                names = [ColorSpaceResolver.Deref(arr[1], doc) is PdfName sepName ? sepName.Value : null];
                break;

            case "DeviceN":
                if (ColorSpaceResolver.Deref(arr[1], doc) is not PdfArray namesArr) return false;
                names = new List<string?>(namesArr.Count);
                foreach (PdfObject nameObj in namesArr)
                    names.Add(ColorSpaceResolver.Deref(nameObj, doc) is PdfName n ? n.Value : null);
                break;

            default:
                return false;
        }

        space = new SpotColorSpace(family.Value, names, arr, doc);
        return true;
    }

    private void EnsureAlternate()
    {
        if (_alternateComputed) return;
        _alternateComputed = true;

        PdfObject? altObj = _source.Count >= 3 ? ColorSpaceResolver.Deref(_source[2], _doc) : null;
        _alternateObject = altObj;
        _alternateSpaceName = altObj switch
        {
            PdfName n => n.Value,
            PdfArray { Count: >= 1 } a when a[0] is PdfName t => t.Value,
            _ => string.Empty,
        };
    }

    private void EnsureTint()
    {
        if (_tintComputed) return;
        _tintComputed = true;

        _tintTransformObject = _source.Count >= 4 ? ColorSpaceResolver.Deref(_source[3], _doc) : null;
    }

    private void EnsureAttributes()
    {
        if (_attributesComputed) return;
        _attributesComputed = true;

        // /Attributes is the optional fifth element and is a DeviceN-only feature.
        if (Family != "DeviceN" || _source.Count < 5) return;
        if (ColorSpaceResolver.Deref(_source[4], _doc) is not PdfDictionary attrs) return;

        if (attrs.TryGetValue(new PdfName("Subtype"), out PdfObject? stObj)
            && ColorSpaceResolver.Deref(stObj!, _doc) is PdfName st)
            _subtype = st.Value;

        if (attrs.TryGetValue(new PdfName("Colorants"), out PdfObject? coObj))
            _colorants = ColorSpaceResolver.Deref(coObj!, _doc) as PdfDictionary;

        if (attrs.TryGetValue(new PdfName("Process"), out PdfObject? prObj))
            _process = ColorSpaceResolver.Deref(prObj!, _doc) as PdfDictionary;
    }
}
