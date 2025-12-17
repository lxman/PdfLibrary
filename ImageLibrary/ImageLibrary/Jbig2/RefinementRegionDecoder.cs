using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Decodes refinement regions used for symbol refinement.
/// T.88 Section 6.3 and 7.4.7.
/// </summary>
internal sealed class RefinementRegionDecoder
{
    // Template 0: 13-bit context (10 from reference + 3 from current)
    // Template 1: 10-bit context (6 from reference + 4 from current)

    private readonly ArithmeticDecoder _decoder;
    private readonly Bitmap _reference;
    private readonly int _refDx;
    private readonly int _refDy;
    private readonly int _template;
    private readonly (int dx, int dy)[] _adaptivePixels;
    private readonly Jbig2DecoderOptions _options;
    private readonly ArithmeticDecoder.Context[] _contexts;

    /// <summary>
    /// Creates a refinement region decoder.
    /// </summary>
    /// <param name="decoder">Arithmetic decoder</param>
    /// <param name="reference">Reference bitmap</param>
    /// <param name="refDx">X offset of reference relative to decoded region</param>
    /// <param name="refDy">Y offset of reference relative to decoded region</param>
    /// <param name="template">Template number (0 or 1)</param>
    /// <param name="adaptivePixels">Adaptive template pixels</param>
    /// <param name="options">Decoder options</param>
    /// <param name="contexts">Optional shared contexts for symbol dictionary decoding</param>
    public RefinementRegionDecoder(
        ArithmeticDecoder decoder,
        Bitmap reference,
        int refDx,
        int refDy,
        int template,
        (int dx, int dy)[]? adaptivePixels = null,
        Jbig2DecoderOptions? options = null,
        ArithmeticDecoder.Context[]? contexts = null)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _reference = reference ?? throw new ArgumentNullException(nameof(reference));
        _refDx = refDx;
        _refDy = refDy;
        _options = options ?? Jbig2DecoderOptions.Default;

        if (template < 0 || template > 1)
            throw new Jbig2DataException($"Invalid refinement template: {template}");
        _template = template;

        _adaptivePixels = adaptivePixels ?? GetDefaultAdaptivePixels(template);

        // Use provided contexts or create new ones
        if (contexts != null)
        {
            _contexts = contexts;
        }
        else
        {
            // Create contexts
            int contextCount = template == 0 ? 8192 : 1024; // 13 bits or 10 bits
            _contexts = new ArithmeticDecoder.Context[contextCount];
            for (var i = 0; i < contextCount; i++)
                _contexts[i] = new ArithmeticDecoder.Context();
        }
    }

    private static (int dx, int dy)[] GetDefaultAdaptivePixels(int template)
    {
        return template switch
        {
            0 => new[] { (-1, -1), (1, -1) },  // Two adaptive pixels for template 0
            1 => Array.Empty<(int, int)>(),    // No adaptive pixels for template 1
            _ => Array.Empty<(int, int)>()
        };
    }

    /// <summary>
    /// Decodes a refinement region.
    /// </summary>
    public Bitmap Decode(int width, int height)
    {
        _options.ValidateDimensions(width, height, "Refinement region");

        var bitmap = new Bitmap(width, height, _options);

        for (var y = 0; y < height; y++)
        {
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
        return _template == 0
            ? GetContextTemplate0(bitmap, x, y)
            : GetContextTemplate1(bitmap, x, y);
    }

    private int GetContextTemplate0(Bitmap bitmap, int x, int y)
    {
        // T.88 Figure 12 - Template 0 refinement context (13 bits)
        // Based on jbig2dec jbig2_refinement.c implementation

        var ctx = 0;

        // Reference bitmap position
        int rx = x - _refDx;
        int ry = y - _refDy;

        // Current bitmap pixels (3 pixels)
        ctx |= bitmap.GetPixel(x - 1, y) << 0;
        ctx |= bitmap.GetPixel(x + 1, y - 1) << 1;
        ctx |= bitmap.GetPixel(x, y - 1) << 2;

        // Adaptive pixel 1 (from current bitmap) - bit 3
        if (_adaptivePixels.Length >= 1)
            ctx |= bitmap.GetPixel(x + _adaptivePixels[0].dx, y + _adaptivePixels[0].dy) << 3;

        // Reference bitmap pixels - row y+1 (3 pixels)
        ctx |= _reference.GetPixel(rx + 1, ry + 1) << 4;
        ctx |= _reference.GetPixel(rx, ry + 1) << 5;
        ctx |= _reference.GetPixel(rx - 1, ry + 1) << 6;

        // Reference bitmap pixels - row y (3 pixels)
        ctx |= _reference.GetPixel(rx + 1, ry) << 7;
        ctx |= _reference.GetPixel(rx, ry) << 8;
        ctx |= _reference.GetPixel(rx - 1, ry) << 9;

        // Reference bitmap pixels - row y-1 (2 pixels)
        ctx |= _reference.GetPixel(rx + 1, ry - 1) << 10;
        ctx |= _reference.GetPixel(rx, ry - 1) << 11;

        // Adaptive pixel 2 (from reference bitmap) - bit 12
        if (_adaptivePixels.Length >= 2)
            ctx |= _reference.GetPixel(rx + _adaptivePixels[1].dx, ry + _adaptivePixels[1].dy) << 12;

        return ctx;
    }

    private int GetContextTemplate1(Bitmap bitmap, int x, int y)
    {
        // T.88 Figure 13 - Template 1 refinement context (10 bits)
        // Based on jbig2dec jbig2_refinement.c implementation

        var ctx = 0;

        // Reference bitmap position
        int rx = x - _refDx;
        int ry = y - _refDy;

        // Current bitmap pixels (4 pixels)
        ctx |= bitmap.GetPixel(x - 1, y) << 0;
        ctx |= bitmap.GetPixel(x + 1, y - 1) << 1;
        ctx |= bitmap.GetPixel(x, y - 1) << 2;
        ctx |= bitmap.GetPixel(x - 1, y - 1) << 3;

        // Reference bitmap pixels - row y+1 (2 pixels)
        ctx |= _reference.GetPixel(rx + 1, ry + 1) << 4;
        ctx |= _reference.GetPixel(rx, ry + 1) << 5;

        // Reference bitmap pixels - row y (3 pixels)
        ctx |= _reference.GetPixel(rx + 1, ry) << 6;
        ctx |= _reference.GetPixel(rx, ry) << 7;
        ctx |= _reference.GetPixel(rx - 1, ry) << 8;

        // Reference bitmap pixels - row y-1 (1 pixel)
        ctx |= _reference.GetPixel(rx, ry - 1) << 9;

        return ctx;
    }
}
