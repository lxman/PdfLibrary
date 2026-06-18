using FontParser;
using FontParser.Subsetting;
using PdfLibrary.Content;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Optimization;

/// <summary>
/// Subsets embedded TrueType (/FontFile2) programs to the glyphs actually used on
/// each page.  Called by <see cref="PdfOptimizer.SubsetFonts"/> when
/// <see cref="PdfOptimizationOptions.SubsetFonts"/> is true.
///
/// Supported:
///   • /Subtype /Type0, /Encoding /Identity-H or /Identity-V, descendant /CIDFontType2
///     with /FontFile2 — PRIMARY target.
///   • /Subtype /TrueType with /FontFile2 — SECONDARY; deferred if round-trip uncertain.
///
/// Skipped entirely: CFF (/FontFile3), Type1 (/FontFile), Type3, non-embedded fonts,
/// Type0 with non-Identity CMaps, sfnt fonts whose OutlineKind is CFF.
/// </summary>
internal static class FontSubsetter
{
    public static void Run(PdfDocument document, PdfOptimizationOptions options)
    {
        // 1. Collect glyph usage across all pages.
        var merged = new Dictionary<PdfStream, FontUsage>(ReferenceEqualityComparer.Instance);

        foreach (PdfPage page in document.Pages)
        {
            PdfResources? resources = page.GetResources();
            if (resources is null)
                continue;

            var collector = new GlyphUsageCollector(resources, document);
            foreach (PdfStream contentStream in page.GetContents())
            {
                byte[] decoded = contentStream.GetDecodedData(document.Decryptor);
                List<PdfOperator> ops = PdfContentParser.Parse(decoded);
                collector.ProcessOperators(ops);
            }

            foreach ((PdfStream fs, FontUsage usage) in collector.Result)
            {
                if (!merged.TryGetValue(fs, out FontUsage? existing))
                    merged[fs] = usage;
                else
                    foreach (ushort gid in usage.Gids)
                        existing.Gids.Add(gid);
            }
        }

        // 2. For each unique font-file stream, attempt subsetting.
        foreach ((PdfStream fontFile2Stream, FontUsage usage) in merged)
        {
            if (usage.Gids.Count == 0)
                continue;

            // Parse the embedded font program.
            SfntFont sfnt;
            try
            {
                byte[] raw = fontFile2Stream.GetDecodedData(document.Decryptor);
                sfnt = new SfntFont(raw);
            }
            catch
            {
                // Unrecognised font format — skip.
                continue;
            }

            // Only subset TrueType-outline fonts; skip CFF/OpenType-CFF.
            if (sfnt.OutlineKind != SfntOutlineKind.TrueType)
                continue;

            // Build the subset bytes plus the old→new GID map.
            byte[] subsetBytes;
            IReadOnlyDictionary<ushort, ushort> oldToNew;
            try
            {
                subsetBytes = TrueTypeSubsetter.Subset(sfnt, usage.Gids, out oldToNew);
            }
            catch
            {
                // Subsetter failure — skip this font to preserve the original.
                continue;
            }

            // Size guard: compare encoded sizes.  Encode the subset first so we have
            // an apples-to-apples comparison of compressed bytes.
            // We encode to a temporary buffer; only commit if size wins.
            var tempStream = new PdfStream(new byte[0]);
            tempStream.SetEncodedData(subsetBytes, "FlateDecode");

            if (tempStream.Length >= fontFile2Stream.Length)
                continue; // Subsetting didn't help — leave the original.

            // 3a. Mutate the EXISTING FontFile2 stream in place so the document
            //     object graph stays consistent (no orphaned objects, no dangling refs).
            fontFile2Stream.SetEncodedData(subsetBytes, "FlateDecode");
            fontFile2Stream.Dictionary[new PdfName("Length1")] = new PdfInteger(subsetBytes.Length);

            // 3b. For Identity-H CIDFontType2, write a /CIDToGIDMap stream.
            //     Register it as a proper document object so the serializer writes it correctly.
            if (usage.Kind == FontUsageKind.IdentityHCidType2 &&
                usage.DescendantCidFontDict is { } cidDict)
            {
                byte[] cidToGidMapBytes = BuildCidToGidMap(usage.Gids, oldToNew);
                var cidToGidStream = new PdfStream(new byte[0]);
                cidToGidStream.SetEncodedData(cidToGidMapBytes, "FlateDecode");

                // Allocate the next available object number.
                int nextObjNum = document.Objects.Count == 0 ? 1
                    : document.Objects.Keys.Max() + 1;
                document.AddObject(nextObjNum, 0, cidToGidStream);

                // Store an indirect reference so the serializer writes "N 0 R".
                cidDict[new PdfName("CIDToGIDMap")] =
                    new PdfIndirectReference(nextObjNum, 0);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Build a big-endian /CIDToGIDMap byte array.
    // Array size = (maxOldCid + 1) * 2 bytes; each 2-byte entry is the new GID
    // for that old CID (0 if the CID was not retained).
    // -------------------------------------------------------------------------

    private static byte[] BuildCidToGidMap(
        IEnumerable<ushort> retainedGids,
        IReadOnlyDictionary<ushort, ushort> oldToNew)
    {
        ushort maxOld = 0;
        foreach (ushort gid in retainedGids)
            if (gid > maxOld)
                maxOld = gid;

        int entryCount = maxOld + 1;
        byte[] map = new byte[entryCount * 2];

        foreach (ushort oldGid in retainedGids)
        {
            if (!oldToNew.TryGetValue(oldGid, out ushort newGid))
                continue; // GID dropped (shouldn't happen for Identity-H if Subset succeeded)
            int offset = oldGid * 2;
            if (offset + 1 < map.Length)
            {
                map[offset]     = (byte)(newGid >> 8);
                map[offset + 1] = (byte)(newGid & 0xFF);
            }
        }

        return map;
    }
}
