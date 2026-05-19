using System;
using ICCSharp.IO;

namespace ICCSharp.Profile;

/// <summary>
/// ICC.1:2010 §7.2 — the 128-byte profile header. Field offsets are documented inline.
/// Construction is permissive: unknown values for enumerated fields are preserved via the
/// Raw* signature properties; only egregious structural errors throw (missing 'acsp' magic,
/// declared profile size below 128).
/// </summary>
public sealed class ProfileHeader
{
    /// <summary>Required size of the on-disk header.</summary>
    public const int Size = 128;

    /// <summary>'acsp' — required at offset 36 (§7.2.9).</summary>
    public static readonly IccSignature MagicNumber = IccSignature.FromAscii("acsp");

    public uint ProfileSize { get; }                 // 0
    public IccSignature PreferredCmm { get; }        // 4
    public ProfileVersion Version { get; }           // 8
    public ProfileClass Class { get; }               // 12
    public IccSignature RawClass { get; }
    public IccSignature DataColorSpace { get; }      // 16
    public IccSignature ProfileConnectionSpace { get; } // 20
    public IccDateTime CreationDate { get; }         // 24
    public IccSignature Magic { get; }               // 36 — 'acsp'
    public PrimaryPlatform PrimaryPlatform { get; }  // 40
    public IccSignature RawPlatform { get; }
    public ProfileFlags Flags { get; }               // 44
    public IccSignature DeviceManufacturer { get; }  // 48
    public IccSignature DeviceModel { get; }         // 52
    public DeviceAttributes DeviceAttributes { get; }// 56 (8 bytes)
    public RenderingIntent RenderingIntent { get; }  // 64 (low 16 bits)
    public ushort RenderingIntentHighBits { get; }   // 64 (high 16 bits, reserved)
    public XyzNumber Illuminant { get; }             // 68 (D50 nominal)
    public IccSignature ProfileCreator { get; }      // 80
    public ReadOnlyMemory<byte> ProfileId { get; }   // 84  (16 bytes — MD5)
    public ReadOnlyMemory<byte> Reserved { get; }    // 100 (28 bytes — must be zero per spec)

    private ProfileHeader(
        uint profileSize,
        IccSignature preferredCmm,
        ProfileVersion version,
        ProfileClass profileClass,
        IccSignature rawClass,
        IccSignature dataColorSpace,
        IccSignature pcs,
        IccDateTime creationDate,
        IccSignature magic,
        PrimaryPlatform primaryPlatform,
        IccSignature rawPlatform,
        ProfileFlags flags,
        IccSignature deviceManufacturer,
        IccSignature deviceModel,
        DeviceAttributes deviceAttributes,
        RenderingIntent renderingIntent,
        ushort renderingIntentHighBits,
        XyzNumber illuminant,
        IccSignature profileCreator,
        ReadOnlyMemory<byte> profileId,
        ReadOnlyMemory<byte> reserved)
    {
        ProfileSize = profileSize;
        PreferredCmm = preferredCmm;
        Version = version;
        Class = profileClass;
        RawClass = rawClass;
        DataColorSpace = dataColorSpace;
        ProfileConnectionSpace = pcs;
        CreationDate = creationDate;
        Magic = magic;
        PrimaryPlatform = primaryPlatform;
        RawPlatform = rawPlatform;
        Flags = flags;
        DeviceManufacturer = deviceManufacturer;
        DeviceModel = deviceModel;
        DeviceAttributes = deviceAttributes;
        RenderingIntent = renderingIntent;
        RenderingIntentHighBits = renderingIntentHighBits;
        Illuminant = illuminant;
        ProfileCreator = profileCreator;
        ProfileId = profileId;
        Reserved = reserved;
    }

    /// <summary>
    /// Parses the 128-byte header from the start of <paramref name="data"/>.
    /// The reader is advanced by exactly 128 bytes on success.
    /// </summary>
    public static ProfileHeader Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length < Size)
            throw new IccParseException($"Header requires {Size} bytes, got {data.Length}.");
        return Parse(new IccBinaryReader(data));
    }

    public static ProfileHeader Parse(IccBinaryReader reader)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        if (reader.Remaining < Size)
            throw new IccParseException($"Header requires {Size} bytes, only {reader.Remaining} remain.");

        int startPosition = reader.Position;

        uint profileSize = reader.ReadUInt32();
        if (profileSize < Size)
            throw new IccParseException($"Profile size field ({profileSize}) is smaller than the 128-byte header.");

        IccSignature preferredCmm = reader.ReadSignature();
        ProfileVersion version = ProfileVersion.FromRaw(reader.ReadUInt32());

        IccSignature rawClass = reader.ReadSignature();
        ProfileClass profileClass = ProfileClassSignatures.FromSignature(rawClass);

        IccSignature dataColorSpace = reader.ReadSignature();
        IccSignature pcs = reader.ReadSignature();
        IccDateTime creationDate = reader.ReadDateTime();

        IccSignature magic = reader.ReadSignature();
        if (magic != MagicNumber)
            throw new IccParseException(
                $"Missing 'acsp' magic at offset 36; found '{magic}' (0x{magic.Value:X8}).");

        IccSignature rawPlatform = reader.ReadSignature();
        PrimaryPlatform platform = PrimaryPlatformSignatures.FromSignature(rawPlatform);

        ProfileFlags flags = (ProfileFlags)reader.ReadUInt32();
        IccSignature deviceManufacturer = reader.ReadSignature();
        IccSignature deviceModel = reader.ReadSignature();
        DeviceAttributes deviceAttributes = (DeviceAttributes)reader.ReadUInt64();

        uint intentField = reader.ReadUInt32();
        RenderingIntent intent = (RenderingIntent)(intentField & 0xFFFF);
        ushort intentHigh = (ushort)((intentField >> 16) & 0xFFFF);

        XyzNumber illuminant = reader.ReadXyz();
        IccSignature creator = reader.ReadSignature();

        byte[] profileId = reader.ReadBytes(16).ToArray();
        byte[] reserved = reader.ReadBytes(28).ToArray();

        int consumed = reader.Position - startPosition;
        if (consumed != Size)
            throw new IccParseException($"Header parser consumed {consumed} bytes (expected {Size}).");

        return new ProfileHeader(
            profileSize, preferredCmm, version, profileClass, rawClass,
            dataColorSpace, pcs, creationDate, magic,
            platform, rawPlatform, flags,
            deviceManufacturer, deviceModel, deviceAttributes,
            intent, intentHigh, illuminant, creator,
            profileId, reserved);
    }

    /// <summary>True iff every byte of <see cref="ProfileId"/> is zero (unset, per §7.2.18).</summary>
    public bool HasProfileId
    {
        get
        {
            ReadOnlySpan<byte> id = ProfileId.Span;
            for (int i = 0; i < id.Length; i++)
                if (id[i] != 0) return true;
            return false;
        }
    }
}
