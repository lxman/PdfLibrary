using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tests.Profile;

public class ProfileClassTests
{
    [Theory]
    [InlineData("scnr", ProfileClass.Input)]
    [InlineData("mntr", ProfileClass.Display)]
    [InlineData("prtr", ProfileClass.Output)]
    [InlineData("link", ProfileClass.DeviceLink)]
    [InlineData("spac", ProfileClass.ColorSpace)]
    [InlineData("abst", ProfileClass.Abstract)]
    [InlineData("nmcl", ProfileClass.NamedColor)]
    public void Known_signatures_map_to_enum(string sig, ProfileClass expected)
    {
        Assert.Equal(expected, ProfileClassSignatures.FromSignature(IccSignature.FromAscii(sig)));
    }

    [Fact]
    public void Unknown_signature_falls_through()
    {
        Assert.Equal(ProfileClass.Unknown, ProfileClassSignatures.FromSignature(IccSignature.FromAscii("zzzz")));
    }
}
