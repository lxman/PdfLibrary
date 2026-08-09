using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts;

/// <summary>What kind of font a <see cref="FontInventoryEntry"/> describes.</summary>
public enum FontKind { Type1, TrueType, Type0CidType0, Type0CidType2, Type3, Unknown }

/// <summary>
/// One font a document references for rendering, and the state remediation cares about.
///
/// <para><paramref name="Id"/> is the LOGICAL font — the Type0 wrapper, or the simple font
/// dictionary. <paramref name="ProgramHolderId"/> is where the program and descriptor live — the
/// descendant CIDFont, or the same dictionary for a simple font. They differ for composite fonts,
/// and operations must target by ROLE: /ToUnicode belongs on the logical font, /FontFile* and
/// /FontDescriptor on the program holder. A /ToUnicode written onto a descendant CIDFont is
/// syntactically valid and no viewer will read it.</para>
/// </summary>
public sealed record FontInventoryEntry(
    FontId Id,
    FontId? ProgramHolderId,
    string BaseFont,
    string? SubsetTag,
    string FamilyName,
    FontKind Kind,
    bool IsEmbedded,
    bool HasToUnicode,
    bool HasWidths,
    bool IsAddressable,
    IReadOnlyList<int> UsedCodes,
    IReadOnlyList<int> PagesUsedOn);

/// <summary>
/// Reads the document's referenced fonts and their remediation-relevant state. Built on
/// <see cref="ReferencedFontWalker"/> — the SAME walk the conformance rules use — so the inventory
/// and the findings cannot disagree about which fonts exist.
/// </summary>
public static class FontInventory
{
    private static readonly PdfName[] FontFileKeys =
        [new PdfName("FontFile"), new PdfName("FontFile2"), new PdfName("FontFile3")];

    /// <summary>Every font the document references for rendering, one entry per logical font.</summary>
    public static IReadOnlyList<FontInventoryEntry> Read(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var context = new ConformanceContext(document, ConformanceProfile.PdfA2b);
        IReadOnlyList<PdfDictionary> referenced = context.ReferencedFonts;

        // Descendant CIDFonts appear in the walk in their own right. They are not logical fonts —
        // they are the program holder of the Type0 that precedes them — so collect them first and
        // exclude them from the top-level pass. Keyed on dictionary IDENTITY, not object number:
        // ISO 32000-1 Table 121 requires /DescendantFonts to be an array but does not require its
        // element be an indirect reference, and PdfDictionary carries no Equals/GetHashCode override
        // (see PdfObject), so reference equality is exactly what Dictionary<PdfDictionary,_> gives by
        // default — explicit here so that stays true even if that assumption ever changes. Keying on
        // object number instead would silently let a direct descendant (ObjectNumber 0, IsIndirect
        // false) through unexcluded, since ObjectNumber alone cannot distinguish it from every OTHER
        // direct dictionary.
        var descendantOf = new Dictionary<PdfDictionary, PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (PdfDictionary dict in referenced)
        {
            if (Name(document, dict.Get("Subtype")) != "Type0") continue;
            if (Resolve(document, dict.Get("DescendantFonts")) is not PdfArray descendants
                || descendants.Count == 0) continue;
            if (Resolve(document, descendants[0]) is not PdfDictionary descendant) continue;
            descendantOf[descendant] = dict;
        }

        Dictionary<PdfDictionary, (List<int> Codes, SortedSet<int> Pages)> usage = CollectUsage(context);

        var entries = new List<FontInventoryEntry>();
        foreach (PdfDictionary dict in referenced)
        {
            if (descendantOf.ContainsKey(dict))
                continue; // reached as the program holder of its Type0 parent

            entries.Add(BuildEntry(document, dict, usage));
        }
        return entries;
    }

