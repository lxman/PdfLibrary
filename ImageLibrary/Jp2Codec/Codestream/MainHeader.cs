using System;
using System.Collections.Generic;
using System.IO;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.Codestream
{
    /// <summary>
    /// Result of parsing a J2K codestream main header (everything from SOC to
    /// just before the first SOT). Holds the mandatory SIZ/COD/QCD segments
    /// and any optional COC/QCC/COM entries that appeared between them.
    ///
    /// Unknown markers in the legal Part-1 range are recorded by code so the
    /// caller can decide whether to warn or fail; markers we don't model
    /// (TLM, PLM, RGN, POC, CRG) are tolerated as opaque payloads here and
    /// decoded by higher layers once needed. PPM is captured into
    /// <see cref="PpmSegments"/> so the tile-part assembler can route
    /// packet headers from the main header into each tile-part's decode
    /// stream.
    /// </summary>
    internal sealed class MainHeader
    {
        public SizSegment Siz { get; }
        public CodSegment Cod { get; }
        public QcdSegment Qcd { get; }
        public IReadOnlyList<CocSegment> CocOverrides { get; }
        public IReadOnlyList<QccSegment> QccOverrides { get; }
        public IReadOnlyList<ComSegment> Comments { get; }
        public IReadOnlyList<PpmSegment> PpmSegments { get; }
        public IReadOnlyList<ushort> UnparsedMarkers { get; }

        /// <summary>Codestream-relative position of the first byte AFTER the main header (start of the first SOT).</summary>
        public int EndPosition { get; }

        public MainHeader(
            SizSegment siz,
            CodSegment cod,
            QcdSegment qcd,
            IReadOnlyList<CocSegment> cocOverrides,
            IReadOnlyList<QccSegment> qccOverrides,
            IReadOnlyList<ComSegment> comments,
            IReadOnlyList<PpmSegment> ppmSegments,
            IReadOnlyList<ushort> unparsedMarkers,
            int endPosition)
        {
            Siz = siz ?? throw new ArgumentNullException(nameof(siz));
            Cod = cod ?? throw new ArgumentNullException(nameof(cod));
            Qcd = qcd ?? throw new ArgumentNullException(nameof(qcd));
            CocOverrides = cocOverrides ?? throw new ArgumentNullException(nameof(cocOverrides));
            QccOverrides = qccOverrides ?? throw new ArgumentNullException(nameof(qccOverrides));
            Comments = comments ?? throw new ArgumentNullException(nameof(comments));
            PpmSegments = ppmSegments ?? throw new ArgumentNullException(nameof(ppmSegments));
            UnparsedMarkers = unparsedMarkers ?? throw new ArgumentNullException(nameof(unparsedMarkers));
            EndPosition = endPosition;
        }
    }

    /// <summary>
    /// Walks a J2K codestream main header (from SOC up to the first SOT) and
    /// collects each marker segment into a <see cref="MainHeader"/>. The walker
    /// does not interpret tile-part headers — call <see cref="TilePartHeaderParser"/>
    /// once positioned at SOT.
    /// </summary>
    internal static class MainHeaderParser
    {
        public static MainHeader Parse(CodestreamReader r)
        {
            // The first marker MUST be SOC (A.4.1).
            ushort first = r.ReadMarker();
            if (first != MarkerCode.Soc)
                throw new InvalidDataException(
                    $"Codestream does not start with SOC; first marker is {MarkerCode.Format(first)}.");

            // The second marker MUST be SIZ (A.4.1 + A.5.1).
            ushort second = r.ReadMarker();
            if (second != MarkerCode.Siz)
                throw new InvalidDataException(
                    $"SOC must be immediately followed by SIZ; got {MarkerCode.Format(second)}.");

            SizSegment siz = SizSegment.Parse(r.ReadSegment());

            CodSegment? cod = null;
            QcdSegment? qcd = null;
            var cocs = new List<CocSegment>();
            var qccs = new List<QccSegment>();
            var coms = new List<ComSegment>();
            var ppms = new List<PpmSegment>();
            var unparsed = new List<ushort>();

            while (true)
            {
                if (r.Remaining < 2)
                    throw new InvalidDataException(
                        "Main header truncated: expected SOT or further main-header marker before end of stream.");

                ushort marker = r.PeekUInt16BigEndian();
                if (marker == MarkerCode.Sot)
                    break;

                r.ReadMarker();

                switch (marker)
                {
                    case MarkerCode.Cod:
                        if (cod != null)
                            throw new InvalidDataException("Main header has more than one COD segment.");
                        cod = CodSegment.Parse(r.ReadSegment());
                        break;

                    case MarkerCode.Coc:
                        cocs.Add(CocSegment.Parse(r.ReadSegment(), siz.NumberOfComponents));
                        break;

                    case MarkerCode.Qcd:
                        if (qcd != null)
                            throw new InvalidDataException("Main header has more than one QCD segment.");
                        qcd = QcdSegment.Parse(r.ReadSegment());
                        break;

                    case MarkerCode.Qcc:
                        qccs.Add(QccSegment.Parse(r.ReadSegment(), siz.NumberOfComponents));
                        break;

                    case MarkerCode.Com:
                        coms.Add(ComSegment.Parse(r.ReadSegment()));
                        break;

                    case MarkerCode.Ppm:
                        ppms.Add(PpmSegment.Parse(r.ReadSegment()));
                        break;

                    case MarkerCode.Rgn:
                    case MarkerCode.Poc:
                    case MarkerCode.Tlm:
                    case MarkerCode.Plm:
                    case MarkerCode.Crg:
                        // Skip-but-record: the parsers for these markers live
                        // in later layers (ROI, progression-order-change, etc.).
                        // Skipping here keeps the main-header walker working
                        // against real-world files while higher layers fill in.
                        unparsed.Add(marker);
                        _ = r.ReadSegment();
                        break;

                    default:
                        // Tolerate unknown Part 1 markers by consuming their
                        // segment (if they carry a length) and recording. A
                        // stricter mode could be added later.
                        unparsed.Add(marker);
                        if (MarkerCode.HasSegmentLength(marker))
                            _ = r.ReadSegment();
                        break;
                }
            }

            if (cod is null)
                throw new InvalidDataException("Main header missing required COD segment.");
            if (qcd is null)
                throw new InvalidDataException("Main header missing required QCD segment.");

            return new MainHeader(
                siz, cod, qcd,
                cocs, qccs, coms,
                ppms,
                unparsed,
                endPosition: r.Position);
        }
    }
}
