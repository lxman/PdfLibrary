using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Document;

/// <summary>
/// Optional-content visibility for marked content (<c>/OC /MCx BDC … EMC</c>): OCG on/off from the default
/// configuration, OCMD <c>/P</c> policy, and OCMD <c>/VE</c> visibility expressions (ISO 32000-1 §8.11).
/// The default configuration <c>/D</c> is stored as an INDIRECT reference here — the case that left GWG150
/// unhidden before the manager resolved it.
/// </summary>
public class OptionalContentManagerTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    // OCG 10 = ON, OCG 11 = OFF, wired via an indirect /D default configuration.
    private static OptionalContentManager Manager()
    {
        var doc = new PdfDocument();
        doc.AddObject(10, 0, new PdfDictionary { [N("Type")] = N("OCG") });
        doc.AddObject(11, 0, new PdfDictionary { [N("Type")] = N("OCG") });
        doc.AddObject(20, 0, new PdfDictionary
        {
            [N("BaseState")] = N("ON"),
            [N("ON")] = new PdfArray(Ref(10)),
            [N("OFF")] = new PdfArray(Ref(11)),
        });
        var catalog = new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("OCProperties")] = new PdfDictionary
            {
                [N("OCGs")] = new PdfArray(Ref(10), Ref(11)),
                [N("D")] = Ref(20),   // indirect default config — must be resolved
            },
        };
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return new OptionalContentManager(doc);
    }

    private static PdfDictionary Ocmd(params (string Key, PdfObject Value)[] entries)
    {
        var d = new PdfDictionary { [N("Type")] = N("OCMD") };
        foreach ((string key, PdfObject value) in entries) d[N(key)] = value;
        return d;
    }

    [Fact]
    public void On_ocg_is_visible() => Assert.True(Manager().IsMarkedContentVisible(Ref(10)));

    // Fails unless the manager resolves the indirect /D (the GWG150 regression).
    [Fact]
    public void Off_ocg_is_hidden_via_indirect_default_config() =>
        Assert.False(Manager().IsMarkedContentVisible(Ref(11)));

    [Fact]
    public void Null_property_is_visible() => Assert.True(Manager().IsMarkedContentVisible(null));

    [Theory]
    [InlineData("AnyOn", false)]   // the only member (OCG 11) is off
    [InlineData("AllOn", false)]
    [InlineData("AnyOff", true)]
    [InlineData("AllOff", true)]
    public void Ocmd_p_policy_over_a_single_off_member(string policy, bool visible) =>
        Assert.Equal(visible, Manager().IsMarkedContentVisible(
            Ocmd(("OCGs", new PdfArray(Ref(11))), ("P", N(policy)))));

    [Fact]
    public void Ocmd_ve_not_off_is_visible() =>
        Assert.True(Manager().IsMarkedContentVisible(Ocmd(("VE", new PdfArray(N("Not"), Ref(11))))));

    [Fact]
    public void Ocmd_ve_and_on_and_off_is_hidden() =>
        Assert.False(Manager().IsMarkedContentVisible(Ocmd(("VE", new PdfArray(N("And"), Ref(10), Ref(11))))));

    [Fact]
    public void Ocmd_ve_or_on_or_off_is_visible() =>
        Assert.True(Manager().IsMarkedContentVisible(Ocmd(("VE", new PdfArray(N("Or"), Ref(10), Ref(11))))));

    // /VE present → /OCGs + /P are ignored. Here /P AnyOn over the off member would say hidden, but /VE wins.
    [Fact]
    public void Ve_takes_precedence_over_ocgs_and_p() =>
        Assert.True(Manager().IsMarkedContentVisible(
            Ocmd(("VE", new PdfArray(N("Not"), Ref(11))),
                 ("OCGs", new PdfArray(Ref(11))), ("P", N("AnyOn")))));
}
