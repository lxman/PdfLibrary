using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Decodes generic (raw bitmap) regions using arithmetic coding.
/// T.88 Section 6.2 and 7.4.5.
/// </summary>
internal sealed class GenericRegionDecoder
{
    // Template 0: 16-bit context
    // Layout per jbig2dec:
    // Bits 0-3:   4 pixels from current row y, positions (-4,0) to (-1,0)
    // Bit 4:     Adaptive AT1 (default: 3,-1)
    // Bits 5-9:  5 pixels from row y-1, positions (-4,-1) to (0,-1)
    // Bit 10:    Adaptive AT2 (default: -3,-1)
    // Bit 11:    Adaptive AT3 (default: 2,-2)
    // Bits 12-14: 3 pixels from row y-2, positions (-4,-2) to (-2,-2)
    // Bit 15:    Adaptive AT4 (default: -2,-2)

    // Template 1 context pixel positions (10 pixels)
    private static readonly (int dx, int dy)[] Template1 =
    [
        (-3, -2), (-2, -2), (-1, -2),
        (-4, -1), (-3, -1), (-2, -1), (-1, -1), (0, -1), (1, -1), (2, -1),
        (-2, 0), (-1, 0)
        // Plus adaptive pixel
    ];

    // Template 2 context pixel positions (7 pixels)
    private static readonly (int dx, int dy)[] Template2 =
    [
        (-2, -2), (-1, -2),
        (-2, -1), (-1, -1), (0, -1), (1, -1),
        (-1, 0)
        // Plus adaptive pixel
    ];

    // Template 3 context pixel positions (5 pixels)
    private static readonly (int dx, int dy)[] Template3 =
    [
        (-3, -1), (-2, -1), (-1, -1), (0, -1), (1, -1),
        (-1, 0)
        // Plus adaptive pixel
    ];

    private readonly ArithmeticDecoder _decoder;
    private readonly int _template;
    private readonly (int dx, int dy)[] _adaptivePixels;
    private readonly bool _typicalPrediction;
    private readonly ArithmeticDecoder.Context[] _contexts;
    private readonly Jbig2DecoderOptions _options;

    /// <summary>
    /// Creates a generic region decoder.
    /// </summary>
    /// <param name="decoder">Arithmetic decoder to use</param>
    /// <param name="template">Template number (0-3)</param>
    /// <param name="adaptivePixels">Adaptive template pixel positions (4 for template 0, 1 for others)</param>
    /// <param name="typicalPrediction">Whether to use typical prediction</param>
    /// <param name="options">Decoder options for resource limits</param>
    /// <param name="contexts">Pre-allocated arithmetic decoder contexts (optional)</param>
    public GenericRegionDecoder(
        ArithmeticDecoder decoder,
        int template = 0,
        (int dx, int dy)[]? adaptivePixels = null,
        bool typicalPrediction = false,
        Jbig2DecoderOptions? options = null,
        ArithmeticDecoder.Context[]? contexts = null)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _options = options ?? Jbig2DecoderOptions.Default;

        // Validate template
        if (template < 0 || template > 3)
            throw new Jbig2DataException($"Invalid template number: {template}");

        _template = template;
        _adaptivePixels = adaptivePixels ?? GetDefaultAdaptivePixels(template);
        _typicalPrediction = typicalPrediction;

        // Validate adaptive pixels count
        int expectedCount = template == 0 ? 4 : 1;
        if (_adaptivePixels.Length < expectedCount)
            throw new Jbig2DataException($"Template {template} requires {expectedCount} adaptive pixels, got {_adaptivePixels.Length}");

        // Create contexts - number depends on template
        int contextCount = template switch
        {
            0 => 65536,  // 16 bits
            1 => 8192,   // 13 bits
            2 => 1024,   // 10 bits
            3 => 512,    // 9 bits (with TPGD)
            _ => 65536
        };

