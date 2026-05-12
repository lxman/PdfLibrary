using System;
using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Common.ChainedSequenceContext.Format1;
using FontParser.Tables.Common.ChainedSequenceContext.Format2;
using FontParser.Tables.Common.ChainedSequenceContext.Format3;
using FontParser.Tables.Common.SequenceContext.Format1;
using FontParser.Tables.Common.SequenceContext.Format2;
using FontParser.Tables.Common.SequenceContext.Format3;
using FontParser.Tables.Gsub.LookupSubTables.AlternateSubstitution;
using FontParser.Tables.Gsub.LookupSubTables.LigatureSubstitution;
using FontParser.Tables.Gsub.LookupSubTables.MultipleSubstitution;
using FontParser.Tables.Gsub.LookupSubTables.ReverseChainSingleSubstitution;
using FontParser.Tables.Gsub.LookupSubTables.SingleSubstitution;
using FontParser.Tables.Gsub.LookupSubTables.SubstitutionExtension;

namespace FontParser.Tables.Common
{
    public class GsubLookupTable
    {
        public List<ILookupSubTable> SubTables { get; } = new List<ILookupSubTable>();

        public GsubLookupTable(BigEndianReader reader)
        {
            long startOfTable = reader.Position;
            var lookupType = (GsubLookupType)reader.ReadUShort();
            var lookupFlags = (LookupFlag)reader.ReadUShort();
            ushort subTableCount = reader.ReadUShort();
            ushort[] subTableOffsets = reader.ReadUShortArray(subTableCount);
            ushort? markFilteringSet = null;
            if (lookupFlags.HasFlag(LookupFlag.UseMarkFilteringSet))
            {
                markFilteringSet = reader.ReadUShort();
            }
            for (var i = 0; i < subTableCount; i++)
            {
                reader.Seek(startOfTable + subTableOffsets[i]);
                switch (lookupType)
                {
                    case GsubLookupType.SingleSubstitution:
                        byte subType = reader.PeekBytes(2)[1];
                        switch (subType)
                        {
                            case 1:
                                SubTables.Add(new SingleSubstitutionFormat1(reader));
                                break;

                            case 2:
                                SubTables.Add(new SingleSubstitutionFormat2(reader));
                                break;
                        }
                        break;

                    case GsubLookupType.MultipleSubstitution:
                        SubTables.Add(new MultipleSubstitutionFormat1(reader));
                        break;

                    case GsubLookupType.AlternateSubstitution:
                        SubTables.Add(new AlternateSubstitutionFormat1(reader));
                        break;

                    case GsubLookupType.LigatureSubstitution:
                        SubTables.Add(new LigatureSubstitutionFormat1(reader));
                        break;

                    case GsubLookupType.ContextSubstitution:
                        subType = reader.PeekBytes(2)[1];
                        switch (subType)
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

                    case GsubLookupType.ChainedContextSubstitution:
                        subType = reader.PeekBytes(2)[1];
                        switch (subType)
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

                    case GsubLookupType.SubstitutionExtension:
                        SubTables.Add(new SubstitutionExtensionFormat1(reader));
                        break;

                    case GsubLookupType.ReverseChainedContexts:
                        SubTables.Add(new ReverseChainSingleSubstitutionFormat1(reader));
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }
}