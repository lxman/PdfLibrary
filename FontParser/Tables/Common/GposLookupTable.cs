using System;
using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common.ChainedSequenceContext.Format1;
using FontParser.Tables.Common.ChainedSequenceContext.Format2;
using FontParser.Tables.Common.ChainedSequenceContext.Format3;
using FontParser.Tables.Common.SequenceContext.Format1;
using FontParser.Tables.Common.SequenceContext.Format2;
using FontParser.Tables.Common.SequenceContext.Format3;
using FontParser.Tables.Gpos.LookupSubtables;
using FontParser.Tables.Gpos.LookupSubtables.PairPos;

namespace FontParser.Tables.Common
{
    public class GposLookupTable
    {
        public ushort? MarkFilteringSet { get; }

        public List<ILookupSubTable> SubTables { get; } = new List<ILookupSubTable>();

        public GposLookupTable(BigEndianReader reader)
        {
            long lookupTableStart = reader.Position;
            var lookupType = (GposLookupType)reader.ReadUShort();
            var lookupFlags = (LookupFlag)reader.ReadUShort();
            ushort subTableCount = reader.ReadUShort();
            ushort[] subTableOffsets = reader.ReadUShortArray(subTableCount);
            if (lookupFlags.HasFlag(LookupFlag.UseMarkFilteringSet))
            {
                MarkFilteringSet = reader.ReadUShort();
            }
            for (var i = 0; i < subTableCount; i++)
            {
                reader.Seek(subTableOffsets[i] + lookupTableStart);
                switch (lookupType)
                {
                    case GposLookupType.SingleAdjustment:
                        SubTables.Add(new SinglePos(reader));
                        break;

                    case GposLookupType.PairAdjustment:
                        byte version = reader.PeekBytes(2)[1];
                        switch (version)
                        {
                            case 1:
                                SubTables.Add(new PairPosFormat1(reader));
                                break;

                            case 2:
                                SubTables.Add(new PairPosFormat2(reader));
                                break;
                        }
                        break;

                    case GposLookupType.CursiveAttachment:
                        SubTables.Add(new Gpos.LookupSubtables.CursivePos.CursivePosFormat1(reader));
                        break;

                    case GposLookupType.MarkToBaseAttachment:
                        SubTables.Add(new Gpos.LookupSubtables.MarkBasePos.MarkBasePosFormat1(reader));
                        break;

                    case GposLookupType.MarkToLigatureAttachment:
                        SubTables.Add(new Gpos.LookupSubtables.MarkLigPos.MarkLigPosFormat1(reader));
                        break;

                    case GposLookupType.MarkToMarkAttachment:
                        SubTables.Add(new Gpos.LookupSubtables.MarkMarkPos.MarkMarkPosFormat1(reader));
                        break;

                    case GposLookupType.ContextPositioning:
                        byte format = reader.PeekBytes(2)[1];
                        switch (format)
                        {
                            case 1:
                                SubTables.Add(new SequenceContextFormat1(reader));
                                break;

                            case 2:
                                SubTables.Add(new SequenceContextFormat2(reader));
                                break;

                            case 3:
                                SubTables.Add(new SequenceContextFormat3(reader));
                                break;
                        }
                        break;

                    case GposLookupType.ChainedContextPositioning:
                        format = reader.PeekBytes(2)[1];
                        switch (format)
                        {
                            case 1:
                                SubTables.Add(new ChainedSequenceContextFormat1(reader));
                                break;

                            case 2:
                                SubTables.Add(new ChainedSequenceContextFormat2(reader));
                                break;

                            case 3:
                                SubTables.Add(new ChainedSequenceContextFormat3(reader));
                                break;
                        }
                        break;

                    case GposLookupType.PositioningExtension:
                        SubTables.Add(new PosExtensionFormat1(reader));
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }
}