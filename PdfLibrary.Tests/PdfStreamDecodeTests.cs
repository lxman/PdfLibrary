using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Filters;

namespace PdfLibrary.Tests;

/// <summary>
/// Comprehensive tests for PdfStream.GetDecodedData() method
/// Tests stream decoding with various filters and configurations
/// </summary>
public class PdfStreamDecodeTests
{
    #region No Filter Tests

    [Fact]
    public void GetDecodedData_NoFilter_ReturnsRawData()
    {
        byte[] rawData = Encoding.ASCII.GetBytes("Hello, World!");
        var stream = new PdfStream(rawData);

        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(rawData, decoded);
        Assert.Equal("Hello, World!", Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void GetDecodedData_EmptyDictionary_ReturnsRawData()
    {
        byte[] rawData = Encoding.ASCII.GetBytes("Test data");
        var dict = new PdfDictionary();
        var stream = new PdfStream(dict, rawData);

        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(rawData, decoded);
    }

    [Fact]
    public void GetDecodedData_NullFilterValue_ReturnsRawData()
    {
        byte[] rawData = Encoding.ASCII.GetBytes("Test data");
        var dict = new PdfDictionary();
        var stream = new PdfStream(dict, rawData);

        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(rawData, decoded);
    }

    #endregion

    #region Single Filter Tests

    [Fact]
    public void GetDecodedData_FlateDecodeFilter_DecodesCorrectly()
    {
        // Prepare original data
        byte[] originalData = Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog.");

        // Compress with FlateDecode
        var filter = new FlateDecodeFilter();
        byte[] compressedData = filter.Encode(originalData);

        // Create stream with Filter dictionary
        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfName("FlateDecode")
        };
        var stream = new PdfStream(dict, compressedData);

        // Decode
        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(originalData, decoded);
        Assert.Equal("The quick brown fox jumps over the lazy dog.", Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void GetDecodedData_ASCIIHexDecode_DecodesCorrectly()
    {
        // "Hello" in ASCII hex: 48656C6C6F
        byte[] encodedData = Encoding.ASCII.GetBytes("48656C6C6F>");

        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfName("ASCIIHexDecode")
        };
        var stream = new PdfStream(dict, encodedData);

        byte[] decoded = stream.GetDecodedData();

        Assert.Equal("Hello", Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void GetDecodedData_ASCII85Decode_DecodesCorrectly()
    {
        // "Hello" encoded in ASCII85
        var originalData = Encoding.ASCII.GetBytes("Hello");
        var filter = new Ascii85DecodeFilter();
        byte[] encodedData = filter.Encode(originalData);

        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfName("ASCII85Decode")
        };
        var stream = new PdfStream(dict, encodedData);

        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(originalData, decoded);
        Assert.Equal("Hello", Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void GetDecodedData_RunLengthDecode_DecodesCorrectly()
    {
        var originalData = Encoding.ASCII.GetBytes("Test");
        var filter = new RunLengthDecodeFilter();
        byte[] encodedData = filter.Encode(originalData);

        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfName("RunLengthDecode")
        };
        var stream = new PdfStream(dict, encodedData);

        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(originalData, decoded);
    }

    #endregion

    #region Multiple Filters Tests

    [Fact]
    public void GetDecodedData_TwoFilters_AppliesInSequence()
    {
        // Original data
        byte[] originalData = Encoding.ASCII.GetBytes("Test data for multiple filters");

        // Apply filters in sequence: first ASCII85, then Flate
        var ascii85Filter = new Ascii85DecodeFilter();
        byte[] step1 = ascii85Filter.Encode(originalData);

        var flateFilter = new FlateDecodeFilter();
        byte[] step2 = flateFilter.Encode(step1);

        // Create stream with filter array
        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfArray
            {
                new PdfName("FlateDecode"),
                new PdfName("ASCII85Decode")
            }
        };
        var stream = new PdfStream(dict, step2);

        // Decode (should apply in reverse order)
        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(originalData, decoded);
    }

    [Fact]
    public void GetDecodedData_ThreeFilters_AppliesInSequence()
    {
        // Original data
        byte[] originalData = Encoding.ASCII.GetBytes("ABC");

        // Apply filters: RunLength -> ASCII85 -> Flate
        var rlFilter = new RunLengthDecodeFilter();
        byte[] step1 = rlFilter.Encode(originalData);

        var ascii85Filter = new Ascii85DecodeFilter();
        byte[] step2 = ascii85Filter.Encode(step1);

        var flateFilter = new FlateDecodeFilter();
        byte[] step3 = flateFilter.Encode(step2);

        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfArray
            {
                new PdfName("FlateDecode"),
                new PdfName("ASCII85Decode"),
                new PdfName("RunLengthDecode")
            }
        };
        var stream = new PdfStream(dict, step3);

        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(originalData, decoded);
    }

    [Fact]
    public void GetDecodedData_EmptyFilterArray_ReturnsRawData()
    {
        byte[] rawData = Encoding.ASCII.GetBytes("Test");
        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfArray() // Empty array
        };
        var stream = new PdfStream(dict, rawData);

        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(rawData, decoded);
    }

    #endregion

    #region Decode Parameters Tests

    [Fact]
    public void GetDecodedData_WithDecodeParams_PassesToFilter()
    {
        byte[] originalData = Encoding.ASCII.GetBytes("Test with params");
        var filter = new FlateDecodeFilter();
        byte[] encodedData = filter.Encode(originalData);

        var decodeParams = new PdfDictionary
        {
            [new PdfName("Predictor")] = new PdfInteger(1) // No prediction
        };

        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfName("FlateDecode"),
            [PdfName.DecodeParms] = decodeParams
        };
        var stream = new PdfStream(dict, encodedData);

        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(originalData, decoded);
    }

