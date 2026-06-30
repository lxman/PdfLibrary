using ICCSharp;
using ICCSharp.Profile;
using PdfLibrary.Rendering.Icc;

namespace PdfLibrary.Tests.Rendering;

public class CmykProfileProviderTests
{
    [Fact]
    public void Default_provider_returns_a_cmyk_profile()
    {
        var provider = new CmykProfileProvider();
        IccProfile profile = provider.GetProfile();
        IccTransform t = IccTransform.Create(profile, BuiltInProfiles.Srgb);
        Assert.Equal(4, t.InputChannels);
    }

    [Fact]
    public void GetProfile_is_cached_when_key_unchanged()
    {
        var provider = new CmykProfileProvider();
        IccProfile a = provider.GetProfile();
        IccProfile b = provider.GetProfile();
        Assert.Same(a, b);
    }

    [Fact]
    public void Changing_override_invalidates_cache()
    {
        var provider = new CmykProfileProvider();
        IccProfile bundled = provider.GetProfile();

        // Point the override at a temp copy of the bundled bytes (a valid CMYK profile on disk).
        string tmp = Path.Combine(Path.GetTempPath(), $"sp1-cmyk-{Guid.NewGuid():N}.icc");
        File.WriteAllBytes(tmp, IccResources.ReadDefaultCmykProfile());
        try
        {
            provider.OverridePath = tmp;
            IccProfile overridden = provider.GetProfile();
            Assert.NotSame(bundled, overridden);                // cache was invalidated + reloaded
            Assert.Equal(4, IccTransform.Create(overridden, BuiltInProfiles.Srgb).InputChannels);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Invalid_override_falls_back_to_bundled()
    {
        var provider = new CmykProfileProvider
        {
            OverridePath = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".icc")
        };
        IccProfile profile = provider.GetProfile();           // must not throw
        Assert.Equal(4, IccTransform.Create(profile, BuiltInProfiles.Srgb).InputChannels);
    }

    [Fact]
    public void OverrideProfileBytes_take_precedence_and_invalidate_cache()
    {
        var provider = new CmykProfileProvider();
        IccProfile bundled = provider.GetProfile();

        provider.OverrideProfileBytes = IccResources.ReadDefaultCmykProfile(); // valid CMYK bytes
        IccProfile fromBytes = provider.GetProfile();
        Assert.NotSame(bundled, fromBytes);
        Assert.Equal(4, IccTransform.Create(fromBytes, BuiltInProfiles.Srgb).InputChannels);

        provider.OverrideProfileBytes = null;     // revert
        IccProfile revert = provider.GetProfile();
        Assert.Equal(4, IccTransform.Create(revert, BuiltInProfiles.Srgb).InputChannels);
    }

    [Fact]
    public void Invalid_override_bytes_fall_back()
    {
        var provider = new CmykProfileProvider { OverrideProfileBytes = new byte[] { 1, 2, 3 } };
        IccProfile profile = provider.GetProfile();   // must not throw
        Assert.Equal(4, IccTransform.Create(profile, BuiltInProfiles.Srgb).InputChannels);
    }
}
