using ICCSharp.IO;
using ICCSharp.Profile;
using ICCSharp.Tags;

namespace ICCSharp.Tests.Profile;

/// <summary>
/// Smoke-level integration: parses the OS-installed sRGB profile end-to-end
/// (header + tag table). Skipped silently when the profile is not available
/// so the test suite stays portable.
/// </summary>
public class RealProfileSmokeTests
{
    private static readonly string SrgbPath =
        @"C:\Windows\System32\spool\drivers\color\sRGB Color Space Profile.icm";

    [Fact]
    public void SRGB_profile_parses_header_and_tag_table()
    {
        if (!File.Exists(SrgbPath))
            return; // Not installed on this machine; skip silently.

        byte[] bytes = File.ReadAllBytes(SrgbPath);
        Assert.True(bytes.Length >= ProfileHeader.Size, "Profile shorter than header.");

        ProfileHeader header = ProfileHeader.Parse(bytes);
        Assert.Equal(ProfileHeader.MagicNumber, header.Magic);
        Assert.Equal(ProfileClass.Display, header.Class);
        Assert.Equal(ColorSpaceSignatures.RGB, header.DataColorSpace);
        Assert.Equal(ColorSpaceSignatures.XYZ, header.ProfileConnectionSpace);
        Assert.True(header.ProfileSize <= bytes.Length,
            $"Header declares size {header.ProfileSize} but file is {bytes.Length} bytes.");

        // Tag table sits at offset 128.
        IccBinaryReader r = new(bytes);
        r.Position = ProfileHeader.Size;
        TagTable table = TagTable.Parse(r, header.ProfileSize);
        Assert.True(table.Count > 0);

        // Every display profile must carry these descriptive tags.
        Assert.True(table.Contains(IccSignature.FromAscii("desc")), "Missing 'desc' tag.");
        Assert.True(table.Contains(IccSignature.FromAscii("wtpt")), "Missing 'wtpt' tag.");
    }

    [Fact]
    public void SRGB_profile_every_tag_parses_through_dispatcher()
    {
        if (!File.Exists(SrgbPath))
            return;

        byte[] bytes = File.ReadAllBytes(SrgbPath);
        ProfileHeader header = ProfileHeader.Parse(bytes);
        IccBinaryReader r = new(bytes);
        r.Position = ProfileHeader.Size;
        TagTable table = TagTable.Parse(r, header.ProfileSize);

        // Walk every tag — no exceptions, every result non-null. Unknown types are allowed
        // (they fall through to UnknownTagElement) but parser explosions are not.
        ReadOnlyMemory<byte> mem = bytes;
        foreach (TagDirectoryEntry entry in table.Entries)
        {
            ReadOnlyMemory<byte> slice = mem.Slice((int)entry.Offset, (int)entry.Size);
            TagElement el = TagElementReader.Parse(slice);
            Assert.NotNull(el);
        }

        // White point should be an XYZ tag; description should be either 'desc' or 'mluc'.
        ReadOnlyMemory<byte> wtpt = SliceTag(mem, table, "wtpt");
        Assert.IsType<XyzTagElement>(TagElementReader.Parse(wtpt));

        ReadOnlyMemory<byte> desc = SliceTag(mem, table, "desc");
        TagElement descEl = TagElementReader.Parse(desc);
        Assert.True(descEl is TextDescriptionTagElement or MultiLocalizedUnicodeTagElement,
            $"'desc' parsed as {descEl.GetType().Name}");
    }

    private static ReadOnlyMemory<byte> SliceTag(ReadOnlyMemory<byte> mem, TagTable table, string sig)
    {
        TagDirectoryEntry e = table[IccSignature.FromAscii(sig)];
        return mem.Slice((int)e.Offset, (int)e.Size);
    }
}
