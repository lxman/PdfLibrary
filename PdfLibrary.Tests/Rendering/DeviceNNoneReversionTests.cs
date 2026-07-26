using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ISO 32000-2 §8.6.6.5, matrix row 5-8: "when the DeviceN colour space reverts to its alternate
/// colour space, those components shall be passed to the tint transformation function" — where
/// "those" are the components naming <c>/None</c>.
///
/// <para>
/// This is the clause most easily got backwards, because its neighbour 5-7 requires the opposite:
/// when painting named device colourants DIRECTLY, <c>/None</c> components are discarded. Discarded
/// when painting direct, passed through on reversion. An implementation that filtered <c>/None</c>
/// out at the colour-space level would satisfy 5-7 and violate this row, and the two are one
/// paragraph apart in the specification.
/// </para>
///
/// <para>
/// The transform is chosen so the <c>/None</c> component's tint MATTERS to the output: it maps
/// (t₁,t₂) → (0, t₁, t₂, 0), so t₂ — the <c>/None</c> component — lands on the yellow plate. A
/// transform that ignored its second input would pass whether or not the component was passed, which
/// is the vacuity trap.
/// </para>
/// </summary>
public class DeviceNNoneReversionTests
{
    [Fact]
    public void DeviceN_Reversion_PassesNoneComponentsToTheTintTransform()
    {
        // (t₁ t₂) → (0 t₁ t₂ 0): push 0, roll top 3 by 1, push 0.
        const string ps = "<< /FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1 0 1] /Length 16 >>\r\n" +
                          "stream\r\n{ 0 3 1 roll 0 }\r\nendstream";
        const string cs = "[/DeviceN [/SpotA /None] /DeviceCMYK 5 0 R]";

        SKColor viaDeviceN = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build(cs, ColourConformancePage.FillRect("/Cs0 cs 0.25 0.75 scn"),
                withFont: false, extraResources: "", extraObjects: ps));

        // The oracle: the same colour painted directly in the alternate space. If the /None component
        // (0.75) reached the transform, the yellow plate is 0.75 and the two must be identical.
        SKColor direct = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build("/DeviceRGB", ColourConformancePage.FillRect("0 0.25 0.75 0 k")));

        Assert.True(viaDeviceN == direct,
            $"DeviceN reversion painted RGB({viaDeviceN.Red},{viaDeviceN.Green},{viaDeviceN.Blue}) but " +
            $"the alternate space painted directly gives RGB({direct.Red},{direct.Green},{direct.Blue}). " +
            "§8.6.6.5 requires /None components to be passed to the tint transform on reversion.");
    }
}
