using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Tests;

/// <summary>
/// Tests for PdfImage class
/// </summary>
public class PdfImageTests
{
    [Fact]
    public void Constructor_ValidImageXObject_CreatesImage()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(100),
            [new PdfName("Height")] = new PdfInteger(200),
            [new PdfName("ColorSpace")] = new PdfName("DeviceRGB"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8)
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.NotNull(image);
        Assert.Equal(100, image.Width);
        Assert.Equal(200, image.Height);
    }

    [Fact]
    public void Constructor_NonImageXObject_ThrowsArgumentException()
    {
        var formDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Form") // Not an image
        };
        var stream = new PdfStream(formDict, new byte[100]);

        Assert.Throws<ArgumentException>(() => new PdfImage(stream));
    }

    [Fact]
    public void Width_ReturnsCorrectValue()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(640)
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Equal(640, image.Width);
    }

    [Fact]
    public void Height_ReturnsCorrectValue()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Height")] = new PdfInteger(480)
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Equal(480, image.Height);
    }

    [Fact]
    public void BitsPerComponent_DefaultsTo8()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image")
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Equal(8, image.BitsPerComponent);
    }

    [Fact]
    public void BitsPerComponent_ReturnsSpecifiedValue()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(16)
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Equal(16, image.BitsPerComponent);
    }

    [Fact]
    public void ColorSpace_DeviceGray_ReturnsCorrectly()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("ColorSpace")] = new PdfName("DeviceGray")
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Equal("DeviceGray", image.ColorSpace);
    }

    [Fact]
    public void ColorSpace_DeviceRGB_ReturnsCorrectly()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("ColorSpace")] = new PdfName("DeviceRGB")
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Equal("DeviceRGB", image.ColorSpace);
    }

    [Fact]
    public void ColorSpace_DeviceCMYK_ReturnsCorrectly()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("ColorSpace")] = new PdfName("DeviceCMYK")
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Equal("DeviceCMYK", image.ColorSpace);
    }

    [Fact]
    public void ColorSpace_IndexedFromArray_ReturnsCorrectly()
    {
        var colorSpaceArray = new PdfArray
        {
            new PdfName("Indexed"),
            new PdfName("DeviceRGB"),
            new PdfInteger(255),
            new PdfString("palette data")
        };

        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("ColorSpace")] = colorSpaceArray
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Equal("Indexed", image.ColorSpace);
    }

    [Fact]
    public void ComponentCount_DeviceGray_Returns1()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("ColorSpace")] = new PdfName("DeviceGray")
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Equal(1, image.ComponentCount);
    }

    [Fact]
    public void ComponentCount_DeviceRGB_Returns3()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("ColorSpace")] = new PdfName("DeviceRGB")
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Equal(3, image.ComponentCount);
    }

    [Fact]
    public void ComponentCount_DeviceCMYK_Returns4()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("ColorSpace")] = new PdfName("DeviceCMYK")
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Equal(4, image.ComponentCount);
    }

    [Fact]
    public void Filters_SingleFilter_ReturnsCorrectly()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Filter")] = new PdfName("DCTDecode")
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Single(image.Filters);
        Assert.Equal("DCTDecode", image.Filters[0]);
    }

    [Fact]
    public void Filters_MultipleFilters_ReturnsCorrectly()
    {
        var filterArray = new PdfArray
        {
            new PdfName("ASCII85Decode"),
            new PdfName("FlateDecode")
        };

        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Filter")] = filterArray
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Equal(2, image.Filters.Count);
        Assert.Equal("ASCII85Decode", image.Filters[0]);
        Assert.Equal("FlateDecode", image.Filters[1]);
    }

    [Fact]
    public void Filters_NoFilter_ReturnsEmptyList()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image")
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Empty(image.Filters);
    }

    [Fact]
    public void HasAlpha_WithSMask_ReturnsTrue()
    {
        var maskDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image")
        };
        var maskStream = new PdfStream(maskDict, new byte[10]);

        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("SMask")] = maskStream
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.True(image.HasAlpha);
    }

    [Fact]
    public void HasAlpha_WithMask_ReturnsTrue()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Mask")] = new PdfArray { new PdfInteger(0), new PdfInteger(255) }
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.True(image.HasAlpha);
    }

    [Fact]
    public void HasAlpha_NoMask_ReturnsFalse()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image")
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.False(image.HasAlpha);
    }

    [Fact]
    public void IsImageMask_True_ReturnsTrue()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("ImageMask")] = PdfBoolean.True
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.True(image.IsImageMask);
    }

    [Fact]
    public void IsImageMask_False_ReturnsFalse()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("ImageMask")] = PdfBoolean.False
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.False(image.IsImageMask);
    }

    [Fact]
    public void IsImageMask_NotPresent_ReturnsFalse()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image")
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.False(image.IsImageMask);
    }

    [Fact]
    public void Intent_Present_ReturnsCorrectly()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Intent")] = new PdfName("RelativeColorimetric")
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Equal("RelativeColorimetric", image.Intent);
    }

    [Fact]
    public void Intent_NotPresent_ReturnsNull()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image")
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        Assert.Null(image.Intent);
    }

    [Fact]
    public void GetExpectedDataSize_RGB_CalculatesCorrectly()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(100),
            [new PdfName("Height")] = new PdfInteger(50),
            [new PdfName("ColorSpace")] = new PdfName("DeviceRGB"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8)
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        // 100 width * 50 height * 3 components * 8 bits / 8 bits per byte
        int expected = 100 * 50 * 3 * 1;
        Assert.Equal(expected, image.GetExpectedDataSize());
    }

    [Fact]
    public void GetExpectedDataSize_Grayscale_CalculatesCorrectly()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(200),
            [new PdfName("Height")] = new PdfInteger(100),
            [new PdfName("ColorSpace")] = new PdfName("DeviceGray"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8)
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        // 200 width * 100 height * 1 component * 8 bits / 8 bits per byte
        int expected = 200 * 100 * 1 * 1;
        Assert.Equal(expected, image.GetExpectedDataSize());
    }

    [Fact]
    public void GetExpectedDataSize_1Bit_RoundsUp()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(10),
            [new PdfName("Height")] = new PdfInteger(10),
            [new PdfName("ColorSpace")] = new PdfName("DeviceGray"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(1)
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);

        // 10 width * 1 bit = 10 bits, rounds up to 2 bytes per row
        // 2 bytes * 10 rows = 20 bytes
        Assert.Equal(20, image.GetExpectedDataSize());
    }

    [Fact]
    public void IsImageXObject_ValidImage_ReturnsTrue()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image")
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        bool result = PdfImage.IsImageXObject(stream);

        Assert.True(result);
    }

    [Fact]
    public void IsImageXObject_FormXObject_ReturnsFalse()
    {
        var formDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Form")
        };
        var stream = new PdfStream(formDict, new byte[100]);

        bool result = PdfImage.IsImageXObject(stream);

        Assert.False(result);
    }

    [Fact]
    public void IsImageXObject_NoSubtype_ReturnsFalse()
    {
        var dict = new PdfDictionary();
        var stream = new PdfStream(dict, new byte[100]);

        bool result = PdfImage.IsImageXObject(stream);

        Assert.False(result);
    }

    [Fact]
    public void ToString_ContainsKeyInformation()
    {
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(640),
            [new PdfName("Height")] = new PdfInteger(480),
            [new PdfName("ColorSpace")] = new PdfName("DeviceRGB"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
            [new PdfName("Filter")] = new PdfName("DCTDecode")
        };
        var stream = new PdfStream(imageDict, new byte[100]);

        var image = new PdfImage(stream);
        var str = image.ToString();

        Assert.Contains("640x480", str);
        Assert.Contains("DeviceRGB", str);
        Assert.Contains("8bpc", str);
        Assert.Contains("DCTDecode", str);
    }

    [Fact]
    public void GetDecodedData_ReturnsStreamData()
    {
        byte[] testData = [1, 2, 3, 4, 5];
        var imageDict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image")
        };
        var stream = new PdfStream(imageDict, testData);

        var image = new PdfImage(stream);
        byte[] data = image.GetDecodedData();

        Assert.Equal(testData, data);
    }
}