    /// <summary>
    /// The entry a given object number belongs to, matching EITHER the logical font or its program
    /// holder. This is what lets a finding from any rule land on the right row: FontEmbeddingRule
    /// names the descendant CIDFont while FontProgramRule names the font dictionary it parsed.
    /// </summary>
    public static FontInventoryEntry? Find(IReadOnlyList<FontInventoryEntry> inventory, int objectNumber)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        foreach (FontInventoryEntry entry in inventory)
        {
            if (entry.Id.ObjectNumber == objectNumber) return entry;
            if (entry.ProgramHolderId?.ObjectNumber == objectNumber) return entry;
        }
        return null;
    }

    private static FontInventoryEntry BuildEntry(
        PdfDocument document,
        PdfDictionary dict,
        Dictionary<PdfDictionary, (List<int> Codes, SortedSet<int> Pages)> usage)
    {
        string subtype = Name(document, dict.Get("Subtype")) ?? "";
        PdfDictionary programHolder = dict;

        if (subtype == "Type0"
            && Resolve(document, dict.Get("DescendantFonts")) is PdfArray descendants
            && descendants.Count > 0
            && Resolve(document, descendants[0]) is PdfDictionary descendant)
        {
            programHolder = descendant;
        }

        string baseFont = (Resolve(document, dict.Get("BaseFont")) as PdfName)?.Value ?? "Unknown";
        string? subsetTag = SubsetTag(baseFont);

        (List<int> codes, SortedSet<int> pages) =
            usage.TryGetValue(dict, out (List<int>, SortedSet<int>) u) ? u : ([], []);

        return new FontInventoryEntry(
            Id: new FontId(dict.IsIndirect ? dict.ObjectNumber : 0),
            ProgramHolderId: programHolder.IsIndirect ? new FontId(programHolder.ObjectNumber) : null,
            BaseFont: baseFont,
            SubsetTag: subsetTag,
            FamilyName: subsetTag is null ? baseFont : baseFont[7..],
            Kind: KindOf(document, subtype, programHolder),
            IsEmbedded: IsEmbedded(document, programHolder),
            HasToUnicode: dict.Get("ToUnicode") is not null,
            HasWidths: dict.Get("Widths") is not null || programHolder.Get("W") is not null,
            IsAddressable: dict.IsIndirect && programHolder.IsIndirect,
            UsedCodes: codes,
            PagesUsedOn: pages.ToList());
    }

    private static Dictionary<PdfDictionary, (List<int> Codes, SortedSet<int> Pages)> CollectUsage(
        ConformanceContext context)
    {
        var result = new Dictionary<PdfDictionary, (List<int>, SortedSet<int>)>();
        foreach (UsedFontCodes used in context.UsedTextGlyphs)
        {
            PdfDictionary dict = used.Font.FontDictionary;
            if (!result.TryGetValue(dict, out (List<int> Codes, SortedSet<int> Pages) entry))
                result[dict] = entry = ([], []);
            entry.Codes.AddRange(used.Codes);
        }
        // Page attribution comes from ConformanceContext.FontPagesUsed — the SAME per-page
        // content-stream walk that produced UsedTextGlyphs above, captured before that walk merges
        // codes across pages and discards which page each one came from. A resource-presence scan
        // (an earlier version of this method used context.Pages[i].GetResources()?.GetFonts()) both
        // under-reports — misses a font only reachable through a Form XObject, tiling pattern or
        // annotation appearance a page-level-only scan does not see — and over-reports — counts a
        // font that sits, unused, in a page's (or an inherited) resource dictionary. FontPagesUsed
        // avoids both because it only records a page where a character was actually drawn.
        foreach ((PdfDictionary dict, IReadOnlyList<int> pages) in context.FontPagesUsed)
        {
            if (!result.TryGetValue(dict, out (List<int> Codes, SortedSet<int> Pages) entry))
                result[dict] = entry = ([], []);
            entry.Pages.UnionWith(pages);
        }
        return result;
    }

    private static FontKind KindOf(PdfDocument document, string subtype, PdfDictionary programHolder) =>
        subtype switch
        {
            "Type1" or "MMType1" => FontKind.Type1,
            "TrueType" => FontKind.TrueType,
            "Type3" => FontKind.Type3,
            "Type0" => Name(document, programHolder.Get("Subtype")) == "CIDFontType0"
                ? FontKind.Type0CidType0
                : FontKind.Type0CidType2,
            _ => FontKind.Unknown,
        };

    private static bool IsEmbedded(PdfDocument document, PdfDictionary programHolder)
    {
        if (Resolve(document, programHolder.Get("FontDescriptor")) is not PdfDictionary descriptor)
            return false;
        foreach (PdfName key in FontFileKeys)
            if (descriptor.Get(key.Value) is not null)
                return true;
        return false;
    }

    /// <summary>The 6-uppercase-letter subset tag, or null. Mirrors <see cref="PdfFont.IsSubsetFont"/>.</summary>
    private static string? SubsetTag(string baseFont)
    {
        if (baseFont.Length <= 7 || baseFont[6] != '+') return null;
        for (var i = 0; i < 6; i++)
            if (baseFont[i] is < 'A' or > 'Z')
                return null;
        return baseFont[..6];
    }

    private static PdfObject? Resolve(PdfDocument document, PdfObject? obj) =>
        obj is PdfIndirectReference reference ? document.ResolveReference(reference) : obj;

    private static string? Name(PdfDocument document, PdfObject? obj) =>
        (Resolve(document, obj) as PdfName)?.Value;
}
