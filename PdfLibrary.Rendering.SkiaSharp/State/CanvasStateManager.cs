using SkiaSharp;

namespace PdfLibrary.Rendering.SkiaSharp.State;

/// <summary>
/// Manages canvas state save/restore operations and state depth tracking.
/// Coordinates with SoftMaskManager for proper transparency handling.
/// </summary>
internal class CanvasStateManager
{
    private readonly SKCanvas _canvas;
    private readonly Stack<SKMatrix> _stateStack;
    private int _currentDepth;

    public int CurrentDepth => _currentDepth;
    public int StackCount => _stateStack.Count;

    public CanvasStateManager(SKCanvas canvas)
    {
        _canvas = canvas;
        _stateStack = new Stack<SKMatrix>();
        _currentDepth = 0;
    }

    /// <summary>
    /// Save the current canvas state.
    /// Increments depth counter and pushes current transformation matrix onto stack.
    /// </summary>
    public void Save()
    {
        _canvas.Save();
        _stateStack.Push(_canvas.TotalMatrix);
        _currentDepth++;
    }

    /// <summary>
    /// Restore the previously saved canvas state.
    /// Decrements depth counter and pops transformation matrix from stack.
    /// Note: Caller is responsible for soft mask handling before calling this.
    /// </summary>
    public void Restore()
    {
        _canvas.Restore();
        if (_stateStack.Count > 0)
            _stateStack.Pop();
        _currentDepth--;
    }

    /// <summary>
    /// Clear the state stack.
    /// Called when clearing the render target or starting a new page.
    /// </summary>
    public void Clear()
    {
        _stateStack.Clear();
        _currentDepth = 0;
    }
}
