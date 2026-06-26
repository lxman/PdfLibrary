using System.Runtime.InteropServices;
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
            _canvas.DrawImage(maskImage, 0, 0, SKSamplingOptions.Default, maskPaint);
        }

        _canvas.SetMatrix(currentMatrix);
    }

    /// <summary>
    /// Converts a rendered bitmap to a luminosity-based alpha mask.
    /// The luminosity (grayscale value) of each pixel becomes the alpha value.
    /// </summary>
    public static SKBitmap ConvertToLuminosityMask(SKBitmap source)
    {
        int width = source.Width;
        int height = source.Height;
        var result = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        int pixelCount = width * height;
        var srcPixels = new byte[pixelCount * 4];
        var dstPixels = new byte[pixelCount * 4];

        IntPtr srcPtr = source.GetPixels();
        if (srcPtr != IntPtr.Zero)
            Marshal.Copy(srcPtr, srcPixels, 0, srcPixels.Length);

        for (var i = 0; i < pixelCount; i++)
        {
            int off = i * 4;
            byte r = srcPixels[off];
            byte g = srcPixels[off + 1];
            byte b = srcPixels[off + 2];
            var luminosity = (byte)((r * 77 + g * 150 + b * 29) >> 8);
            dstPixels[off] = 255;
            dstPixels[off + 1] = 255;
            dstPixels[off + 2] = 255;
            dstPixels[off + 3] = luminosity;
        }

        IntPtr dstPtr = result.GetPixels();
        if (dstPtr != IntPtr.Zero)
        {
            Marshal.Copy(dstPixels, 0, dstPtr, dstPixels.Length);
            result.NotifyPixelsChanged();
        }

        return result;
    }
}
