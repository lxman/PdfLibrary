using SkiaSharp;

namespace PdfLibrary.Rendering.SkiaSharp.State;

/// <summary>
/// Manages soft mask (transparency mask) lifecycle for PDF rendering.
/// Handles setting, applying, and clearing soft masks in coordination with graphics state.
/// </summary>
internal class SoftMaskManager
{
    private readonly SKCanvas _canvas;
    private readonly Func<(float width, float height)> _getLayerBounds;

    private SKBitmap? _activeSoftMask;
    private int _softMaskOwnerDepth = -1;  // The state depth that owns the current soft mask (-1 = no mask)

    public bool HasActiveMask => _activeSoftMask is not null;
    public int OwnerDepth => _softMaskOwnerDepth;

    /// <summary>
    /// Creates a new soft mask manager.
    /// </summary>
    /// <param name="canvas">The SKCanvas to draw on</param>
    /// <param name="getLayerBounds">Function that returns the current layer bounds (width, height) for SaveLayer</param>
    public SoftMaskManager(SKCanvas canvas, Func<(float width, float height)> getLayerBounds)
    {
        _canvas = canvas;
        _getLayerBounds = getLayerBounds;
    }

    /// <summary>
    /// Sets a new soft mask bitmap.
    /// If there's already an active mask, it will be applied and cleared first.
    /// Starts a new layer for masked content.
    /// </summary>
    /// <param name="maskBitmap">The mask bitmap (alpha channel will be used)</param>
    /// <param name="subtype">The mask subtype (Alpha or Luminosity)</param>
    /// <param name="currentDepth">Current state depth</param>
    public void SetMask(SKBitmap maskBitmap, string subtype, int currentDepth)
    {
        // If there's already a mask, apply and clear it first
        if (_activeSoftMask is not null)
        {
            Clear();
        }

        _activeSoftMask = maskBitmap;
        _softMaskOwnerDepth = currentDepth;

        // Start a new layer for masked content
        // All subsequent drawing will go to this layer until the owning state is restored
        (float width, float height) = _getLayerBounds();
        var layerBounds = new SKRect(0, 0, width, height);
        _canvas.SaveLayer(layerBounds, null);
    }

    /// <summary>
    /// Called before restoring a state.
    /// If the state being restored owns the soft mask, applies and clears it.
    /// </summary>
    /// <param name="depthBeingRestored">The depth of the state being restored</param>
    public void OnBeforeRestore(int depthBeingRestored)
    {
        if (_activeSoftMask is not null && depthBeingRestored == _softMaskOwnerDepth)
        {
            ApplyMaskToLayer();
            // Restore the layer that was started by SaveLayer in SetMask
            _canvas.Restore();

            // Clear the mask ownership
            _activeSoftMask.Dispose();
            _activeSoftMask = null;
            _softMaskOwnerDepth = -1;
        }
    }

    /// <summary>
    /// Clears the active soft mask.
    /// If a soft mask layer is active, composites it with the mask before clearing.
    /// </summary>
    public void Clear()
    {
        if (_activeSoftMask is not null && _softMaskOwnerDepth >= 0)
        {
            // Apply the mask to whatever content has been drawn
            ApplyMaskToLayer();
            // Restore the layer that was started by SaveLayer in SetMask
            _canvas.Restore();

            _activeSoftMask.Dispose();
            _activeSoftMask = null;
            _softMaskOwnerDepth = -1;
        }
    }

    /// <summary>
    /// Applies the soft mask to the current layer before restoring.
    /// Uses DstIn blend mode to mask the layer content with the soft mask's alpha channel.
    /// </summary>
    private void ApplyMaskToLayer()
    {
        if (_activeSoftMask is null)
            return;

        // Create a paint with DstIn blend mode
        // DstIn: Result = Dst × Src.Alpha - keeps destination color but multiplies by source alpha
        using var maskPaint = new SKPaint();
        maskPaint.BlendMode = SKBlendMode.DstIn;

        // Draw the mask bitmap - its alpha channel will be applied to the layer
        // The mask is in device coordinates, so reset the matrix temporarily
        SKMatrix currentMatrix = _canvas.TotalMatrix;
        _canvas.SetMatrix(SKMatrix.Identity);

        using SKImage? maskImage = SKImage.FromBitmap(_activeSoftMask);
        if (maskImage is not null)
        {
            _canvas.DrawImage(maskImage, 0, 0, maskPaint);
        }

        _canvas.SetMatrix(currentMatrix);
    }

    /// <summary>
    /// Converts a rendered bitmap to a luminosity-based alpha mask.
    /// The luminosity (grayscale value) of each pixel becomes the alpha value.
    /// </summary>
    public static SKBitmap ConvertToLuminosityMask(SKBitmap source)
    {
        var result = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                SKColor pixel = source.GetPixel(x, y);
                // Calculate luminosity: 0.299*R + 0.587*G + 0.114*B
                var luminosity = (byte)(0.299 * pixel.Red + 0.587 * pixel.Green + 0.114 * pixel.Blue);
                // Use luminosity as alpha, with white (255, 255, 255) for the color
                result.SetPixel(x, y, new SKColor(255, 255, 255, luminosity));
            }
        }

        return result;
    }
}
