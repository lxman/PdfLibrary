using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

// Serialized: these toggle the process-wide UseIccForDeviceCmyk flag.
[Collection("PdfColorToRgbStatic")]
public class PdfColorToRgbDormancyTests
{
    [Fact]
    public void Default_is_dormant_devicecmyk_unchanged()
    {
        Assert.False(PdfColorToRgb.UseIccForDeviceCmyk);   // ships OFF

        // A battery of CMYK inputs must equal the legacy naive results exactly.
        Assert.Equal(((byte)0,(byte)255,(byte)255), PdfColorToRgb.ToRgb([1,0,0,0], "DeviceCMYK"));   // cyan
        Assert.Equal(((byte)0,(byte)0,(byte)0),     PdfColorToRgb.ToRgb([0,0,0,1], "DeviceCMYK"));   // black
        Assert.Equal(((byte)255,(byte)255,(byte)255), PdfColorToRgb.ToRgb([0,0,0,0], "DeviceCMYK")); // white
        Assert.Equal(((byte)255,(byte)153,(byte)51), PdfColorToRgb.ToRgb([0,0.4,0.8,0], "DeviceCMYK")); // orange
    }

    [Fact]
    public void Flag_on_engages_icc_then_reset()
    {
        try
        {
            PdfColorToRgb.UseIccForDeviceCmyk = true;
            // SWOP pure cyan differs from naive (0,255,255).
            Assert.NotEqual(((byte)0,(byte)255,(byte)255), PdfColorToRgb.ToRgb([1,0,0,0], "DeviceCMYK"));
        }
        finally
        {
            PdfColorToRgb.UseIccForDeviceCmyk = false;
        }
    }
}

[CollectionDefinition("PdfColorToRgbStatic", DisableParallelization = true)]
public class PdfColorToRgbStaticCollection { }
