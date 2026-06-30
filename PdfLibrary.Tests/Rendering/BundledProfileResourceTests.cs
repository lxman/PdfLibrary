using ICCSharp;
using ICCSharp.Profile;
using PdfLibrary.Rendering.Icc;

namespace PdfLibrary.Tests.Rendering;

public class BundledProfileResourceTests
{
    [Fact]
    public void Default_cmyk_resource_is_embedded_and_is_a_cmyk_profile()
    {
        byte[] bytes = IccResources.ReadDefaultCmykProfile();
        Assert.True(bytes.Length > 50_000, $"expected the bundled CMYK profile, got {bytes.Length} bytes");

        IccProfile profile = IccProfile.Parse(bytes);
        IccTransform toRgb = IccTransform.Create(profile, BuiltInProfiles.Srgb);

        Assert.Equal(4, toRgb.InputChannels);
        Assert.Equal(3, toRgb.OutputChannels);
    }
}
