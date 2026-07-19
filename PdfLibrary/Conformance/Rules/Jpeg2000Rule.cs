using System.Linq;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A (ISO 19005-2/3, clause 6.2.8.3): constraints on the JPEG2000 data of a JPXDecode image, mirroring
/// veraPDF's <c>JPEG2000</c> rules. Per image: the colour-channel count is 1, 3, or 4 (test 1); if there is
/// more than one colour-space specification, exactly one carries the best-fidelity APPROX field 0x01
/// (test 2); the colr box METH is 1, 2, or 3 (test 3); enumerated colour space 19 (CIEJab) is not used
/// (test 4); and the bit depth is in [1, 38] and uniform across channels, i.e. no <c>bpcc</c> box (test 5).
/// Tests 2–4 are escaped when the image XObject carries an explicit <c>/ColorSpace</c>, which overrides the
/// JPEG2000 internal colour specification.
///
/// The rule reads the JP2 wrapper boxes directly from the image's raw (still JPXDecode-encoded) stream
/// bytes with a defensive reader that never throws and returns nothing for data it cannot parse as a JP2
/// box structure (a raw J2K codestream, a chained filter, or truncated data) — so it only ever
/// under-reports, keeping the preflighter a strict subset of the reference validator.
/// </summary>
internal sealed class Jpeg2000Rule : IConformanceRule
{
    public string RuleId => "jpeg2000";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (PdfStream stream in context.Streams)
        {
            if (context.ResolveName(stream.Dictionary.Get("Subtype")) != "Image")
                continue;
            if (!HasJpxFilter(context, stream.Dictionary))
                continue;

            Jp2Info? info = Jp2BoxReader.Read(stream.Data);
            if (info is null)
                continue; // not a parseable JP2 box structure — skip (never a false positive)

            bool hasColorSpace = stream.Dictionary.Get("ColorSpace") is not null;
            string? violation = Evaluate(info, hasColorSpace);
            if (violation is null)
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.2.8.3"),
                Message = $"A JPEG2000 image violates PDF/A: {violation}.",
                ObjectNumber = stream.IsIndirect ? stream.ObjectNumber : null,
            };
        }
    }

    private static string? Evaluate(Jp2Info info, bool hasColorSpace)
    {
        // Test 1 — colour channels (about the image data, so no /ColorSpace escape).
        if (info.NumComponents is { } nc && nc != 1 && nc != 3 && nc != 4)
            return $"the JPEG2000 data has {nc} colour channels, which is neither 1, 3, nor 4";

        // Test 5 — a bpcc box signals per-channel bit depths; otherwise the single ihdr depth must be in range.
        if (info.BpccPresent)
            return "the JPEG2000 channels do not all share one bit depth (a 'bpcc' box is present)";
        if (info.BitDepth is { } bd && (bd < 1 || bd > 38))
            return $"the JPEG2000 bit depth is {bd}, outside the permitted range of 1 to 38";

        // Tests 2–4 govern the internal colour specification, which an explicit /ColorSpace overrides.
        if (!hasColorSpace && info.Colrs.Count > 0)
        {
            // Test 2 — with more than one colour spec, exactly one must carry APPROX 0x01.
            if (info.Colrs.Count > 1 && info.Colrs.Count(c => c.Approx == 1) != 1)
                return $"the JPEG2000 data has {info.Colrs.Count} colour specifications but not exactly one "
                     + "with the best-fidelity APPROX field (0x01)";

            // Tests 3 & 4 read METH/EnumCS from the colour spec a reader actually uses: the first box with
            // APPROX 0x01 (best fidelity), else the sole box. With several boxes and none marked APPROX 0x01
            // the reference validator leaves METH/EnumCS unset (and test 2 above already flags that case), so
            // there is no single spec to judge here.
            List<ColrSpec> approxBoxes = info.Colrs.Where(c => c.Approx == 1).ToList();
            ColrSpec? primary = approxBoxes.Count > 0 ? approxBoxes[0]
                : info.Colrs.Count == 1 ? info.Colrs[0]
                : null;

            if (primary is { } spec)
            {
                // Test 3 — the colr box METH must be 1, 2, or 3.
                if (spec.Meth is < 1 or > 3)
                    return $"the JPEG2000 'colr' box METH value is {spec.Meth}, which is not 1, 2, or 3";
                // Test 4 — enumerated colour space 19 (CIEJab) is forbidden.
                if (spec.Meth == 1 && spec.EnumCs == 19)
                    return "the JPEG2000 data uses enumerated colour space 19 (CIEJab), which PDF/A forbids";
            }
        }

        return null;
    }

    private static bool HasJpxFilter(ConformanceContext context, PdfDictionary dict) =>
        context.Resolve(dict.Get("Filter")) switch
        {
            PdfName name => name.Value == "JPXDecode",
            PdfArray filters => filters.Any(f => (context.Resolve(f) as PdfName)?.Value == "JPXDecode"),
            _ => false,
        };

    /// <summary>The JP2 fields clause 6.2.8.3 constrains, extracted from the wrapper boxes.</summary>
    private sealed record Jp2Info(int? NumComponents, int? BitDepth, bool BpccPresent, IReadOnlyList<ColrSpec> Colrs);

    /// <summary>One JPEG2000 <c>colr</c> box: its method, APPROX field, and enumerated colour space (METH 1 only).</summary>
    private readonly record struct ColrSpec(int Meth, int Approx, long? EnumCs);

    /// <summary>
    /// A defensive reader for the JP2 wrapper (ISO/IEC 15444-1). It validates the JP2 signature box, finds
    /// the <c>jp2h</c> super-box, and extracts <c>ihdr</c> (channel count, bit depth), every <c>colr</c>
    /// box, and whether a <c>bpcc</c> box is present. Every access is bounds-checked; anything it cannot
    /// parse yields null (or an absent field), never an exception.
    /// </summary>
    private static class Jp2BoxReader
    {
        public static Jp2Info? Read(byte[] data)
        {
            List<(string Type, int Start, int Len)> top = Boxes(data, 0, data.Length);

            // Require a JP2 signature box (type 'jP  ', content 0x0D0A870A) to confirm this is a boxed JP2
            // and not a raw J2K codestream or an unrelated payload.
            if (top.Count == 0 || top[0].Type != "jP  " || top[0].Len != 4
                || ReadU32(data, top[0].Start) != 0x0D0A870A)
            {
                return null;
            }

            int jp2hIndex = top.FindIndex(b => b.Type == "jp2h");
            if (jp2hIndex < 0)
                return null;

            (_, int jp2hStart, int jp2hLen) = top[jp2hIndex];
            int? nc = null, bitDepth = null;
            bool bpcc = false;
            var colrs = new List<ColrSpec>();

            foreach ((string type, int start, int len) in Boxes(data, jp2hStart, jp2hStart + jp2hLen))
            {
                if (type == "ihdr" && len >= 11) // HEIGHT(4) WIDTH(4) NC(2) BPC(1) …
                {
                    nc = ReadU16(data, start + 8);
                    bitDepth = (data[start + 10] & 0x7F) + 1; // BPC encodes (bit depth − 1); high bit = signed
                }
                else if (type == "bpcc")
                {
                    bpcc = true;
                }
                else if (type == "colr")
                {
                    // A colr box too short for its fields makes the reference validator abort the header
                    // parse; mirror that so the colr count cannot diverge (a truncated box would otherwise
                    // be counted here but not there). A <3-byte box is dropped before it is counted.
                    if (len < 3)
                        break;
                    int meth = data[start];            // METH(1) PREC(1) APPROX(1) [ EnumCS(4) | ICC ]
                    int approx = data[start + 2];
                    long? enumCs = meth == 1 && len >= 7 ? ReadU32(data, start + 3) : null;
                    colrs.Add(new ColrSpec(meth, approx, enumCs));
                    if (meth == 1 && len < 7)
                        break; // METH-1 box lacking its 4-byte EnumCS: counted, then the walk stops
                }
            }

            return new Jp2Info(nc, bitDepth, bpcc, colrs);
        }

        /// <summary>Enumerates the boxes wholly contained in <c>[start, end)</c>, stopping at the first
        /// malformed or truncated length rather than throwing.</summary>
        private static List<(string Type, int Start, int Len)> Boxes(byte[] d, int start, int end)
        {
            var boxes = new List<(string, int, int)>();
            int p = start;
            while (p + 8 <= end)
            {
                long len = ReadU32(d, p);
                int header = 8;
                if (len == 1) // 64-bit extended length
                {
                    if (p + 16 > end) break;
                    len = ReadU64(d, p + 8);
                    header = 16;
                }
                else if (len == 0) // extends to the end of the enclosing range
                {
                    len = end - p;
                }
                if (len < header || p + len > end)
                    break; // truncated or nonsensical length

                boxes.Add((Latin1(d, p + 4, 4), p + header, (int)(len - header)));
                p += (int)len;
            }
            return boxes;
        }

        private static uint ReadU32(byte[] d, int p) =>
            (uint)((d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]);

        private static long ReadU64(byte[] d, int p) => ((long)ReadU32(d, p) << 32) | ReadU32(d, p + 4);

        private static int ReadU16(byte[] d, int p) => (d[p] << 8) | d[p + 1];

        private static string Latin1(byte[] d, int p, int n)
        {
            var chars = new char[n];
            for (int i = 0; i < n; i++) chars[i] = (char)d[p + i];
            return new string(chars);
        }
    }
}
