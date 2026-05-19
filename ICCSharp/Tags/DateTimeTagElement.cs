using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>ICC.1:2010 §10.7 dateTimeType — one dateTimeNumber.</summary>
public sealed class DateTimeTagElement : TagElement
{
    public IccDateTime Value { get; }

    public DateTimeTagElement(IccDateTime value) : base(TagTypeSignatures.DateTimeType)
    {
        Value = value;
    }

    internal static DateTimeTagElement Parse(IccBinaryReader reader, int payloadBytes)
    {
        if (payloadBytes < 12)
            throw new IccParseException($"dateTimeType payload requires 12 bytes, got {payloadBytes}.");
        return new DateTimeTagElement(reader.ReadDateTime());
    }
}
