using System;
using FontParser.Reader;
using FontParser.Tables.Woff;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8601 // Possible null reference assignment.

namespace FontParser.Tables.WOFF2
{
    public class Woff2TableDirectoryEntry : IDirectoryEntry
    {
        public string Tag { get; }

        public Enum Transformation { get; }

        public uint OriginalLength { get; }

        public uint? TransformLength { get; }

        public Woff2TableDirectoryEntry(FileByteReader reader)
        {
            byte flags = reader.ReadBytes(1)[0];
            var tableTag = Convert.ToByte(flags & 0x3F);
            var transformationVersion = Convert.ToByte((flags & 0xC0) >> 6);
            var newFlag = 0;
            Tag = tableTag <= 62
                ? Woff2KnownTableTags.Values[tableTag]
                : reader.ReadString(4);
            Transformation = Tag switch
            {
                "glyf" => (GlyfTransform)transformationVersion,
                "loca" => (LocaTransform)transformationVersion,
                "hmtx" => (HmtxTransform)transformationVersion,
                _ => Transformation
            };
            OriginalLength = reader.ReadUintBase128();
            if (Tag == "glyf" || Tag == "loca")
            {
                if (transformationVersion == 0)
                {
                    newFlag |= 0x100;
                }
            }
            else if (transformationVersion != 0)
            {
                newFlag |= 0x100;
            }

            newFlag |= transformationVersion;
            if ((newFlag & 0x100) == 0)
            {
                return;
            }
            TransformLength = reader.ReadUintBase128();
        }
    }
}