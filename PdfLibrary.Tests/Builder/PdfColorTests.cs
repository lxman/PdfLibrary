using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

namespace PdfLibrary.Tests.Builder;

/// <summary>
/// Tests for PdfColor class including new Separation color space support.
/// </summary>
public class PdfColorTests
{
    #region Predefined Colors

    [Fact]
    public void PredefinedColors_HaveCorrectValues()
    {
        // RGB colors
        Assert.Equal(PdfColorSpace.DeviceRGB, PdfColor.Red.ColorSpace);
        Assert.Equal([1.0, 0.0, 0.0], PdfColor.Red.Components);

        Assert.Equal(PdfColorSpace.DeviceRGB, PdfColor.Green.ColorSpace);
        Assert.Equal([0.0, 1.0, 0.0], PdfColor.Green.Components);

        Assert.Equal(PdfColorSpace.DeviceRGB, PdfColor.Blue.ColorSpace);
        Assert.Equal([0.0, 0.0, 1.0], PdfColor.Blue.Components);

        // Grayscale colors
        Assert.Equal(PdfColorSpace.DeviceGray, PdfColor.Black.ColorSpace);
        Assert.Equal([0.0], PdfColor.Black.Components);

        Assert.Equal(PdfColorSpace.DeviceGray, PdfColor.White.ColorSpace);
        Assert.Equal([1.0], PdfColor.White.Components);
    }

    #endregion

    #region FromRgb

    [Fact]
    public void FromRgb_IntegerComponents_CreatesCorrectColor()
    {
        var color = PdfColor.FromRgb(128, 64, 192);

        Assert.Equal(PdfColorSpace.DeviceRGB, color.ColorSpace);
        Assert.Equal(128.0 / 255.0, color.Components[0], precision: 4);
        Assert.Equal(64.0 / 255.0, color.Components[1], precision: 4);
        Assert.Equal(192.0 / 255.0, color.Components[2], precision: 4);
    }

    [Fact]
    public void FromRgb_FloatComponents_CreatesCorrectColor()
    {
        var color = PdfColor.FromRgb(0.5, 0.25, 0.75);

        Assert.Equal(PdfColorSpace.DeviceRGB, color.ColorSpace);
        Assert.Equal([0.5, 0.25, 0.75], color.Components);
    }

    [Fact]
    public void FromRgb_ClampsOutOfRangeValues()
    {
        var color1 = PdfColor.FromRgb(-0.5, 0.5, 1.5);
        Assert.Equal([0.0, 0.5, 1.0], color1.Components);

        var color2 = PdfColor.FromRgb(-50, 128, 300);
        Assert.Equal([0.0, 128.0 / 255.0, 1.0], color2.Components);
    }

    #endregion

    #region FromGray

    [Fact]
    public void FromGray_CreatesCorrectColor()
    {
        var color = PdfColor.FromGray(0.5);

        Assert.Equal(PdfColorSpace.DeviceGray, color.ColorSpace);
        Assert.Equal([0.5], color.Components);
    }

    [Fact]
    public void FromGray_ClampsOutOfRangeValues()
    {
        var color1 = PdfColor.FromGray(-0.5);
        Assert.Equal([0.0], color1.Components);

        var color2 = PdfColor.FromGray(1.5);
        Assert.Equal([1.0], color2.Components);
    }

    #endregion

    #region FromCmyk

    [Fact]
    public void FromCmyk_CreatesCorrectColor()
    {
        var color = PdfColor.FromCmyk(0.0, 0.5, 1.0, 0.0);

        Assert.Equal(PdfColorSpace.DeviceCMYK, color.ColorSpace);
        Assert.Equal([0.0, 0.5, 1.0, 0.0], color.Components);
    }

    [Fact]
    public void FromCmyk_ClampsOutOfRangeValues()
    {
        var color = PdfColor.FromCmyk(-0.5, 0.5, 1.5, 0.5);

        Assert.Equal([0.0, 0.5, 1.0, 0.5], color.Components);
    }