        if (contexts != null)
        {
            // Use provided contexts (allows sharing across symbol decodes)
            if (contexts.Length < contextCount)
                throw new Jbig2DataException($"Provided context array too small: need {contextCount}, got {contexts.Length}");
            _contexts = contexts;
        }
        else
        {
            // Create new contexts
            _contexts = new ArithmeticDecoder.Context[contextCount];
            for (var i = 0; i < contextCount; i++)
                _contexts[i] = new ArithmeticDecoder.Context();
        }
    }

    /// <summary>
    /// Creates a context array for the given template, suitable for reuse.
    /// </summary>
    public static ArithmeticDecoder.Context[] CreateContexts(int template)
    {
        int contextCount = template switch
        {
            0 => 65536,  // 16 bits
            1 => 8192,   // 13 bits
            2 => 1024,   // 10 bits
            3 => 512,    // 9 bits (with TPGD)
            _ => 65536
        };

        var contexts = new ArithmeticDecoder.Context[contextCount];
        for (var i = 0; i < contextCount; i++)
            contexts[i] = new ArithmeticDecoder.Context();
        return contexts;
    }

    private static (int dx, int dy)[] GetDefaultAdaptivePixels(int template)
    {
        return template switch
        {
            0 => new[] { (3, -1), (-3, -1), (2, -2), (-2, -2) },
            1 => new[] { (3, -1) },
            2 => new[] { (2, -1) },
            3 => new[] { (2, -1) },
            _ => new[] { (3, -1) }
        };
    }

    /// <summary>
    /// Decodes a generic region into a bitmap.
    /// </summary>
    public Bitmap Decode(int width, int height)
    {
        // Validate dimensions against options
        _options.ValidateDimensions(width, height, "Generic region decode");

        var bitmap = new Bitmap(width, height, _options);
        var ltp = false; // Line is "typical" (same as previous)

        for (var y = 0; y < height; y++)
        {
            // Typical prediction for generic direct (TPGD)
            if (_typicalPrediction)
            {
                int tpgdBit = _decoder.DecodeBit(_contexts[_contexts.Length - 1]);
                ltp ^= tpgdBit != 0;

                if (ltp)
                {
                    // Copy previous line
                    if (y > 0)
                    {
                        for (var x = 0; x < width; x++)
                            bitmap.SetPixel(x, y, bitmap.GetPixel(x, y - 1));
                    }
                    continue;
                }
            }

            // Decode each pixel in the line
            for (var x = 0; x < width; x++)
            {
                int context = GetContext(bitmap, x, y);
                int pixel = _decoder.DecodeBit(_contexts[context]);
                bitmap.SetPixel(x, y, pixel);
            }
        }

        return bitmap;
    }

    private int GetContext(Bitmap bitmap, int x, int y)
    {
        var context = 0;

        switch (_template)
        {
            case 0:
                context = GetContextTemplate0(bitmap, x, y);
                break;
            case 1:
                context = GetContextTemplate1(bitmap, x, y);
                break;
            case 2:
                context = GetContextTemplate2(bitmap, x, y);
                break;
            case 3:
                context = GetContextTemplate3(bitmap, x, y);
                break;
        }

        return context;
    }

    private int GetContextTemplate0(Bitmap bitmap, int x, int y)
    {
        // 16-bit context for template 0 - per T.88 Figure 3
        // Bits 0-3:   4 pixels from current row y at (x-1), (x-2), (x-3), (x-4)
        // Bit 4:      Adaptive AT1 (default: x+3, y-1)
        // Bits 5-9:   5 pixels from row y-1 at (x+2), (x+1), (x), (x-1), (x-2)
        // Bit 10:     Adaptive AT2 (default: x-3, y-1)
        // Bit 11:     Adaptive AT3 (default: x+2, y-2)
        // Bits 12-14: 3 pixels from row y-2 at (x+1), (x), (x-1)
        // Bit 15:     Adaptive AT4 (default: x-2, y-2)

        var ctx = 0;

        // Row y (current row): 4 pixels, bits 0-3
        // Bit 0 = most recent (x-1), Bit 3 = oldest (x-4)
        ctx |= bitmap.GetPixel(x - 1, y) << 0;
        ctx |= bitmap.GetPixel(x - 2, y) << 1;
        ctx |= bitmap.GetPixel(x - 3, y) << 2;
        ctx |= bitmap.GetPixel(x - 4, y) << 3;

        // Adaptive AT1 -> bit 4
        ctx |= bitmap.GetPixel(x + _adaptivePixels[0].dx, y + _adaptivePixels[0].dy) << 4;

        // Row y-1: 5 fixed pixels at (x+2,-1) to (x-2,-1) -> bits 5-9
        // This matches jbig2dec's sliding window (pd >> 8) & 0x03E0
        ctx |= bitmap.GetPixel(x + 2, y - 1) << 5;
        ctx |= bitmap.GetPixel(x + 1, y - 1) << 6;
        ctx |= bitmap.GetPixel(x, y - 1) << 7;
        ctx |= bitmap.GetPixel(x - 1, y - 1) << 8;
        ctx |= bitmap.GetPixel(x - 2, y - 1) << 9;

        // Adaptive AT2 -> bit 10
        ctx |= bitmap.GetPixel(x + _adaptivePixels[1].dx, y + _adaptivePixels[1].dy) << 10;

        // Adaptive AT3 -> bit 11
        ctx |= bitmap.GetPixel(x + _adaptivePixels[2].dx, y + _adaptivePixels[2].dy) << 11;

        // Row y-2: 3 fixed pixels -> bits 12-14
        // Per T.88 Template 0 Figure 3:
        // bit 12: (x+1, y-2)
        // bit 13: (x, y-2)
        // bit 14: (x-1, y-2)
        ctx |= bitmap.GetPixel(x + 1, y - 2) << 12;
        ctx |= bitmap.GetPixel(x, y - 2) << 13;
        ctx |= bitmap.GetPixel(x - 1, y - 2) << 14;

        // Adaptive AT4 -> bit 15
        ctx |= bitmap.GetPixel(x + _adaptivePixels[3].dx, y + _adaptivePixels[3].dy) << 15;

        return ctx;
    }

    private int GetContextTemplate1(Bitmap bitmap, int x, int y)
    {
        // 13-bit context for template 1
        var ctx = 0;

        // Row y-2: 3 pixels
        ctx |= bitmap.GetPixel(x - 3, y - 2) << 0;
        ctx |= bitmap.GetPixel(x - 2, y - 2) << 1;
        ctx |= bitmap.GetPixel(x - 1, y - 2) << 2;

        // Row y-1: 7 pixels
        ctx |= bitmap.GetPixel(x - 4, y - 1) << 3;
        ctx |= bitmap.GetPixel(x - 3, y - 1) << 4;
        ctx |= bitmap.GetPixel(x - 2, y - 1) << 5;
        ctx |= bitmap.GetPixel(x - 1, y - 1) << 6;
        ctx |= bitmap.GetPixel(x, y - 1) << 7;
        ctx |= bitmap.GetPixel(x + 1, y - 1) << 8;
        ctx |= bitmap.GetPixel(x + 2, y - 1) << 9;

        // Row y: 2 pixels
        ctx |= bitmap.GetPixel(x - 2, y) << 10;
        ctx |= bitmap.GetPixel(x - 1, y) << 11;

        // Adaptive pixel
        ctx |= bitmap.GetPixel(x + _adaptivePixels[0].dx, y + _adaptivePixels[0].dy) << 12;

        return ctx;
    }

    private int GetContextTemplate2(Bitmap bitmap, int x, int y)
    {
        // 10-bit context for template 2 - per jbig2dec implementation
        // Context layout matches jbig2dec's sliding window approach
        // jbig2dec uses:
        //   CONTEXT = out_byte & 0x003;  // 2 pixels from current row
        //   CONTEXT |= AT << 2;          // adaptive pixel
        //   CONTEXT |= (pd>>11) & 0x078; // 4 pixels from row y-1
        //   CONTEXT |= (ppd>>7) & 0x380; // 3 pixels from row y-2
        var ctx = 0;

        // Bits 0-1: Current row y - out_byte has newest pixel in bit 0
        // bit 0 = (x-1, y), bit 1 = (x-2, y)
        ctx |= bitmap.GetPixel(x - 1, y) << 0;
        ctx |= bitmap.GetPixel(x - 2, y) << 1;

        // Bit 2: Adaptive pixel (default: 2, -1)
        ctx |= bitmap.GetPixel(x + _adaptivePixels[0].dx, y + _adaptivePixels[0].dy) << 2;

        // Bits 3-6: Row y-1 via sliding window (pd>>11) & 0x078
        // bit 3 = (x+1, y-1), bit 4 = (x, y-1), bit 5 = (x-1, y-1), bit 6 = (x-2, y-1)
        ctx |= bitmap.GetPixel(x + 1, y - 1) << 3;
        ctx |= bitmap.GetPixel(x, y - 1) << 4;
        ctx |= bitmap.GetPixel(x - 1, y - 1) << 5;
        ctx |= bitmap.GetPixel(x - 2, y - 1) << 6;

        // Bits 7-9: Row y-2 via sliding window (ppd>>7) & 0x380
        // bit 7 = (x+1, y-2), bit 8 = (x, y-2), bit 9 = (x-1, y-2)
        ctx |= bitmap.GetPixel(x + 1, y - 2) << 7;
        ctx |= bitmap.GetPixel(x, y - 2) << 8;
        ctx |= bitmap.GetPixel(x - 1, y - 2) << 9;

        return ctx;
    }

    private int GetContextTemplate3(Bitmap bitmap, int x, int y)
    {
        // 9-bit context for template 3 (10 bits with TPGD)
        var ctx = 0;

        // Row y-1: 5 pixels
        ctx |= bitmap.GetPixel(x - 3, y - 1) << 0;
        ctx |= bitmap.GetPixel(x - 2, y - 1) << 1;
        ctx |= bitmap.GetPixel(x - 1, y - 1) << 2;
        ctx |= bitmap.GetPixel(x, y - 1) << 3;
        ctx |= bitmap.GetPixel(x + 1, y - 1) << 4;

        // Row y: 1 pixel
        ctx |= bitmap.GetPixel(x - 1, y) << 5;

        // Adaptive pixel
        ctx |= bitmap.GetPixel(x + _adaptivePixels[0].dx, y + _adaptivePixels[0].dy) << 6;

        return ctx;
    }
}
