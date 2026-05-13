using System;
using System.Collections.Generic;
using System.IO;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.Codestream
{
    /// <summary>
    /// Result of parsing a J2K tile-part header (from SOT through the SOD
    /// marker). A tile-part header is a subset of the main-header markers —
    /// it carries the SOT plus any per-tile overrides of COD/QCD/etc.
    /// </summary>
    internal sealed class TilePartHeader
    {
        public SotSegment Sot { get; }
        public CodSegment? CodOverride { get; }
        public QcdSegment? QcdOverride { get; }
        public IReadOnlyList<CocSegment> CocOverrides { get; }
        public IReadOnlyList<QccSegment> QccOverrides { get; }
        public IReadOnlyList<ComSegment> Comments { get; }
        public IReadOnlyList<PptSegment> PptSegments { get; }
        public IReadOnlyList<ushort> UnparsedMarkers { get; }

        /// <summary>
        /// Codestream-relative position of the first byte of the packet stream
        /// (i.e. just after the SOD marker). The packet body extends from here
        /// for <c>Psot - (SodPosition - SotPosition)</c> bytes.
        /// </summary>
        public int PacketBodyStartPosition { get; }

        public TilePartHeader(
            SotSegment sot,
            CodSegment? codOverride,
            QcdSegment? qcdOverride,
            IReadOnlyList<CocSegment> cocOverrides,
            IReadOnlyList<QccSegment> qccOverrides,
            IReadOnlyList<ComSegment> comments,
            IReadOnlyList<PptSegment> pptSegments,
            IReadOnlyList<ushort> unparsedMarkers,
            int packetBodyStartPosition)
        {
            Sot = sot ?? throw new ArgumentNullException(nameof(sot));
            CodOverride = codOverride;
            QcdOverride = qcdOverride;
            CocOverrides = cocOverrides ?? throw new ArgumentNullException(nameof(cocOverrides));
            QccOverrides = qccOverrides ?? throw new ArgumentNullException(nameof(qccOverrides));
            Comments = comments ?? throw new ArgumentNullException(nameof(comments));
            PptSegments = pptSegments ?? throw new ArgumentNullException(nameof(pptSegments));
            UnparsedMarkers = unparsedMarkers ?? throw new ArgumentNullException(nameof(unparsedMarkers));
            PacketBodyStartPosition = packetBodyStartPosition;
        }
    }

    /// <summary>
    /// Parses a single tile-part header. The reader must be positioned at SOT
    /// (the caller usually peeks for it after <see cref="MainHeaderParser"/>
    /// hands back the cursor). <paramref name="numberOfComponents"/> comes from
    /// the SIZ segment and is needed to size Ccoc/Cqcc fields.
    /// </summary>
    internal static class TilePartHeaderParser
    {
        public static TilePartHeader Parse(CodestreamReader r, int numberOfComponents)
        {
            ushort first = r.ReadMarker();
            if (first != MarkerCode.Sot)
                throw new InvalidDataException(
                    $"Tile-part must start with SOT; got {MarkerCode.Format(first)}.");
            SotSegment sot = SotSegment.Parse(r.ReadSegment());

            CodSegment? codOverride = null;
            QcdSegment? qcdOverride = null;
            var cocs = new List<CocSegment>();
            var qccs = new List<QccSegment>();
            var coms = new List<ComSegment>();
            var ppts = new List<PptSegment>();
            var unparsed = new List<ushort>();

            while (true)
            {
                if (r.Remaining < 2)
                    throw new InvalidDataException(
                        "Tile-part header truncated: expected SOD before end of stream.");

                ushort marker = r.PeekUInt16BigEndian();
                if (marker == MarkerCode.Sod)
                {
                    r.ReadMarker(); // consume SOD
                    break;
                }

                r.ReadMarker();

                switch (marker)
                {
                    case MarkerCode.Cod:
                        if (codOverride != null)
                            throw new InvalidDataException("Tile-part has more than one COD override.");
                        codOverride = CodSegment.Parse(r.ReadSegment());
                        break;
                    case MarkerCode.Coc:
                        cocs.Add(CocSegment.Parse(r.ReadSegment(), numberOfComponents));
                        break;
                    case MarkerCode.Qcd:
                        if (qcdOverride != null)
                            throw new InvalidDataException("Tile-part has more than one QCD override.");
                        qcdOverride = QcdSegment.Parse(r.ReadSegment());
                        break;
                    case MarkerCode.Qcc:
                        qccs.Add(QccSegment.Parse(r.ReadSegment(), numberOfComponents));
                        break;
                    case MarkerCode.Com:
                        coms.Add(ComSegment.Parse(r.ReadSegment()));
                        break;
                    case MarkerCode.Ppt:
                        ppts.Add(PptSegment.Parse(r.ReadSegment()));
                        break;
                    case MarkerCode.Rgn:
                    case MarkerCode.Poc:
                    case MarkerCode.Plt:
                        unparsed.Add(marker);
                        _ = r.ReadSegment();
                        break;
                    default:
                        unparsed.Add(marker);
                        if (MarkerCode.HasSegmentLength(marker))
                            _ = r.ReadSegment();
                        break;
                }
            }

            return new TilePartHeader(
                sot, codOverride, qcdOverride,
                cocs, qccs, coms,
                ppts,
                unparsed,
                packetBodyStartPosition: r.Position);
        }
    }
}