    #endregion

    #region FromHex

    [Fact]
    public void FromHex_ParsesCorrectly()
    {
        var color = PdfColor.FromHex("#8040C0");

        Assert.Equal(PdfColorSpace.DeviceRGB, color.ColorSpace);
        Assert.Equal(0x80 / 255.0, color.Components[0], precision: 4);
        Assert.Equal(0x40 / 255.0, color.Components[1], precision: 4);
        Assert.Equal(0xC0 / 255.0, color.Components[2], precision: 4);
    }

    [Fact]
    public void FromHex_WithoutHash_ParsesCorrectly()
    {
        var color = PdfColor.FromHex("FF00FF");

        Assert.Equal(PdfColorSpace.DeviceRGB, color.ColorSpace);
        Assert.Equal([1.0, 0.0, 1.0], color.Components);
    }

    [Fact]
    public void FromHex_ShortFormat_ParsesCorrectly()
    {
        var color = PdfColor.FromHex("#F0F");

        Assert.Equal(PdfColorSpace.DeviceRGB, color.ColorSpace);
        Assert.Equal([1.0, 0.0, 1.0], color.Components);
    }

    #endregion

    #region FromSeparation (NEW FEATURE)

    [Fact]
    public void FromSeparation_CreatesCorrectColor()
    {
        var color = PdfColor.FromSeparation("PANTONE 185 C", 1.0);

        Assert.Equal(PdfColorSpace.Separation, color.ColorSpace);
        Assert.Equal("PANTONE 185 C", color.ColorantName);
        Assert.Equal([1.0], color.Components);
        Assert.Equal(1.0, color.Tint);
    }

    [Fact]
    public void FromSeparation_WithPartialTint_CreatesCorrectColor()
    {
        var color = PdfColor.FromSeparation("PANTONE 185 C", 0.5);

        Assert.Equal(PdfColorSpace.Separation, color.ColorSpace);
        Assert.Equal("PANTONE 185 C", color.ColorantName);
        Assert.Equal([0.5], color.Components);
        Assert.Equal(0.5, color.Tint);
    }

    [Fact]
    public void FromSeparation_ClampsTintToValidRange()
    {
        var color1 = PdfColor.FromSeparation("Spot1", -0.5);
        Assert.Equal(0.0, color1.Tint);
        Assert.Equal([0.0], color1.Components);

        var color2 = PdfColor.FromSeparation("Spot2", 1.5);
        Assert.Equal(1.0, color2.Tint);
        Assert.Equal([1.0], color2.Components);
    }

    [Fact]
    public void FromSeparation_ZeroTint_ProducesNoColor()
    {
        var color = PdfColor.FromSeparation("PANTONE 185 C", 0.0);

        Assert.Equal(0.0, color.Tint);
        Assert.Equal([0.0], color.Components);
    }

    [Fact]
    public void FromSeparation_FullTint_ProducesFullColor()
    {
        var color = PdfColor.FromSeparation("PANTONE 185 C", 1.0);

        Assert.Equal(1.0, color.Tint);
        Assert.Equal([1.0], color.Components);
    }

    [Fact]
    public void Tint_OnNonSeparationColor_ReturnsZero()
    {
        // Tint property should return 0 for non-Separation colors

        var rgb = PdfColor.Red;
        Assert.Equal(0.0, rgb.Tint);

        var gray = PdfColor.Black;
        Assert.Equal(0.0, gray.Tint);

        var cmyk = PdfColor.FromCmyk(0, 1, 1, 0);
        Assert.Equal(0.0, cmyk.Tint);
    }

    [Fact]
    public void ColorantName_OnNonSeparationColor_ReturnsNull()
    {
        var rgb = PdfColor.Red;
        Assert.Null(rgb.ColorantName);

        var gray = PdfColor.Black;
        Assert.Null(gray.ColorantName);
    }

    #endregion

    #region Builder Integration

