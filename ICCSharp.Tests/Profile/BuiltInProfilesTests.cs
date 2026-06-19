using ICCSharp.Profile;

namespace ICCSharp.Tests.Profile;

public class BuiltInProfilesTests
{
    [Fact]
    public void Srgb_profile_parses_and_has_matrix_TRC_family()
    {
        IccProfile p = BuiltInProfiles.Srgb;
        Assert.Equal(ProfileClass.Display, p.Header.Class);
        Assert.Equal(ColorSpaceSignatures.RGB, p.Header.DataColorSpace);
        Assert.Equal(ColorSpaceSignatures.XYZ, p.Header.ProfileConnectionSpace);
        Assert.True(p.HasMatrixTrc);
    }

    [Fact]
    public void Srgb_self_transform_is_identity()
    {
        // Round-trip through the synthetic profile must recover the input to within s15Fixed16
        // precision (~1e-4 max delta).
        IccProfile p = BuiltInProfiles.Srgb;
        var t = IccTransform.Create(p, p);

        foreach ((double r, double g, double b) in new[]
        {
            (0.0, 0.0, 0.0),
            (1.0, 1.0, 1.0),
            (0.5, 0.5, 0.5),
            (0.1, 0.4, 0.8),
        })
        {
            double[] result = t.Apply(r, g, b);
            Assert.Equal(r, result[0], 3);
            Assert.Equal(g, result[1], 3);
            Assert.Equal(b, result[2], 3);
        }
    }

    [Fact]
    public void Srgb_singleton_is_cached()
    {
        Assert.Same(BuiltInProfiles.Srgb, BuiltInProfiles.Srgb);
    }
}
