using ICCSharp;
using ICCSharp.Profile;
using PdfLibrary.Rendering.Icc;

namespace PdfLibrary.Tests.Rendering;

public class BundledProfileResourceTests
{
    [Fact]
    public void Swop_resource_is_embedded_and_is_a_cmyk_profile()
    {
        byte[] bytes = IccResources.ReadSwop();
        Assert.True(bytes.Length > 100_000, $"expected the ~557KB SWOP profile, got {bytes.Length} bytes");

        IccProfile profile = IccProfile.Parse(bytes);
        IccTransform toRgb = IccTransform.Create(profile, BuiltInProfiles.Srgb);

        Assert.Equal(4, toRgb.InputChannels);   // CMYK device side
        Assert.Equal(3, toRgb.OutputChannels);  // sRGB
    }
}