    [Fact]
    public void GetDecodedData_WithDecodeParamsArray_AppliesToEachFilter()
    {
        byte[] originalData = Encoding.ASCII.GetBytes("Test");

        // Encode with two filters
        var ascii85Filter = new Ascii85DecodeFilter();
        byte[] step1 = ascii85Filter.Encode(originalData);

        var flateFilter = new FlateDecodeFilter();
        byte[] step2 = flateFilter.Encode(step1);

        // Decode params for each filter
        var decodeParamsArray = new PdfArray
        {
            new PdfDictionary { [new PdfName("Predictor")] = new PdfInteger(1) }, // For FlateDecode
            PdfNull.Instance // No params for ASCII85Decode
        };

        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfArray
            {
                new PdfName("FlateDecode"),
                new PdfName("ASCII85Decode")
            },
            [PdfName.DecodeParms] = decodeParamsArray
        };
        var stream = new PdfStream(dict, step2);

        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(originalData, decoded);
    }

    [Fact]
    public void GetDecodedData_DecodeParamsWithVariousTypes_ConvertsCorrectly()
    {
        byte[] originalData = Encoding.ASCII.GetBytes("Test");
        var filter = new FlateDecodeFilter();
        byte[] encodedData = filter.Encode(originalData);

        var decodeParams = new PdfDictionary
        {
            [new PdfName("Predictor")] = new PdfInteger(1),
            [new PdfName("Columns")] = new PdfInteger(8),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
            [new PdfName("BlackIs1")] = PdfBoolean.False
        };

        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfName("FlateDecode"),
            [PdfName.DecodeParms] = decodeParams
        };
        var stream = new PdfStream(dict, encodedData);

        // Should not throw
        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(originalData, decoded);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void GetDecodedData_UnsupportedFilter_ThrowsNotSupportedException()
    {
        byte[] rawData = Encoding.ASCII.GetBytes("Test");
        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfName("UnsupportedFilter")
        };
        var stream = new PdfStream(dict, rawData);

        Assert.Throws<NotSupportedException>(() => stream.GetDecodedData());
    }

    [Fact]
    public void GetDecodedData_InvalidFilterInArray_ThrowsNotSupportedException()
    {
        byte[] originalData = Encoding.ASCII.GetBytes("Test");
        var filter = new FlateDecodeFilter();
        byte[] encodedData = filter.Encode(originalData);

        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfArray
            {
                new PdfName("FlateDecode"),
                new PdfName("InvalidFilter")
            }
        };
        var stream = new PdfStream(dict, encodedData);

        Assert.Throws<NotSupportedException>(() => stream.GetDecodedData());
    }

    [Fact]
    public void GetDecodedData_NonNameFilterInArray_SkipsEntry()
    {
        byte[] originalData = Encoding.ASCII.GetBytes("Test");
        var filter = new FlateDecodeFilter();
        byte[] encodedData = filter.Encode(originalData);

        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfArray
            {
                new PdfName("FlateDecode"),
                new PdfInteger(42) // Invalid - not a name
            }
        };
        var stream = new PdfStream(dict, encodedData);

        // Should decode with FlateDecode and skip the integer
        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(originalData, decoded);
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public void GetDecodedData_PDFTextStream_DecodesCorrectly()
    {
        // Simulate a typical PDF text stream
        string pdfText = "BT\n/F1 12 Tf\n100 700 Td\n(Hello, World!) Tj\nET";
        byte[] originalData = Encoding.ASCII.GetBytes(pdfText);

        var filter = new FlateDecodeFilter();
        byte[] compressedData = filter.Encode(originalData);

        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfName("FlateDecode")
        };
        var stream = new PdfStream(dict, compressedData);

        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(pdfText, Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void GetDecodedData_LargeStream_HandlesEfficiently()
    {
        // Create large data (1MB)
        byte[] largeData = new byte[1024 * 1024];
        for (int i = 0; i < largeData.Length; i++)
            largeData[i] = (byte)(i % 256);

        var filter = new FlateDecodeFilter();
        byte[] compressedData = filter.Encode(largeData);

        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfName("FlateDecode")
        };
        var stream = new PdfStream(dict, compressedData);

        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(largeData, decoded);
    }

    [Fact]
    public void GetDecodedData_BinaryImageData_PreservesBytes()
    {
        // Simulate binary image data
        byte[] imageData = new byte[256];
        for (int i = 0; i < 256; i++)
            imageData[i] = (byte)i;

        var filter = new FlateDecodeFilter();
        byte[] compressedData = filter.Encode(imageData);

        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfName("FlateDecode"),
            [PdfName.Width] = new PdfInteger(16),
            [PdfName.Height] = new PdfInteger(16),
            [PdfName.BitsPerComponent] = new PdfInteger(8)
        };
        var stream = new PdfStream(dict, compressedData);

        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(imageData, decoded);
        // Verify all bytes 0-255 are present
        for (int i = 0; i < 256; i++)
            Assert.Equal((byte)i, decoded[i]);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GetDecodedData_FilterObjectNotNameOrArray_ReturnsRawData()
    {
        byte[] rawData = Encoding.ASCII.GetBytes("Test");
        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfInteger(42) // Invalid type
        };
        var stream = new PdfStream(dict, rawData);

        byte[] decoded = stream.GetDecodedData();

        Assert.Equal(rawData, decoded);
    }

    [Fact]
    public void GetDecodedData_EmptyRawData_ReturnsEmpty()
    {
        byte[] emptyData = [];
        var filter = new FlateDecodeFilter();
        byte[] compressedEmpty = filter.Encode(emptyData);

        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfName("FlateDecode")
        };
        var stream = new PdfStream(dict, compressedEmpty);

        byte[] decoded = stream.GetDecodedData();

        Assert.Empty(decoded);
    }

    [Fact]
    public void GetDecodedData_MultipleCallsSameStream_ReturnsSameResult()
    {
        byte[] originalData = Encoding.ASCII.GetBytes("Consistency test");
        var filter = new FlateDecodeFilter();
        byte[] compressedData = filter.Encode(originalData);

        var dict = new PdfDictionary
        {
            [PdfName.Filter] = new PdfName("FlateDecode")
        };
        var stream = new PdfStream(dict, compressedData);

        byte[] decoded1 = stream.GetDecodedData();
        byte[] decoded2 = stream.GetDecodedData();
        byte[] decoded3 = stream.GetDecodedData();

        Assert.Equal(decoded1, decoded2);
        Assert.Equal(decoded2, decoded3);
    }

    #endregion
}
