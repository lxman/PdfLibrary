using FontParser.Reader;
using FontParser.Tables.Common;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8601 // Possible null reference assignment.

namespace FontParser.Tables.Gsub.LookupSubTables.SubstitutionExtension
{
    public class SubstitutionExtensionFormat1 : ILookupSubTable
    {
        public ILookupSubTable SubstitutionTable { get; }

        public SubstitutionExtensionFormat1(BigEndianReader reader)
        {
            // TODO: Needs work
            long startOfTable = reader.Position;
            _ = reader.ReadUShort();
            ushort extensionLookupType = reader.ReadUShort();
            uint extensionOffset = reader.ReadUInt32();
            reader.Seek(startOfTable + extensionOffset);
            SubstitutionTable = extensionLookupType switch
            {
                1 => new SingleSubstitution.SingleSubstitutionFormat1(reader),
                2 => new MultipleSubstitution.MultipleSubstitutionFormat1(reader),
                3 => new AlternateSubstitution.AlternateSubstitutionFormat1(reader),
                4 => new LigatureSubstitution.LigatureSubstitutionFormat1(reader),
                //5 => new ContextSubstitution.Format1(reader),
                //6 => new ChainingContextualSubstitution.Format1(reader),
                //7 => new ExtensionSubstitution.Format1(reader),
                //8 => new ReverseChainingContextualSingleSubstitution.Format1(reader),
                //9 => new MultipleSubstitution.Format2(reader),
                _ => SubstitutionTable
            };
        }
    }
}