using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class PdfColorToRgbTests
{
    [Fact] public void Gray() => Assert.Equal(((byte)128,(byte)128,(byte)128),
        Round(PdfColorToRgb.ToRgb([0.5], "DeviceGray")));

    [Fact] public void Rgb() => Assert.Equal(((byte)255,(byte)0,(byte)0),
        PdfColorToRgb.ToRgb([1.0, 0.0, 0.0], "DeviceRGB"));

    [Fact] public void Cmyk_PureCyan() // c=1,m=0,y=0,k=0 → (0,255,255)
        => Assert.Equal(((byte)0,(byte)255,(byte)255), PdfColorToRgb.ToRgb([1,0,0,0], "DeviceCMYK"));

    [Fact] public void Cmyk_Black() // k=1 → (0,0,0)
        => Assert.Equal(((byte)0,(byte)0,(byte)0), PdfColorToRgb.ToRgb([0,0,0,1], "DeviceCMYK"));

    [Fact] public void UnknownThreeComps_TreatedAsRgb()
        => Assert.Equal(((byte)0,(byte)255,(byte)0), PdfColorToRgb.ToRgb([0,1,0], "Separation"));

    [Fact] public void Empty_DefaultsBlack()
        => Assert.Equal(((byte)0,(byte)0,(byte)0), PdfColorToRgb.ToRgb([], "DeviceRGB"));

    [Fact] public void Alpha() => Assert.Equal((byte)128, PdfColorToRgb.AlphaByte(0.5));

    private static (byte,byte,byte) Round((byte r, byte g, byte b) c) => (c.r, c.g, c.b);
}
