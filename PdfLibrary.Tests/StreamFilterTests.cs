using PdfLibrary.Filters;

namespace PdfLibrary.Tests;

public class StreamFilterTests
{
    #region FlateDecode Tests

    [Fact]
    public void FlateDecode_HasCorrectFilterName()
    {
        var filter = new FlateDecodeFilter();
        Assert.Equal("FlateDecode", filter.Name);
    }

    [Fact]
    public void FlateDecode_ThrowsOnNullData_Encode()
    {
        var filter = new FlateDecodeFilter();
        Assert.Throws<ArgumentNullException>(() => filter.Encode(null!));
    }

    [Fact]
    public void FlateDecode_ThrowsOnNullData_Decode()
    {
        var filter = new FlateDecodeFilter();
        Assert.Throws<ArgumentNullException>(() => filter.Decode(null!));
    }

    [Fact]
    public void FlateDecode_EncodesSimpleData()
    {
        var filter = new FlateDecodeFilter();
        byte[] data = System.Text.Encoding.ASCII.GetBytes("Hello, World!");

        byte[] encoded = filter.Encode(data);

        Assert.NotNull(encoded);
        Assert.NotEmpty(encoded);
        // Compressed data should be different from original
        Assert.NotEqual(data, encoded);
    }

    [Fact]
    public void FlateDecode_DecodesSimpleData()
    {
        var filter = new FlateDecodeFilter();
        byte[] original = System.Text.Encoding.ASCII.GetBytes("Hello, World!");

        byte[] encoded = filter.Encode(original);
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void FlateDecode_RoundTrip_SimpleText()
    {
        var filter = new FlateDecodeFilter();
        byte[] original = System.Text.Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog.");

        byte[] encoded = filter.Encode(original);
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal(original, decoded);
        Assert.Equal("The quick brown fox jumps over the lazy dog.",
            System.Text.Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void FlateDecode_RoundTrip_BinaryData()
    {
        var filter = new FlateDecodeFilter();
        var original = new byte[256];
        for (var i = 0; i < 256; i++)
            original[i] = (byte)i;

        byte[] encoded = filter.Encode(original);
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void FlateDecode_RoundTrip_EmptyData()
    {
        var filter = new FlateDecodeFilter();
        byte[] original = [];

        byte[] encoded = filter.Encode(original);
        byte[] decoded = filter.Decode(encoded);

        Assert.Empty(decoded);
    }

    [Fact]
    public void FlateDecode_WithPredictor1_NoTransform()
    {
        // Predictor = 1 means no prediction
        var filter = new FlateDecodeFilter();
        byte[] original = System.Text.Encoding.ASCII.GetBytes("ABCDEFGH");

        byte[] encoded = filter.Encode(original);

        var parameters = new Dictionary<string, object>
        {
            { "Predictor", 1 }
        };

        byte[] decoded = filter.Decode(encoded, parameters);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void FlateDecode_WithPngPredictor_None()
    {
        // PNG predictor 0 (None) - simplest case
        // Format: [predictor_type] [row_data]
        var filter = new FlateDecodeFilter();

        // Create simple 4x1 image: RGBA = [255,0,0,255, 0,255,0,255, 0,0,255,255, 255,255,255,255]
        // With PNG predictor 0 (None), each row is: [0] [original_data]
        byte[] imageData =
        [
            255, 0, 0, 255,    // Red pixel
            0, 255, 0, 255,    // Green pixel
            0, 0, 255, 255,    // Blue pixel
            255, 255, 255, 255 // White pixel
        ];

        // Add PNG predictor type byte (0 = None)
        var withPredictor = new byte[imageData.Length + 1];
        withPredictor[0] = 0; // PNG predictor type: None
        Array.Copy(imageData, 0, withPredictor, 1, imageData.Length);

        byte[] encoded = filter.Encode(withPredictor);

        var parameters = new Dictionary<string, object>
        {
            { "Predictor", 10 },           // PNG predictor
            { "Columns", 4 },              // 4 pixels wide
            { "Colors", 4 },               // RGBA (4 color components)
            { "BitsPerComponent", 8 }      // 8 bits per component
        };

        byte[] decoded = filter.Decode(encoded, parameters);

        Assert.Equal(imageData, decoded);
    }

    [Fact]
    public void FlateDecode_WithPngPredictor_Sub()
    {
        // PNG predictor 1 (Sub) - each byte is difference from left neighbor
        var filter = new FlateDecodeFilter();

        // Original: [10, 20, 30, 40]
        // Encoded with Sub predictor: [10, 10, 10, 10] (each is difference from left)
        byte[] encodedRow =
        [
            1,   // PNG predictor type: Sub
            10,  // First byte: 10 (no left neighbor)
            10,  // Second: 10 (20 - 10 = 10)
            10,  // Third: 10 (30 - 20 = 10)
            10   // Fourth: 10 (40 - 30 = 10)
        ];

        byte[] compressed = filter.Encode(encodedRow);

        var parameters = new Dictionary<string, object>
        {
            { "Predictor", 11 },           // PNG Sub predictor
            { "Columns", 4 },              // 4 bytes
            { "Colors", 1 },               // Grayscale
            { "BitsPerComponent", 8 }
        };

        byte[] decoded = filter.Decode(compressed, parameters);

        Assert.Equal(new byte[] { 10, 20, 30, 40 }, decoded);
    }

    [Fact]
    public void FlateDecode_WithPngPredictor_Up()
    {
        // PNG predictor 2 (Up) - each byte is difference from above neighbor
        var filter = new FlateDecodeFilter();

        // Two rows: [10, 20] then [15, 25]
        // Encoded: Row 1: [2][10, 20] (no row above)
        //          Row 2: [2][5, 5] (differences from row above)
        byte[] encodedData =
        [
            2, 10, 20,  // First row with Up predictor
            2, 5, 5     // Second row: differences from first row
        ];

        byte[] compressed = filter.Encode(encodedData);

        var parameters = new Dictionary<string, object>
        {
            { "Predictor", 12 },           // PNG Up predictor
            { "Columns", 2 },              // 2 bytes per row
            { "Colors", 1 },
            { "BitsPerComponent", 8 }
        };

        byte[] decoded = filter.Decode(compressed, parameters);

        Assert.Equal(new byte[] { 10, 20, 15, 25 }, decoded);
    }

    [Fact]
    public void FlateDecode_WithPngPredictor_Average()
    {
        // PNG predictor 3 (Average) - each byte is difference from average of left and above
        var filter = new FlateDecodeFilter();

        // Simple case: [0, 10] then [10, ?]
        // For position [1,1]: left=10, above=10, average=10, so encoded value = ? - 10
        byte[] encodedData =
        [
            3, 0, 10,   // First row
            3, 10, 10   // Second row: [10, 20] where 20 = 10 + (10+10)/2 = 10 + 10
        ];

        byte[] compressed = filter.Encode(encodedData);

        var parameters = new Dictionary<string, object>
        {
            { "Predictor", 13 },           // PNG Average predictor
            { "Columns", 2 },
            { "Colors", 1 },
            { "BitsPerComponent", 8 }
        };

        byte[] decoded = filter.Decode(compressed, parameters);

        Assert.Equal(4, decoded.Length);
    }

    [Fact]
    public void FlateDecode_WithPngPredictor_Paeth()
    {
        // PNG predictor 4 (Paeth) - uses Paeth predictor algorithm
        var filter = new FlateDecodeFilter();

        byte[] encodedData =
        [
            4, 100, 110, 120,  // First row with Paeth predictor
            4, 5, 5, 5         // Second row
        ];

        byte[] compressed = filter.Encode(encodedData);

        var parameters = new Dictionary<string, object>
        {
            { "Predictor", 14 },           // PNG Paeth predictor
            { "Columns", 3 },
            { "Colors", 1 },
            { "BitsPerComponent", 8 }
        };

        byte[] decoded = filter.Decode(compressed, parameters);

        Assert.Equal(6, decoded.Length);
        Assert.Equal(100, decoded[0]);
    }

    [Fact]
    public void FlateDecode_WithTiffPredictor()
    {
        // TIFF Predictor 2 - horizontal differencing
        var filter = new FlateDecodeFilter();

        // Original: [10, 20, 30, 40]
        // TIFF encoded: [10, 10, 10, 10] (each is difference from left)
        byte[] encodedData = [10, 10, 10, 10];

        byte[] compressed = filter.Encode(encodedData);

        var parameters = new Dictionary<string, object>
        {
            { "Predictor", 2 },            // TIFF Predictor
            { "Columns", 4 },
            { "Colors", 1 },
            { "BitsPerComponent", 8 }
        };

        byte[] decoded = filter.Decode(compressed, parameters);

        Assert.Equal(new byte[] { 10, 20, 30, 40 }, decoded);
    }

    [Fact]
    public void FlateDecode_WithTiffPredictor_RGB()
    {
        // TIFF Predictor with RGB (3 colors)
        var filter = new FlateDecodeFilter();

        // Original RGB pixels: [(100,50,25), (110,60,35)]
        // TIFF encoded: [100,50,25, 10,10,10] (second pixel is difference)
        byte[] encodedData = [100, 50, 25, 10, 10, 10];

        byte[] compressed = filter.Encode(encodedData);

        var parameters = new Dictionary<string, object>
        {
            { "Predictor", 2 },
            { "Columns", 2 },              // 2 pixels
            { "Colors", 3 },               // RGB
            { "BitsPerComponent", 8 }
        };

        byte[] decoded = filter.Decode(compressed, parameters);

        Assert.Equal(new byte[] { 100, 50, 25, 110, 60, 35 }, decoded);
    }

    [Fact]
    public void FlateDecode_WithPredictor_DefaultParameters()
    {
        // Test that default parameters are used when not specified
        var filter = new FlateDecodeFilter();

        byte[] encodedData = [2, 10, 20];  // PNG Up predictor, 2 bytes
        byte[] compressed = filter.Encode(encodedData);

        var parameters = new Dictionary<string, object>
        {
            { "Predictor", 12 }  // Only predictor specified, others should default
            // Defaults: Columns=1, Colors=1, BitsPerComponent=8
        };

        byte[] decoded = filter.Decode(compressed, parameters);

        Assert.NotEmpty(decoded);
    }

    [Fact]
    public void FlateDecode_CompressesRepetitiveData()
    {
        var filter = new FlateDecodeFilter();

        // Highly repetitive data should compress well
        var original = new byte[1000];
        Array.Fill(original, (byte)'A');

        byte[] encoded = filter.Encode(original);

        // Compressed size should be much smaller than original
        Assert.True(encoded.Length < original.Length / 10,
            $"Expected compressed size < {original.Length / 10}, got {encoded.Length}");

        byte[] decoded = filter.Decode(encoded);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void FlateDecode_LargeData()
    {
        var filter = new FlateDecodeFilter();

        // Test with larger data (10KB)
        var original = new byte[10240];
        var random = new Random(42); // Fixed seed for reproducibility
        random.NextBytes(original);

        byte[] encoded = filter.Encode(original);
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void FlateDecode_MultipleRows_PngPredictor()
    {
        var filter = new FlateDecodeFilter();

        // 3 rows of 4 bytes each, all using PNG None predictor
        byte[] encodedData =
        [
            0, 1, 2, 3, 4,      // Row 1
            0, 5, 6, 7, 8,      // Row 2
            0, 9, 10, 11, 12    // Row 3
        ];

        byte[] compressed = filter.Encode(encodedData);

        var parameters = new Dictionary<string, object>
        {
            { "Predictor", 10 },
            { "Columns", 4 },
            { "Colors", 1 },
            { "BitsPerComponent", 8 }
        };

        byte[] decoded = filter.Decode(compressed, parameters);

        byte[] expected = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        Assert.Equal(expected, decoded);
    }

    #endregion

    #region ASCIIHexDecode Tests

    [Fact]
    public void ASCIIHexDecode_HasCorrectFilterName()
    {
        var filter = new AsciiHexDecodeFilter();
        Assert.Equal("ASCIIHexDecode", filter.Name);
    }

    [Fact]
    public void ASCIIHexDecode_ThrowsOnNullData_Encode()
    {
        var filter = new AsciiHexDecodeFilter();
        Assert.Throws<ArgumentNullException>(() => filter.Encode(null!));
    }

    [Fact]
    public void ASCIIHexDecode_ThrowsOnNullData_Decode()
    {
        var filter = new AsciiHexDecodeFilter();
        Assert.Throws<ArgumentNullException>(() => filter.Decode(null!));
    }

    [Fact]
    public void ASCIIHexDecode_EncodesSimpleData()
    {
        var filter = new AsciiHexDecodeFilter();
        byte[] data = [0x41, 0x42, 0x43]; // ABC

        byte[] encoded = filter.Encode(data);

        string result = System.Text.Encoding.ASCII.GetString(encoded);
        Assert.Equal("414243>", result); // EOD marker included
    }

    [Fact]
    public void ASCIIHexDecode_DecodesSimpleData()
    {
        var filter = new AsciiHexDecodeFilter();
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes("414243>");

        byte[] decoded = filter.Decode(encoded);

        Assert.Equal("ABC"u8.ToArray(), decoded);
    }

    [Fact]
    public void ASCIIHexDecode_RoundTrip()
    {
        var filter = new AsciiHexDecodeFilter();
        byte[] original = [0x00, 0x01, 0x7F, 0x80, 0xFF];

        byte[] encoded = filter.Encode(original);
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void ASCIIHexDecode_RoundTrip_Text()
    {
        var filter = new AsciiHexDecodeFilter();
        byte[] original = System.Text.Encoding.ASCII.GetBytes("Hello, World!");

        byte[] encoded = filter.Encode(original);
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal(original, decoded);
        Assert.Equal("Hello, World!", System.Text.Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void ASCIIHexDecode_HandlesLowercaseHex()
    {
        var filter = new AsciiHexDecodeFilter();
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes("6162636465>");

        byte[] decoded = filter.Decode(encoded);

        Assert.Equal("abcde", System.Text.Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void ASCIIHexDecode_HandlesMixedCaseHex()
    {
        var filter = new AsciiHexDecodeFilter();
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes("4142636465>");

        byte[] decoded = filter.Decode(encoded);

        Assert.Equal("ABcde"u8.ToArray(), decoded);
    }

    [Fact]
    public void ASCIIHexDecode_HandlesWhitespace()
    {
        var filter = new AsciiHexDecodeFilter();
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes("41 42 43\n44 45>");

        byte[] decoded = filter.Decode(encoded);

        Assert.Equal("ABCDE"u8.ToArray(), decoded);
    }

    [Fact]
    public void ASCIIHexDecode_HandlesOddNumberOfDigits()
    {
        // If odd number of digits, last digit is assumed to be followed by 0
        var filter = new AsciiHexDecodeFilter();
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes("4142>");

        byte[] decoded = filter.Decode(encoded);

        Assert.Equal("AB"u8.ToArray(), decoded);
    }

    [Fact]
    public void ASCIIHexDecode_HandlesOddDigitWithImplicitZero()
    {
        var filter = new AsciiHexDecodeFilter();
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes("414>");

        byte[] decoded = filter.Decode(encoded);

        // "414>" should decode to [0x41, 0x40] - second digit is 4 with implied 0
        Assert.Equal("A@"u8.ToArray(), decoded);
    }

    [Fact]
    public void ASCIIHexDecode_ThrowsOnInvalidHexCharacter()
    {
        var filter = new AsciiHexDecodeFilter();
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes("41G2>");

        Assert.Throws<InvalidDataException>(() => filter.Decode(encoded));
    }

    [Fact]
    public void ASCIIHexDecode_EmptyData()
    {
        var filter = new AsciiHexDecodeFilter();
        byte[] original = [];

        byte[] encoded = filter.Encode(original);
        byte[] decoded = filter.Decode(encoded);

        Assert.Empty(decoded);
    }

    [Fact]
    public void ASCIIHexDecode_StopsAtEODMarker()
    {
        var filter = new AsciiHexDecodeFilter();
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes("4142>4344");

        byte[] decoded = filter.Decode(encoded);

        // Should only decode "4142", stop at >
        Assert.Equal("AB"u8.ToArray(), decoded);
    }

    #endregion

    #region ASCII85Decode Tests

    [Fact]
    public void ASCII85Decode_HasCorrectFilterName()
    {
        var filter = new Ascii85DecodeFilter();
        Assert.Equal("ASCII85Decode", filter.Name);
    }

    [Fact]
    public void ASCII85Decode_ThrowsOnNullData_Encode()
    {
        var filter = new Ascii85DecodeFilter();
        Assert.Throws<ArgumentNullException>(() => filter.Encode(null!));
    }

    [Fact]
    public void ASCII85Decode_ThrowsOnNullData_Decode()
    {
        var filter = new Ascii85DecodeFilter();
        Assert.Throws<ArgumentNullException>(() => filter.Decode(null!));
    }

    [Fact]
    public void ASCII85Decode_EncodesSimpleData()
    {
        var filter = new Ascii85DecodeFilter();
        byte[] data = [0x41, 0x42, 0x43, 0x44]; // ABCD

        byte[] encoded = filter.Encode(data);

        string result = System.Text.Encoding.ASCII.GetString(encoded);
        Assert.EndsWith("~>", result); // Should end with EOD marker
        Assert.NotEmpty(result);
    }

    [Fact]
    public void ASCII85Decode_RoundTrip()
    {
        var filter = new Ascii85DecodeFilter();
        byte[] original = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07];

        byte[] encoded = filter.Encode(original);
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void ASCII85Decode_RoundTrip_Text()
    {
        var filter = new Ascii85DecodeFilter();
        byte[] original = System.Text.Encoding.ASCII.GetBytes("Hello, World!");

        byte[] encoded = filter.Encode(original);
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal(original, decoded);
        Assert.Equal("Hello, World!", System.Text.Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void ASCII85Decode_HandlesZeroShorthand()
    {
        // 'z' is shorthand for four zero bytes (0x00000000)
        var filter = new Ascii85DecodeFilter();
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes("z~>");

        byte[] decoded = filter.Decode(encoded);

        Assert.Equal("\0\0\0\0"u8.ToArray(), decoded);
    }

    [Fact]
    public void ASCII85Decode_HandlesMultipleZeros()
    {
        var filter = new Ascii85DecodeFilter();
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes("zz~>");

        byte[] decoded = filter.Decode(encoded);

        Assert.Equal("\0\0\0\0\0\0\0\0"u8.ToArray(), decoded);
    }

    [Fact]
    public void ASCII85Decode_ThrowsOnInvalidZPlacement()
    {
        // 'z' can only appear at tuple boundaries
        var filter = new Ascii85DecodeFilter();
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes("!z~>");

        Assert.Throws<InvalidDataException>(() => filter.Decode(encoded));
    }

    [Fact]
    public void ASCII85Decode_HandlesWhitespace()
    {
        var filter = new Ascii85DecodeFilter();
        byte[] original = [0x41, 0x42, 0x43, 0x44];

        byte[] encoded = filter.Encode(original);
        string encodedStr = System.Text.Encoding.ASCII.GetString(encoded);

        // Add whitespace
        string withSpaces = encodedStr.Replace("~>", " \n\t ~>");
        byte[] spacedEncoded = System.Text.Encoding.ASCII.GetBytes(withSpaces);

        byte[] decoded = filter.Decode(spacedEncoded);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void ASCII85Decode_ThrowsOnInvalidCharacter()
    {
        var filter = new Ascii85DecodeFilter();
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes("!!v!!~>"); // 'v' is > 'u', invalid

        Assert.Throws<InvalidDataException>(() => filter.Decode(encoded));
    }

    [Fact]
    public void ASCII85Decode_EmptyData()
    {
        var filter = new Ascii85DecodeFilter();
        byte[] original = [];

        byte[] encoded = filter.Encode(original);
        byte[] decoded = filter.Decode(encoded);

        Assert.Empty(decoded);
    }

    [Fact]
    public void ASCII85Decode_HandlesPartialTuple()
    {
        // Test encoding/decoding when data length is not multiple of 4
        var filter = new Ascii85DecodeFilter();
        byte[] original = [0x41, 0x42, 0x43]; // 3 bytes

        byte[] encoded = filter.Encode(original);
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void ASCII85Decode_StopsAtEODMarker()
    {
        var filter = new Ascii85DecodeFilter();
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes("z~>!!!!!!");

        byte[] decoded = filter.Decode(encoded);

        // Should only decode "z", stop at ~>
        Assert.Equal("\0\0\0\0"u8.ToArray(), decoded);
    }

    [Fact]
    public void ASCII85Decode_LargeData()
    {
        var filter = new Ascii85DecodeFilter();
        var original = new byte[1024];
        var random = new Random(42);
        random.NextBytes(original);

        byte[] encoded = filter.Encode(original);
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal(original, decoded);
    }

    #endregion

    #region RunLengthDecode Tests

    [Fact]
    public void RunLengthDecode_DecodesLiteralSequence()
    {
        // Encode: "ABC" as literal sequence
        // Format: [length-1] [bytes...]
        byte[] encoded = [2, (byte)'A', (byte)'B', (byte)'C', 128]; // 2 means 3 bytes, 128 is EOD

        var filter = new RunLengthDecodeFilter();
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal("ABC", System.Text.Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void RunLengthDecode_DecodesRunSequence()
    {
        // Encode: Five 'A's as a run
        // Format: [257-length] [byte]
        byte[] encoded = [252, (byte)'A', 128]; // 257-252 = 5 repetitions, 128 is EOD

        var filter = new RunLengthDecodeFilter();
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal("AAAAA", System.Text.Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void RunLengthDecode_DecodesMixedSequence()
    {
        // Mix of literal and run: "ABCCCDE"
        byte[] encoded =
        [
            1, (byte)'A', (byte)'B',  // 2 literals (A, B)
            254, (byte)'C',            // 3 repetitions of C (257-254=3)
            1, (byte)'D', (byte)'E',   // 2 literals (D, E)
            128                        // EOD marker
        ];

        var filter = new RunLengthDecodeFilter();
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal("ABCCCDE", System.Text.Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void RunLengthDecode_HandlesEODMarker()
    {
        // Data with EOD marker in the middle - should stop decoding
        byte[] encoded =
        [
            2, (byte)'A', (byte)'B', (byte)'C',  // 3 literals
            128,                                  // EOD marker
            0, (byte)'X'                          // This should be ignored
        ];

        var filter = new RunLengthDecodeFilter();
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal("ABC", System.Text.Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void RunLengthDecode_HandlesMaximumRun()
    {
        // Maximum run length is 128 repetitions (257-129=128)
        byte[] encoded = [129, (byte)'X', 128]; // 128 X's

        var filter = new RunLengthDecodeFilter();
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal(128, decoded.Length);
        Assert.All(decoded, b => Assert.Equal((byte)'X', b));
    }

    [Fact]
    public void RunLengthDecode_HandlesMaximumLiteral()
    {
        // Maximum literal length is 128 bytes (length byte = 127 means 128 bytes)
        var literal = new byte[128];
        for (var i = 0; i < 128; i++)
            literal[i] = (byte)(i % 26 + 65); // A-Z repeated

        var encoded = new byte[130];
        encoded[0] = 127; // 128 bytes follow
        Array.Copy(literal, 0, encoded, 1, 128);
        encoded[129] = 128; // EOD

        var filter = new RunLengthDecodeFilter();
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal(128, decoded.Length);
        Assert.Equal(literal, decoded);
    }

    [Fact]
    public void RunLengthEncode_EncodesSimpleRun()
    {
        byte[] data = [(byte)'A', (byte)'A', (byte)'A', (byte)'A', (byte)'A'];

        var filter = new RunLengthDecodeFilter();
        byte[] encoded = filter.Encode(data);

        // Should be: [252, 'A', 128] = run of 5 A's + EOD
        Assert.Equal(3, encoded.Length);
        Assert.Equal(252, encoded[0]); // 257-5 = 252
        Assert.Equal((byte)'A', encoded[1]);
        Assert.Equal(128, encoded[2]); // EOD
    }

    [Fact]
    public void RunLengthEncode_EncodesLiteralSequence()
    {
        byte[] data = [(byte)'A', (byte)'B', (byte)'C'];

        var filter = new RunLengthDecodeFilter();
        byte[] encoded = filter.Encode(data);

        // Should be: [2, 'A', 'B', 'C', 128] = 3 literals + EOD
        Assert.Equal(5, encoded.Length);
        Assert.Equal(2, encoded[0]); // length-1 = 2 for 3 bytes
        Assert.Equal((byte)'A', encoded[1]);
        Assert.Equal((byte)'B', encoded[2]);
        Assert.Equal((byte)'C', encoded[3]);
        Assert.Equal(128, encoded[4]); // EOD
    }

    [Fact]
    public void RunLengthDecode_RoundTrip()
    {
        byte[] original = System.Text.Encoding.ASCII.GetBytes("AAABBBCCCCCCDEEE");

        var filter = new RunLengthDecodeFilter();
        byte[] encoded = filter.Encode(original);
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void RunLengthDecode_EmptyData()
    {
        byte[] encoded = [128]; // Just EOD marker

        var filter = new RunLengthDecodeFilter();
        byte[] decoded = filter.Decode(encoded);

        Assert.Empty(decoded);
    }

    #endregion

    #region LZWDecode Tests

    // Note: Comprehensive LZW codec tests are in Compressors.Lzw.Tests
    // These tests focus on the PdfLibrary filter wrapper interface

    [Fact]
    public void LZWDecode_EncodeDecodeRoundTrip()
    {
        var filter = new LzwDecodeFilter();
        byte[] original = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07];

        byte[] encoded = filter.Encode(original);
        byte[] decoded = filter.Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void LZWDecode_ThrowsOnNullData()
    {
        var filter = new LzwDecodeFilter();

        Assert.Throws<ArgumentNullException>(() => filter.Decode(null!));
        Assert.Throws<ArgumentNullException>(() => filter.Encode(null!));
    }

    [Fact]
    public void LZWDecode_EarlyChangeParameter()
    {
        // Test that EarlyChange parameter is passed to the decoder
        var filter = new LzwDecodeFilter();
        byte[] original = "Hello, LZW World!"u8.ToArray();

        // Encode with default (EarlyChange=true)
        byte[] encoded = filter.Encode(original);

        // Decode with explicit EarlyChange=1 (true)
        var parameters = new Dictionary<string, object> { { "EarlyChange", 1 } };
        byte[] decoded = filter.Decode(encoded, parameters);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void LZWDecode_HasCorrectFilterName()
    {
        var filter = new LzwDecodeFilter();
        Assert.Equal("LZWDecode", filter.Name);
    }

    #endregion

    #region DCTDecode Tests

    [Fact]
    public void DCTDecode_DecodesValidJPEG()
    {
        // Create a minimal valid JPEG (1x1 greyscale pixel)
        byte[] validJpeg =
        [
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01,
            0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43, 0x00, 0x03, 0x02, 0x02, 0x02, 0x02, 0x02, 0x03,
            0x02, 0x02, 0x02, 0x03, 0x03, 0x03, 0x03, 0x04, 0x06, 0x04, 0x04, 0x04, 0x04, 0x04, 0x08, 0x06,
            0x06, 0x05, 0x06, 0x09, 0x08, 0x0A, 0x0A, 0x09, 0x08, 0x09, 0x09, 0x0A, 0x0C, 0x0F, 0x0C, 0x0A,
            0x0B, 0x0E, 0x0B, 0x09, 0x09, 0x0D, 0x11, 0x0D, 0x0E, 0x0F, 0x10, 0x10, 0x11, 0x10, 0x0A, 0x0C,
            0x12, 0x13, 0x12, 0x10, 0x13, 0x0F, 0x10, 0x10, 0x10, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01,
            0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x14, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0xFF, 0xC4, 0x00, 0x14,
            0x10, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0x0F, 0xFF, 0xD9
        ];

        var filter = new DctDecodeFilter();
        byte[] decoded = filter.Decode(validJpeg);

        // Should decode to 1 byte (1x1 greyscale pixel)
        Assert.NotEmpty(decoded);
        Assert.Single(decoded);
    }

    [Fact]
    public void DCTDecode_ThrowsOnInvalidData()
    {
        var filter = new DctDecodeFilter();
        byte[] invalidData = [0x00, 0x01, 0x02, 0x03];

        Assert.Throws<InvalidOperationException>(() => filter.Decode(invalidData));
    }

    [Fact]
    public void DCTDecode_ThrowsOnNullData()
    {
        var filter = new DctDecodeFilter();

        Assert.Throws<ArgumentNullException>(() => filter.Decode(null!));
    }

    [Fact]
    public void DCTDecode_EncodeThrowsNotSupported()
    {
        var filter = new DctDecodeFilter();
        byte[] data = [0x00, 0x01, 0x02];

        Assert.Throws<NotSupportedException>(() => filter.Encode(data));
    }

    #endregion

    #region JBIG2Decode Tests

    // Note: Comprehensive JBIG2 codec tests are in Compressors.Jbig2.Tests
    // These tests focus on the PdfLibrary filter wrapper interface

    [Fact]
    public void JBIG2Decode_ThrowsOnNullData()
    {
        var filter = new Jbig2DecodeFilter();

        Assert.Throws<ArgumentNullException>(() => filter.Decode(null!));
        Assert.Throws<ArgumentNullException>(() => filter.Encode(null!));
    }

    [Fact]
    public void JBIG2Decode_EncodeThrowsNotSupported()
    {
        var filter = new Jbig2DecodeFilter();
        byte[] data = [0x00, 0x01, 0x02];

        Assert.Throws<NotSupportedException>(() => filter.Encode(data));
    }

    [Fact]
    public void JBIG2Decode_EmptyData_ReturnsEmpty()
    {
        var filter = new Jbig2DecodeFilter();

        byte[] result = filter.Decode([]);

        Assert.Empty(result);
    }

    [Fact]
    public void JBIG2Decode_HasCorrectFilterName()
    {
        var filter = new Jbig2DecodeFilter();
        Assert.Equal("JBIG2Decode", filter.Name);
    }

    #endregion

    #region JPXDecode Tests

    [Fact]
    public void JPXDecode_ThrowsOnInvalidData()
    {
        var filter = new JpxDecodeFilter();
        byte[] invalidData = [0x00, 0x01, 0x02, 0x03];

        // JPEG 2000 decoder should throw when given invalid data
        Assert.Throws<InvalidOperationException>(() => filter.Decode(invalidData));
    }

    [Fact]
    public void JPXDecode_ThrowsOnNullData()
    {
        var filter = new JpxDecodeFilter();

        Assert.Throws<ArgumentNullException>(() => filter.Decode(null!));
    }

    [Fact]
    public void JPXDecode_EncodeThrowsNotSupported()
    {
        var filter = new JpxDecodeFilter();
        byte[] data = [0x00, 0x01, 0x02];

        Assert.Throws<NotSupportedException>(() => filter.Encode(data));
    }

    #endregion

    #region CCITTFaxDecode Tests

    // Note: Comprehensive CCITT codec tests are in Compressors.Ccitt.Tests
    // These tests focus on the PdfLibrary filter wrapper interface

    [Fact]
    public void CCITTFaxDecode_EncodeThrowsNotSupported()
    {
        var filter = new CcittFaxDecodeFilter();
        byte[] data = [0x00, 0x01, 0x02];

        Assert.Throws<NotSupportedException>(() => filter.Encode(data));
    }

    [Fact]
    public void CCITTFaxDecode_ThrowsOnNullData()
    {
        var filter = new CcittFaxDecodeFilter();

        Assert.Throws<ArgumentNullException>(() => filter.Decode(null!));
        Assert.Throws<ArgumentNullException>(() => filter.Encode(null!));
    }

    [Fact]
    public void CCITTFaxDecode_EmptyData_ReturnsEmpty()
    {
        var filter = new CcittFaxDecodeFilter();

        byte[] result = filter.Decode([]);

        Assert.Empty(result);
    }

    [Fact]
    public void CCITTFaxDecode_HasCorrectFilterName()
    {
        var filter = new CcittFaxDecodeFilter();
        Assert.Equal("CCITTFaxDecode", filter.Name);
    }

    #endregion
}