    [Fact]
    public void SeparationColor_InBuilder_SerializesCorrectly()
    {
        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(page =>
            {
                page.AddText("Spot Color Test", 100, 700)
                    .WithColor(PdfColor.FromSeparation("PANTONE 185 C", 1.0));
            });

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);

        // Verify Separation color space is written
        Assert.Contains("/Separation", pdfContent);
        Assert.Contains("PANTONE 185 C", pdfContent);
    }

    [Fact]
    public void SeparationColor_WithPartialTint_SerializesCorrectly()
    {
        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(page =>
            {
                page.AddRectangle(100, 600, 100, 100,
                    fillColor: PdfColor.FromSeparation("Spot1", 0.5));
            });

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("/Separation", pdfContent);
        Assert.Contains("Spot1", pdfContent);
        // Tint value 0.5 should appear in content stream
        Assert.Contains("0.5", pdfContent);
    }

    [Fact]
    public void MultipleSeparationColors_SerializeCorrectly()
    {
        // Test multiple different spot colors in same document

        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(page =>
            {
                page.AddRectangle(50, 700, 50, 50,
                    fillColor: PdfColor.FromSeparation("PANTONE 185 C", 1.0));

                page.AddRectangle(150, 700, 50, 50,
                    fillColor: PdfColor.FromSeparation("PANTONE 286 C", 0.8));

                page.AddRectangle(250, 700, 50, 50,
                    fillColor: PdfColor.FromSeparation("Custom Spot", 0.6));
            });

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("PANTONE 185 C", pdfContent);
        Assert.Contains("PANTONE 286 C", pdfContent);
        Assert.Contains("Custom Spot", pdfContent);
    }

    [Fact]
    public void SeparationColor_MixedWithDeviceColors_Works()
    {
        // Test that Separation colors work alongside DeviceRGB, DeviceGray, etc.

        // Arrange & Act
        var builder = PdfDocumentBuilder.Create()
            .AddPage(page =>
            {
                page.AddRectangle(50, 700, 50, 50,
                    fillColor: PdfColor.Red); // DeviceRGB

                page.AddRectangle(150, 700, 50, 50,
                    fillColor: PdfColor.FromSeparation("Spot1", 1.0)); // Separation

                page.AddRectangle(250, 700, 50, 50,
                    fillColor: PdfColor.FromGray(0.5)); // DeviceGray
            });

        // Assert
        byte[] pdf = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Contains("/Separation", pdfContent);
        Assert.Contains("Spot1", pdfContent);
        // Should also have RGB and Gray color operators (rg/RG for RGB, g/G for Gray)
        // Note: DeviceRGB and DeviceGray are implied by the operators, not always explicitly written
        Assert.True(pdfContent.Contains(" rg") || pdfContent.Contains(" RG"), "Should contain RGB color operator");
        // Gray operator is 'g' or 'G' followed by whitespace (newline, space, or other)
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(pdfContent, @"\s+[gG]\s"), "Should contain Gray color operator");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void SeparationColor_EmptyColorantName_ThrowsOrHandles()
    {
        // Verify behavior with empty colorant name
        // Implementation should either throw ArgumentException or handle gracefully

        var color = PdfColor.FromSeparation("", 1.0);
        Assert.NotNull(color);
        // Specific behavior depends on implementation
    }

    [Fact]
    public void SeparationColor_NullColorantName_ThrowsOrHandles()
    {
        // Verify behavior with null colorant name
        // Implementation should either throw ArgumentNullException or handle gracefully

        // This test depends on whether FromSeparation accepts null
        // Update based on actual implementation
        Assert.Throws<ArgumentNullException>(() => PdfColor.FromSeparation(null!, 1.0));
    }

    [Fact]
    public void SeparationColor_VeryLongColorantName_Handles()
    {
        // Test with very long colorant name (PDF spec allows up to 127 characters in names)

        string longName = new string('A', 120);
        var color = PdfColor.FromSeparation(longName, 0.5);

        Assert.Equal(PdfColorSpace.Separation, color.ColorSpace);
        Assert.Equal(longName, color.ColorantName);
    }

    #endregion
}
