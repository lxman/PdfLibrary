using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ISO 32000-2 §8.6.6.5, read physically: the alternate colour space and tint transform are the recipe
/// for SIMULATING an ink the output device has no unit for. When every colorant of an NChannel space is
/// a process colorant with a plate, the device has a unit for all of them and nothing may be simulated —
/// the components go straight to their plates.
///
/// <para>Every assertion here is PER PLATE. The defect is a permutation: the same four values in a
/// different order have the same sum, max, multiset and total ink, so any aggregate assertion passes
/// both before and after the fix.</para>
/// </summary>
public class ShadingAllProcessNChannelTests
{
    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

    private static PdfArray Names(params string[] n)
    {
        var items = new PdfObject[n.Length];
        for (var i = 0; i < n.Length; i++) items[i] = new PdfName(n[i]);
        return new PdfArray(items);
    }

    /// <summary>A tint transform returning a CONSTANT (1,1,1,1) for every input. Deliberately not an
    /// identity: it makes "the transform ran" and "the transform was bypassed" impossible to confuse,
    /// so a bypass failure shows up as 0xFFFFFFFF rather than as a subtly wrong ramp.
    ///
    /// <para><b>Its /Domain is one pair, and that is correct.</b> A <c>FunctionType 2</c> exponential
    /// is single-input by construction — <c>ExponentialFunction</c> consumes <c>input[0]</c> and
    /// ignores the declared arity — so <c>/Domain [0 1]</c> and <c>/Domain [0 1 0 1 0 1 0 1]</c>
    /// return byte-identical output. Measured, both arities give (255,255,255,255).</para></summary>
    private static PdfDictionary ConstantTint()
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(2));
        d.Add(new PdfName("Domain"), Reals(0, 1));
        d.Add(new PdfName("C0"), Reals(1, 1, 1, 1));
        d.Add(new PdfName("C1"), Reals(1, 1, 1, 1));
        d.Add(new PdfName("N"), new PdfReal(1));
        return d;
    }

    /// <summary>A true 4-in/4-out IDENTITY tint transform: a Type 4 PostScript calculator whose body
    /// is empty, so the four inputs are left on the stack as the four outputs. This is exactly the
    /// shape veraPDF <c>6-2-4-4-t02-pass-a</c> uses.
    ///
    /// <para><b>Why both fixtures exist.</b> The constant transform proves the BYPASS (bypassed and
    /// not-bypassed cannot produce the same bytes). Only the identity transform shows the DEFECT — a
    /// pure channel permutation, where the four values arrive in <c>/DeviceN</c> names order at CMYK
    /// positions. With the constant transform every "before" value is (255,255,255,255) and the
    /// permutation is invisible.</para></summary>
    private static PdfStream IdentityTint()
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(4));
        d.Add(new PdfName("Domain"), Reals(0, 1, 0, 1, 0, 1, 0, 1));
        d.Add(new PdfName("Range"), Reals(0, 1, 0, 1, 0, 1, 0, 1));
        return new PdfStream(d, Encoding.ASCII.GetBytes("{ }"));
    }

    private static PdfDictionary Attributes(PdfArray components, string processSpace = "DeviceCMYK")
    {
        var process = new PdfDictionary();
        process.Add(new PdfName("ColorSpace"), new PdfName(processSpace));
        process.Add(new PdfName("Components"), components);

        var attrs = new PdfDictionary();
        attrs.Add(new PdfName("Subtype"), new PdfName("NChannel"));
        attrs.Add(new PdfName("Process"), process);
        return attrs;
    }

    // Note the no-attributes overload still yields a FOUR-element array, which matters:
    // OriginForColorSpaceObject parses with minimumElements: 4 and returns no origin for a shorter one.
    // altSpace is the array's OWN alternate (element 2) — independent of Attributes' processSpace
    // (the /Process /ColorSpace inside /Attributes), which is what placement is actually derived from.
    private static PdfArray DeviceN(
        PdfArray names, PdfDictionary? attributes, PdfObject? tint = null, string altSpace = "DeviceCMYK")
    {
        PdfObject t = tint ?? ConstantTint();
        return attributes is null
            ? new PdfArray(new PdfName("DeviceN"), names, new PdfName(altSpace), t)
            : new PdfArray(new PdfName("DeviceN"), names, new PdfName(altSpace), t, attributes);
    }

    private static (byte C, byte M, byte Y, byte K) Cmyk(uint packed) =>
        ((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);

    // Names order and /Components order differ ON PURPOSE — that difference is the whole defect.
    //   names:      [Black, PrCyan, PrMagenta, PrYellow]
    //   /Components:[PrCyan, PrMagenta, PrYellow, Black]  => PrCyan=0, PrMagenta=1, PrYellow=2, Black=3
    // so the slots are [Plate(3), Plate(0), Plate(1), Plate(2)].
    private static PdfArray AllProcessSpace() =>
        DeviceN(Names("Black", "PrCyan", "PrMagenta", "PrYellow"),
                Attributes(Names("PrCyan", "PrMagenta", "PrYellow", "Black")));

    /// <summary>The same space with a true IDENTITY transform — the shape that shows the defect as a
    /// permutation rather than merely showing that the bypass fired.</summary>
    private static PdfArray AllProcessSpaceIdentity() =>
        DeviceN(Names("Black", "PrCyan", "PrMagenta", "PrYellow"),
                Attributes(Names("PrCyan", "PrMagenta", "PrYellow", "Black")),
                IdentityTint());

    /// <summary>The same all-process shape, but with the array's OWN alternate (element 2) set to
    /// <c>/DeviceRGB</c> instead of <c>/DeviceCMYK</c>. All nine pre-existing fixtures in this file use
    /// a DeviceCMYK alternate; this is the untested shape MINOR 2 of the 2026-07-28 review flagged —
    /// <c>AllProcessPlacement</c> never looks at this alternate at all, only at <c>/Process
    /// /ColorSpace</c> (via <c>Attributes</c>), so the bypass must still fire and pack onto plates even
    /// though a non-CMYK, non-Gray alternate would otherwise send <c>BuildTintToCmyk</c> down the
    /// null-returning path at <c>ColorSpaceResolver.cs:488</c>.</summary>
    private static PdfArray AllProcessSpaceRgbAlternate() =>
        DeviceN(Names("Black", "PrCyan", "PrMagenta", "PrYellow"),
                Attributes(Names("PrCyan", "PrMagenta", "PrYellow", "Black")),
                altSpace: "DeviceRGB");

    [Fact]
    public void AllProcessNChannel_PlacesEachComponentOnItsOwnPlate_NotThroughTheTintTransform()
    {
        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(AllProcessSpace(), null);
        Assert.NotNull(toCmyk);

        // Components in NAMES order: Black=0.36, PrCyan=0.57, PrMagenta=0.02, PrYellow=0.80.
        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.36, 0.57, 0.02, 0.80]));

        // PER PLATE. Black's 0.36 lands on K, PrCyan's 0.57 on C, PrMagenta's 0.02 on M,
        // PrYellow's 0.80 on Y. Running the constant tint transform would give (255,255,255,255).
        Assert.Equal(145, c);   // 0.57
        Assert.Equal(5, m);     // 0.02
        Assert.Equal(204, y);   // 0.80
        Assert.Equal(92, k);    // 0.36
    }

    [Fact]
    public void AllProcessNChannel_UnderAnIdentityTransform_TheDefectIsAPurePermutation()
    {
        // THE fixture that shows the defect rather than merely showing the bypass fired. Measured
        // before this change: (92, 145, 5, 204) — the four values in /DeviceN NAMES order at CMYK
        // positions (Black 0.36 -> C, PrCyan 0.57 -> M, PrMagenta 0.02 -> Y, PrYellow 0.80 -> K).
        // After: each on the plate /Process /Components gives it. IDENTICAL multiset, sum, max and
        // total ink — which is why this is asserted per plate and can be asserted no other way.
        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(AllProcessSpaceIdentity(), null);
        Assert.NotNull(toCmyk);

        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.36, 0.57, 0.02, 0.80]));

        Assert.Equal(145, c);   // was 92
        Assert.Equal(5, m);     // was 145
        Assert.Equal(204, y);   // was 5
        Assert.Equal(92, k);    // was 204
    }

    [Fact]
    public void AllProcessNChannel_IdentityTransform_ZeroComponent_MovesTheMARKEDPlate()
    {
        // The overprint consequence, pinned. Measured before: (0, 145, 5, 204) marks {M,Y,K}.
        // After: (145, 5, 204, 0) marks {C,M,Y}. One plate GAINED, one LOST — on the flatten arm at
        // op=true the mask is the nonzero-markedness proxy against this colour, so this is an
        // overprint-behaviour change, not only a colour change. A gained plate paints where a
        // backdrop used to survive; a lost plate preserves one that used to be overpainted.
        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(AllProcessSpaceIdentity(), null);
        Assert.NotNull(toCmyk);

        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.0, 0.57, 0.02, 0.80]));

        Assert.Equal(145, c);   // C GAINED: was 0
        Assert.Equal(5, m);
        Assert.Equal(204, y);
        Assert.Equal(0, k);     // K LOST: was 204
    }

    [Fact]
    public void AllProcessNChannel_ZeroComponent_LeavesThatPlateUnmarked()
    {
        // The mask consequence, pinned separately: a zero must land on the plate its POSITION names,
        // not the plate its ordinal would have. With the tint transform bypassed, C is the zero one.
        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(AllProcessSpace(), null);
        Assert.NotNull(toCmyk);

        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.36, 0.0, 0.02, 0.80]));

        Assert.Equal(0, c);     // PrCyan is the zero component and PrCyan IS the cyan plate
        Assert.Equal(5, m);
        Assert.Equal(204, y);
        Assert.Equal(92, k);
    }

    [Fact]
    public void NoneComponent_ContributesToNoPlate()
    {
        // /None is a colorant the printer deliberately does not run. Placement gives it Nothing,
        // and Nothing must reach no plate at all.
        PdfArray space = DeviceN(Names("PrCyan", "None"),
                                 Attributes(Names("PrCyan", "PrMagenta", "PrYellow", "Black")));

        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(space, null);
        Assert.NotNull(toCmyk);

        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.36, 1.0]));

        Assert.Equal(92, c);    // PrCyan -> plate 0
        Assert.Equal(0, m);
        Assert.Equal(0, y);
        Assert.Equal(0, k);     // /None's 1.0 went nowhere
    }

    [Fact]
    public void AllProcessNChannel_WithADeviceRgbAlternate_StillBypassesAndPacksByPlacement()
    {
        // MINOR 2 (2026-07-28 review): the bypass widens toCmyk for an all-process NChannel whose OWN
        // alternate is not DeviceCMYK/DeviceGray — a shape BuildTintToCmyk would otherwise refuse
        // (ColorSpaceResolver.cs:488) and send down the sRGB SampleRgbAt path. Placement is derived
        // from /Process /ColorSpace, not this array's alternate, so a DeviceRGB alternate must not
        // change the outcome: the mapper is non-null and packs per plate, identically to
        // AllProcessSpace's DeviceCMYK-alternate fixture.
        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(AllProcessSpaceRgbAlternate(), null);
        Assert.NotNull(toCmyk);

        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.36, 0.57, 0.02, 0.80]));

        Assert.Equal(145, c);   // 0.57
        Assert.Equal(5, m);     // 0.02
        Assert.Equal(204, y);   // 0.80
        Assert.Equal(92, k);    // 0.36
    }

    [Fact]
    public void AllNoneNChannel_DoesNotBypass_SoTheTintPathStillRefusesIt()
    {
        // Every colorant is /None: SpotNames is empty (no Spot slot exists) but there is also no Plate
        // slot — placement is [Nothing, Nothing]. Before the fix, `placement is { SpotNames.Count: 0 }`
        // alone was satisfied, so the bypass fired and packed zero into every plate (0x00000000) — a
        // path BuildTintToCmyk had always refused via its own PaintsNothing check. After the fix the
        // bypass requires at least one Plate slot too, so it declines, BuildCmykMapper falls through to
        // BuildTintToCmyk, and PaintsNothing refuses the space outright: the mapper must be null.
        PdfArray space = DeviceN(Names("None", "None"),
                                 Attributes(Names("PrCyan", "PrMagenta", "PrYellow", "Black")));

        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(space, null);

        Assert.Null(toCmyk);
    }

    // --- shapes that must STILL take the tint transform ---

    [Fact]
    public void NChannelWithASpotComponent_StillRunsTheTintTransform()
    {
        // One colorant with no unit means the space still needs simulating. All-or-nothing:
        // the bypass must not fire just because SOME components are process.
        PdfArray space = DeviceN(Names("PrCyan", "GWG Green"),
                                 Attributes(Names("PrCyan", "PrMagenta", "PrYellow", "Black")));

        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(space, null);
        Assert.NotNull(toCmyk);

        // The constant transform ran. Asserted per plate, not as a tuple: a tuple literal of ints
        // will not unify with (byte, byte, byte, byte) for Assert.Equal's type inference.
        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.36, 0.57]));
        Assert.Equal(255, c);
        Assert.Equal(255, m);
        Assert.Equal(255, y);
        Assert.Equal(255, k);
    }

    [Fact]
    public void PlainDeviceN_StillRunsTheTintTransform()
    {
        // No /Attributes at all => no Subtype => not NChannel => no placement.
        PdfArray space = DeviceN(Names("PrCyan", "PrMagenta"), attributes: null);

        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(space, null);
        Assert.NotNull(toCmyk);

        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.36, 0.57]));
        Assert.Equal(255, c);
        Assert.Equal(255, m);
        Assert.Equal(255, y);
        Assert.Equal(255, k);
    }

    [Fact]
    public void NChannelOverAOneChannelProcessSpace_StillRunsTheTintTransform()
    {
        // Under /DeviceGray a listed name also gets channel 0, byte-identical to a CMYK cyan.
        // ColorantPlacement refuses the whole table there, so the bypass must not fire.
        PdfArray space = DeviceN(Names("Ink1"),
                                 Attributes(Names("Ink1"), processSpace: "DeviceGray"));

        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(space, null);
        Assert.NotNull(toCmyk);

        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.36]));
        Assert.Equal(255, c);
        Assert.Equal(255, m);
        Assert.Equal(255, y);
        Assert.Equal(255, k);
    }

    // --- Step 8: the mesh path, which is not modified by this task but must inherit the fix through
    // BuildCmykMapper. There is no NChannel mesh anywhere in the corpus (Task 0's M3), so this
    // synthetic fixture is the only thing that can ever pin it.

    // Control-point (col,row) in the order a full (flag 0) tensor patch lists them in the stream
    // (same order MeshShadingReaderTests uses).
    private static readonly (int Col, int Row)[] FullOrder =
    [
        (0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2),
        (3, 1), (3, 0), (2, 0), (1, 0), (1, 1), (1, 2), (2, 2), (2, 1)
    ];

    // A single flag-0 tensor patch over the AllProcessSpaceIdentity() colour space, all four corners
    // carrying the SAME four raw component bytes in /DeviceN NAMES order (Black, PrCyan, PrMagenta,
    // PrYellow) = (92, 145, 5, 204) — the same values the non-mesh identity-transform test uses, so the
    // uniform-colour patch's CMYK is directly comparable: pre-fix (92,145,5,204), post-fix (145,5,204,92).
    private static PdfStream AllProcessMeshIdentity()
    {
        var data = new List<byte> { 0 }; // edge flag 0
        foreach ((int col, int row) in FullOrder)
        {
            data.Add((byte)(col * 85));
            data.Add((byte)(row * 85));
        }
        byte[] corner = [92, 145, 5, 204]; // Black, PrCyan, PrMagenta, PrYellow
        for (var i = 0; i < 4; i++) data.AddRange(corner); // c00, c03, c33, c30 — uniform colour

        var dict = new PdfDictionary();
        dict.Add(new PdfName("ShadingType"), new PdfInteger(7));
        dict.Add(new PdfName("ColorSpace"), AllProcessSpaceIdentity());
        dict.Add(new PdfName("BitsPerCoordinate"), new PdfInteger(8));
        dict.Add(new PdfName("BitsPerComponent"), new PdfInteger(8));
        dict.Add(new PdfName("BitsPerFlag"), new PdfInteger(8));
        dict.Add(new PdfName("Decode"), Reals(0, 100, 0, 100, 0, 1, 0, 1, 0, 1, 0, 1));
        return new PdfStream(dict, data.ToArray());
    }

    [Fact]
    public void MeshShading_AllProcessNChannel_PlacesEachVertexComponentOnItsOwnPlate()
    {
        ShadingDescriptor? d = ShadingBuilder.Build(AllProcessMeshIdentity(), null);
        Assert.NotNull(d);
        Assert.True(d!.MeshHasCmyk);
        Assert.NotEmpty(d.MeshTriangles);

        // Uniform-colour patch: every vertex carries the same packed CMYK, so any vertex pins it.
        (byte c, byte m, byte y, byte k) = Cmyk(d.MeshTriangles[0].Cmyk);

        Assert.Equal(145, c);   // was 92
        Assert.Equal(5, m);     // was 145
        Assert.Equal(204, y);   // was 5
        Assert.Equal(92, k);    // was 204
    }

    // --- G-14: plain (non-NChannel) all-reserved Separation/DeviceN pack straight onto plates ---

    private static PdfDictionary LyingMagentaTint()
    {
        // tint t → (0, t, 0, 0): a deliberately WRONG alternate for a /Cyan separation. Direct
        // application must ignore it; the flatten path is positionally visible on the M plate.
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(2));
        d.Add(new PdfName("Domain"), Reals(0, 1));
        d.Add(new PdfName("C0"), Reals(0, 0, 0, 0));
        d.Add(new PdfName("C1"), Reals(0, 1, 0, 0));
        d.Add(new PdfName("N"), new PdfReal(1));
        return d;
    }

    [Fact]
    public void G14_ReservedSeparation_MapperPacksItsPlateDirectly()
    {
        var cs = new PdfArray(new PdfName("Separation"), new PdfName("Cyan"),
            new PdfName("DeviceCMYK"), LyingMagentaTint());

        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(cs, null);

        Assert.NotNull(toCmyk);
        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.7]));
        Assert.Equal(178, c);        // 0.7 → its OWN plate
        Assert.Equal(0, m);          // the lying alternate is ignored
        Assert.Equal(0, y);
        Assert.Equal(0, k);
    }

    [Fact]
    public void G14_ReservedPlainDeviceN_MapperPacksByName_NoneDiscarded()
    {
        // Plain DeviceN (NO /Attributes → no placement → the pre-G-14 code ran the tint transform).
        // Names deliberately non-canonical order + /None: [Black, Cyan, None].
        var cs = new PdfArray(new PdfName("DeviceN"), Names("Black", "Cyan", "None"),
            new PdfName("DeviceCMYK"), IdentityTint());

        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(cs, null);

        Assert.NotNull(toCmyk);
        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.5, 0.25, 0.9]));
        Assert.Equal(64, c);         // Cyan is names[1] → C plate gets 0.25
        Assert.Equal(0, m);
        Assert.Equal(0, y);
        Assert.Equal(128, k);        // Black is names[0] → K plate gets 0.5; 0.5×255 = 127.5 → 128 (Clamp255 rounds to even)
        // /None's 0.9 appears NOWHERE. Measured OLD-path behaviour: the pre-existing tint-transform
        // code right-shifts a 3-component identity result by one plate (C=0, M=0.5, Y=0.25, K=0.9),
        // a pre-existing 3-into-4-domain padding artifact unrelated to this task — every plate still
        // distinguishes the two paths positionally.
    }

    [Fact]
    public void G14_MixedDeviceN_MapperStillRunsTheTintTransform()
    {
        // NEGATIVE CONTROL: one non-reserved name → the predicate fails → tint transform runs.
        var cs = new PdfArray(new PdfName("DeviceN"), Names("Cyan", "PANTONE-X"),
            new PdfName("DeviceCMYK"), ConstantTint());

        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(cs, null);

        Assert.NotNull(toCmyk);
        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.5, 0.5]));
        Assert.Equal((byte)255, c);  // ConstantTint returns (1,1,1,1) — proof the transform RAN
        Assert.Equal((byte)255, m);
        Assert.Equal((byte)255, y);
        Assert.Equal((byte)255, k);
    }
}
