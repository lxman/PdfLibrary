using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering;

/// <summary>
/// One parsed <c>[/Separation name alternateSpace tintTransform]</c> or
/// <c>[/DeviceN [names] alternateSpace tintTransform attributes?]</c> colour space.
///
/// <para>Before Pass 1 this shape was re-derived positionally by five separate ColorSpaceResolver
/// members, each with slightly different strictness. This parser is deliberately the PERMISSIVE union
/// of them — it accepts arrays too short to carry an alternate or tint transform, and records a DeviceN
/// name element that is not a <see cref="PdfName"/> as <c>null</c> rather than rejecting the whole
/// space. Callers keep their own strictness via <see cref="AllNamesResolved"/> and null checks, so
/// unifying the parse changes no behaviour.</para>
///
/// <para><c>Indexed</c> is deliberately NOT modelled here. The members that handle it recurse into the
/// base space themselves, which keeps that recursion where its callers can see it.</para>
/// </summary>
/// <param name="Family">"Separation" or "DeviceN".</param>
/// <param name="Names">One entry for Separation, one per colorant for DeviceN. An entry is null when
/// that element did not resolve to a name; the COUNT is always the declared colorant count.</param>
/// <param name="AlternateObject">The dereferenced alternate space object, or null when the array is
/// shorter than three elements.</param>
/// <param name="AlternateSpaceName">The alternate's family name ("DeviceCMYK", "Lab", "CalRGB", …), or
/// the empty string when absent or unrecognised.</param>
/// <param name="TintTransformObject">The dereferenced tint transform object, or null when the array is
/// shorter than four elements. Deliberately NOT a built <c>PdfFunction</c>: building one per call is
/// today's behaviour, and caching a shared instance is a thread-safety question this pass does not
/// answer (see the Pass 1 plan's scope note).</param>
/// <param name="Subtype">/Attributes /Subtype, defaulting to "DeviceN" per ISO 32000-2 Table 70.
/// Always "DeviceN" for a Separation space.</param>
/// <param name="Colorants">/Attributes /Colorants, or null. Required to be present for NChannel spaces
/// that carry spot colourants. Parsed but not yet consumed — Pass 2 (G-4) is its consumer.</param>
/// <param name="Process">/Attributes /Process, or null. Parsed but not yet consumed.</param>
internal sealed record SpotColorSpace(
    string Family,
    IReadOnlyList<string?> Names,
    PdfObject? AlternateObject,
    string AlternateSpaceName,
    PdfObject? TintTransformObject,
    string Subtype,
    PdfDictionary? Colorants,
    PdfDictionary? Process)
{
    /// <summary>True when every entry in <see cref="Names"/> resolved to a name. The members that
    /// refuse to answer for a malformed name list gate on this; the ones that need only the count
    /// (the tint-transform builders) ignore it.</summary>
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
    /// every other family (including Indexed and ICCBased), for a null object, for an array shorter
    /// than two elements, and for a Separation whose colorant name does not resolve.</summary>
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
                // Every caller requires a Separation's colorant name, so a missing one is a parse
                // failure rather than a null entry.
                if (ColorSpaceResolver.Deref(arr[1], doc) is not PdfName sepName) return false;
                names = [sepName.Value];
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

        PdfObject? altObj = arr.Count >= 3 ? ColorSpaceResolver.Deref(arr[2], doc) : null;
        string altName = altObj switch
        {
            PdfName n => n.Value,
            PdfArray { Count: >= 1 } a when a[0] is PdfName t => t.Value,
            _ => string.Empty,
        };

        PdfObject? tintObj = arr.Count >= 4 ? ColorSpaceResolver.Deref(arr[3], doc) : null;

        var subtype = "DeviceN";
        PdfDictionary? colorants = null;
        PdfDictionary? process = null;

        // /Attributes is the optional fifth element and is a DeviceN-only feature.
        if (family.Value == "DeviceN" && arr.Count >= 5
            && ColorSpaceResolver.Deref(arr[4], doc) is PdfDictionary attrs)
        {
            if (attrs.TryGetValue(new PdfName("Subtype"), out PdfObject? stObj)
                && ColorSpaceResolver.Deref(stObj!, doc) is PdfName st)
                subtype = st.Value;

            if (attrs.TryGetValue(new PdfName("Colorants"), out PdfObject? coObj))
                colorants = ColorSpaceResolver.Deref(coObj!, doc) as PdfDictionary;

            if (attrs.TryGetValue(new PdfName("Process"), out PdfObject? prObj))
                process = ColorSpaceResolver.Deref(prObj!, doc) as PdfDictionary;
        }

        space = new SpotColorSpace(family.Value, names, altObj, altName, tintObj, subtype, colorants, process);
        return true;
    }
}
