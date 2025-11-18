# Rendering Abstraction Interface Design

**Date**: November 17, 2025
**Status**: Design Proposal
**Related**: Platform_And_Architecture_Analysis.md Section 8.2

## 1. Executive Summary

This document proposes enhancements to the existing `IRenderTarget` interface to support the phased WPF → SkiaSharp → Avalonia migration strategy. Rather than creating a new `IPdfRenderer` interface, we enhance the proven `IRenderTarget` abstraction with page lifecycle management.

**Key Decision**: Enhance `IRenderTarget` rather than create redundant `IPdfRenderer` interface.

**Rationale**:
- `IRenderTarget` already implements 90% of required functionality
- Architecture pattern is proven and working (96.54% rendering accuracy)
- Adding page lifecycle methods is cleaner than creating wrapper abstraction
- Preserves existing working implementations

## 2. Current Architecture Analysis

### 2.1 Existing Rendering Stack

```
┌─────────────────────────────────────┐
│ PdfRenderer (PdfContentProcessor)   │ ← Processes PDF operators
├─────────────────────────────────────┤
│        delegates to                 │
│      IRenderTarget                  │ ← Platform-specific rendering
├─────────────────────────────────────┤
│  uses IPathBuilder                  │ ← Path construction
│  uses PdfGraphicsState              │ ← State information
└─────────────────────────────────────┘
```

### 2.2 IRenderTarget Interface (Current)

**Location**: `PdfLibrary/Rendering/IRenderTarget.cs`

```csharp
public interface IRenderTarget
{
    // Path operations
    void StrokePath(IPathBuilder path, PdfGraphicsState state);
    void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd);
    void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd);
    void SetClippingPath(IPathBuilder path, bool evenOdd);

    // Content operations
    void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state);
    void DrawImage(PdfImage image, PdfGraphicsState state);

    // State management
    void SaveState();
    void RestoreState();
}
```

**Strengths**:
- Clean separation of concerns
- Platform-agnostic operation signatures
- Works with existing PdfRenderer implementation
- Successfully renders complex PDFs (96.54% accuracy)

**Missing**:
- Page lifecycle management (BeginPage, EndPage)
- Page dimension setup
- Multi-page rendering coordination

### 2.3 WPF Implementation Observations

**Location**: `PdfTool/Renderer.xaml.cs`

**Additional Methods** (not in interface):
```csharp
public partial class Renderer : UserControl, IRenderTarget
{
    public int CurrentPageNumber { get; set; }

    public void Clear()
    {
        RenderCanvas.Children.Clear();
        _elements.Clear();
    }

    public void SetPageSize(double width, double height)
    {
        RenderCanvas.Width = width;
        RenderCanvas.Height = height;
        RenderCanvas.MinWidth = width;
        RenderCanvas.MinHeight = height;
        this.UpdateLayout();
    }
}
```

**Key Insight**: Page lifecycle methods exist in implementation but not in interface contract.

## 3. Proposed Enhancement: IRenderTarget v2

### 3.1 Enhanced Interface

**Location**: `PdfLibrary/Rendering/IRenderTarget.cs` (modified)

```csharp
namespace PdfLibrary.Rendering;

/// <summary>
/// Platform-agnostic rendering target for PDF content.
/// Implementations provide platform-specific rendering (WPF, SkiaSharp, Avalonia).
/// </summary>
public interface IRenderTarget
{
    // ==================== PAGE LIFECYCLE (NEW) ====================

    /// <summary>
    /// Begin rendering a new page with specified dimensions.
    /// Called before any rendering operations for a page.
    /// </summary>
    /// <param name="pageNumber">1-based page number</param>
    /// <param name="width">Page width in PDF units (1/72 inch)</param>
    /// <param name="height">Page height in PDF units (1/72 inch)</param>
    void BeginPage(int pageNumber, double width, double height);

    /// <summary>
    /// Complete rendering of current page.
    /// Called after all rendering operations for a page.
    /// Implementations may flush buffers, finalize layout, etc.
    /// </summary>
    void EndPage();

    /// <summary>
    /// Clear all rendered content and reset state.
    /// Used when switching documents or resetting renderer.
    /// </summary>
    void Clear();

    // ==================== PATH OPERATIONS ====================

    /// <summary>
    /// Stroke (outline) a path using current stroke state.
    /// </summary>
    void StrokePath(IPathBuilder path, PdfGraphicsState state);

    /// <summary>
    /// Fill a path using current fill state.
    /// </summary>
    /// <param name="evenOdd">True for even-odd fill rule, false for non-zero winding</param>
    void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd);

    /// <summary>
    /// Fill and stroke a path in a single operation.
    /// </summary>
    void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd);

    /// <summary>
    /// Set clipping path for subsequent operations.
    /// </summary>
    void SetClippingPath(IPathBuilder path, bool evenOdd);

    // ==================== CONTENT OPERATIONS ====================

    /// <summary>
    /// Render text string with specified glyph widths.
    /// </summary>
    /// <param name="text">Decoded text string</param>
    /// <param name="glyphWidths">Width of each glyph in text space</param>
    /// <param name="state">Current graphics state (font, transform, colors)</param>
    void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state);

    /// <summary>
    /// Render an image (XObject).
    /// </summary>
    void DrawImage(PdfImage image, PdfGraphicsState state);

    // ==================== STATE MANAGEMENT ====================

    /// <summary>
    /// Save current graphics state to stack (PDF 'q' operator).
    /// </summary>
    void SaveState();

    /// <summary>
    /// Restore graphics state from stack (PDF 'Q' operator).
    /// </summary>
    void RestoreState();

    // ==================== METADATA (NEW) ====================

    /// <summary>
    /// Current page number being rendered (1-based).
    /// Updated by BeginPage().
    /// </summary>
    int CurrentPageNumber { get; }
}
```

### 3.2 Design Rationale

**Page Lifecycle Methods**:

1. **BeginPage(pageNumber, width, height)**
   - Sets up page dimensions
   - Initializes platform-specific canvas/surface
   - Resets state for new page
   - Stores page number for reference

2. **EndPage()**
   - Finalizes page rendering
   - Flushes any buffered operations
   - Allows platform cleanup (layout updates, etc.)

3. **Clear()**
   - Reset renderer between documents
   - Remove all rendered content
   - Clear state stacks

4. **CurrentPageNumber Property**
   - Query current page being rendered
   - Useful for debugging and logging
   - Updated by BeginPage()

**Why Not Create New IPdfRenderer?**

The Platform & Architecture Analysis document suggested:
```csharp
public interface IPdfRenderer
{
    void DrawText(string text, List<double> glyphWidths, RenderState state);
    void DrawPath(PdfPath path, RenderState state);
    void DrawImage(PdfImage image, Matrix transform, RenderState state);
    void BeginPage(int pageNumber, double width, double height);
    void EndPage();
}
```

**Problems with this approach**:
- 90% duplicate of existing IRenderTarget
- Would require wrapper implementations
- Doesn't leverage existing PdfGraphicsState
- Breaks existing working code
- "RenderState" and "PdfPath" are less clear than current design

**Enhancement approach is better**:
- Preserves existing working implementations
- Minimal breaking changes (add 3 new methods)
- Maintains proven architecture patterns
- Clear upgrade path for existing code

## 4. Implementation Impact Analysis

### 4.1 Files Requiring Changes

#### Critical Path
1. ✅ **PdfLibrary/Rendering/IRenderTarget.cs** - Add 3 new methods + 1 property
2. ✅ **PdfTool/Renderer.xaml.cs** - Formalize existing Clear() and SetPageSize() as BeginPage/EndPage
3. ✅ **ComparisonTool/HeadlessWpfRenderer.cs** - Implement page lifecycle
4. ✅ **PdfLibrary/Rendering/PdfRenderer.cs** - Call BeginPage/EndPage at appropriate points

#### Future Implementations
5. 🔲 **PdfLibrary/Rendering/SkiaSharp/SkiaRenderer.cs** (Phase 2)
6. 🔲 **PdfLibrary/Rendering/Avalonia/AvaloniaRenderer.cs** (Phase 3)

### 4.2 Breaking Changes Assessment

**Breaking Changes**: Minimal
- Existing implementations must add 3 new methods
- Implementations that already have Clear() and SetPageSize() just need to rename/adapt

**Compatibility Strategy**:
- Provide default implementation via extension methods (C# 8.0+ default interface methods)
- Or create abstract base class `RenderTargetBase` with default implementations

**Example Default Implementation**:
```csharp
public abstract class RenderTargetBase : IRenderTarget
{
    public virtual int CurrentPageNumber { get; protected set; }

    public virtual void BeginPage(int pageNumber, double width, double height)
    {
        CurrentPageNumber = pageNumber;
        Clear();
    }

    public virtual void EndPage()
    {
        // Default: no-op, derived classes override if needed
    }

    public virtual void Clear()
    {
        // Default: no-op, derived classes override if needed
    }

    // Existing methods remain abstract
    public abstract void StrokePath(IPathBuilder path, PdfGraphicsState state);
    public abstract void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd);
    // ... etc
}
```

### 4.3 PdfRenderer Integration

**Current PdfRenderer** (PdfContentProcessor pattern):
```csharp
public class PdfRenderer : PdfContentProcessor
{
    private readonly IRenderTarget _target;

    public PdfRenderer(IRenderTarget target, PdfResources? resources = null)
    {
        _target = target;
        // ...
    }

    protected override void OnShowText(PdfString text)
    {
        _target.DrawText(...);
    }

    protected override void OnStroke()
    {
        _target.StrokePath(_currentPath, State);
    }
}
```

**Enhanced PdfRenderer with Page Lifecycle**:
```csharp
public class PdfRenderer : PdfContentProcessor
{
    private readonly IRenderTarget _target;

    // NEW: Public method to render a page
    public void RenderPage(PdfPage page)
    {
        // Get page dimensions
        PdfRectangle mediaBox = page.GetMediaBox();
        double width = mediaBox.Width;
        double height = mediaBox.Height;

        // Begin page lifecycle
        _target.BeginPage(page.PageNumber, width, height);

        try
        {
            // Process page content stream (existing code)
            byte[]? contentStream = page.GetContentStream();
            if (contentStream != null)
            {
                ProcessContentStream(contentStream, page.GetResources());
            }
        }
        finally
        {
            // Always end page, even if exception occurs
            _target.EndPage();
        }
    }

    // Existing operator processing methods unchanged
    protected override void OnShowText(PdfString text) { /* ... */ }
    protected override void OnStroke() { /* ... */ }
}
```

## 5. Migration Plan

### 5.1 Phase 1: Interface Enhancement (Immediate)

**Timeline**: 1-2 days

**Steps**:
1. Add new methods to `IRenderTarget` interface
2. Create `RenderTargetBase` abstract class with default implementations
3. Update `Renderer.xaml.cs` to implement new methods
4. Update `HeadlessWpfRenderer.cs` to implement new methods
5. Add `RenderPage()` method to `PdfRenderer`
6. Run existing tests to verify no regressions

**Testing**:
- All existing unit tests should pass unchanged
- Integration tests should render same output
- Pixel comparison tests should maintain 96.54% accuracy

### 5.2 Phase 2: Usage Migration (Short-term)

**Timeline**: 2-3 days

**Steps**:
1. Update all rendering call sites to use `RenderPage()` instead of direct content processing
2. Add page lifecycle calls to any custom rendering code
3. Update WPF viewer tools to use enhanced interface

**Example Migration**:

**Before**:
```csharp
var renderer = new PdfRenderer(wpfTarget, resources);
renderer.ProcessContentStream(contentBytes);
```

**After**:
```csharp
var renderer = new PdfRenderer(wpfTarget, resources);
renderer.RenderPage(page); // Handles BeginPage/EndPage automatically
```

### 5.3 Phase 3: SkiaSharp Implementation (Future)

**Timeline**: 2-3 weeks (Phase 2 of architecture plan)

**New Files**:
```
PdfLibrary/Rendering/SkiaSharp/
    SkiaRenderTarget.cs      - Implements IRenderTarget
    SkiaPathBuilder.cs        - Implements IPathBuilder
    SkiaTextRenderer.cs       - Glyph-level text rendering
    SkiaImageRenderer.cs      - Image decoding and rendering
```

**Implementation Template**:
```csharp
public class SkiaRenderTarget : RenderTargetBase
{
    private SKCanvas _canvas;
    private SKSurface _surface;

    public override void BeginPage(int pageNumber, double width, double height)
    {
        base.BeginPage(pageNumber, width, height);

        // Create SkiaSharp surface
        var info = new SKImageInfo((int)width, (int)height);
        _surface = SKSurface.Create(info);
        _canvas = _surface.Canvas;
        _canvas.Clear(SKColors.White);
    }

    public override void EndPage()
    {
        // Flush canvas
        _canvas.Flush();
        base.EndPage();
    }

    public override void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state)
    {
        // SkiaSharp glyph-level rendering
        // Use SKFont, SKPaint, DrawGlyphs()
    }

    // ... other methods
}
```

## 6. Testing Strategy

### 6.1 Unit Tests

**New Test File**: `PdfLibrary.Tests/Rendering/RenderTargetLifecycleTests.cs`

```csharp
public class RenderTargetLifecycleTests
{
    [Fact]
    public void BeginPage_SetsCurrentPageNumber()
    {
        var target = new MockRenderTarget();
        target.BeginPage(5, 612, 792);
        Assert.Equal(5, target.CurrentPageNumber);
    }

    [Fact]
    public void BeginPage_ClearsExistingContent()
    {
        var target = new MockRenderTarget();
        target.BeginPage(1, 612, 792);
        target.DrawSomething();
        target.BeginPage(2, 612, 792);
        Assert.Empty(target.RenderedElements);
    }

    [Fact]
    public void RenderPage_CallsBeginAndEnd()
    {
        var target = new MockRenderTarget();
        var renderer = new PdfRenderer(target);
        var page = CreateTestPage(1, 612, 792);

        renderer.RenderPage(page);

        Assert.True(target.BeginPageWasCalled);
        Assert.True(target.EndPageWasCalled);
    }

    [Fact]
    public void RenderPage_CallsEndEvenOnException()
    {
        var target = new ThrowingRenderTarget();
        var renderer = new PdfRenderer(target);

        Assert.Throws<RenderException>(() => renderer.RenderPage(page));
        Assert.True(target.EndPageWasCalled); // Still called despite exception
    }
}
```

### 6.2 Integration Tests

**Update Existing Test**: `PdfLibrary.Tests/Rendering/PdfRenderingIntegrationTests.cs`

```csharp
[Fact]
public void RenderPage_MultiPagePdf_RendersAllPagesWithCorrectNumbers()
{
    using var doc = PdfDocument.Load("TestPDFs/MultiPage.pdf");
    var target = new TestRenderTarget();
    var renderer = new PdfRenderer(target);

    for (int i = 0; i < doc.GetPageCount(); i++)
    {
        var page = doc.GetPage(i);
        renderer.RenderPage(page);

        Assert.Equal(i + 1, target.LastRenderedPageNumber);
    }
}
```

### 6.3 Regression Tests

**Verify Existing Tests Still Pass**:
- `EmbeddedFontIntegrationTests.cs` - 5/5 tests
- `CmapFormatTests.cs` - 10/10 tests
- `GlyfLocaTableTests.cs` - 12/12 tests
- All rendering comparison tests (96.54% accuracy maintained)

## 7. Documentation Updates

### 7.1 Code Documentation

**Update XML Comments** in:
- `IRenderTarget.cs` - Document new methods and usage patterns
- `PdfRenderer.cs` - Document RenderPage() method and lifecycle
- `RenderTargetBase.cs` - Document default implementations

### 7.2 Architecture Documentation

**Update**:
- `Platform_And_Architecture_Analysis.md` - Mark step 8.2 as complete
- Create `Rendering_Implementation_Guide.md` - How to implement IRenderTarget
- Update `README.md` - Mention enhanced rendering abstraction

### 7.3 Migration Guide

**Create**: `Roadmap/Planning/IRenderTarget_Migration_Guide.md`

```markdown
# Migrating to Enhanced IRenderTarget

## For Existing Implementations

If you have an existing IRenderTarget implementation:

1. Inherit from RenderTargetBase instead of implementing IRenderTarget directly
2. Add BeginPage implementation (or use default)
3. Add EndPage implementation (or use default)
4. Add Clear implementation (or use default)

## For Renderer Users

Change from:
```csharp
renderer.ProcessContentStream(content);
```

To:
```csharp
renderer.RenderPage(page);
```
```

## 8. Risk Assessment

### 8.1 Technical Risks

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Breaking existing implementations | High | Medium | Provide RenderTargetBase with defaults |
| Performance regression | Medium | Low | Page lifecycle has minimal overhead |
| State management issues | Medium | Low | Existing state management unchanged |
| Test failures | Low | Low | Comprehensive regression testing |

### 8.2 Migration Risks

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Third-party implementations break | High | Low | Good documentation + base class |
| Missed call sites | Medium | Low | Compiler catches missing methods |
| Incomplete migration | Low | Medium | Gradual migration allowed |

## 9. Success Metrics

### 9.1 Completion Criteria

- ✅ IRenderTarget enhanced with 3 new methods
- ✅ RenderTargetBase created with default implementations
- ✅ Both WPF renderers updated (Renderer.xaml.cs, HeadlessWpfRenderer)
- ✅ PdfRenderer.RenderPage() method implemented
- ✅ All 54 existing tests passing
- ✅ Pixel comparison maintains 96.54%+ accuracy
- ✅ Zero compilation warnings

### 9.2 Quality Gates

**Before Merge**:
1. All unit tests pass
2. All integration tests pass
3. Rendering accuracy maintained or improved
4. Code review approved
5. Documentation updated

## 10. Timeline

| Phase | Duration | Deliverables |
|-------|----------|--------------|
| **Design** (This Document) | 1 day | Design approved |
| **Interface Enhancement** | 1 day | IRenderTarget updated, RenderTargetBase created |
| **Implementation Update** | 1 day | WPF renderers updated |
| **PdfRenderer Integration** | 1 day | RenderPage() method added |
| **Testing** | 1 day | All tests passing |
| **Documentation** | 1 day | Migration guide, API docs |
| **TOTAL** | **6 days** | Enhanced rendering abstraction |

## 11. Alternatives Considered

### 11.1 Option A: Create New IPdfRenderer Interface

**Pros**:
- Matches planning document suggestion
- Clean slate design

**Cons**:
- Duplicates 90% of IRenderTarget
- Requires wrapper implementations
- Breaks existing working code
- More work for same result

**Decision**: ❌ Rejected - Unnecessarily disruptive

### 11.2 Option B: Keep Page Lifecycle in Implementations

**Pros**:
- No interface changes
- Zero breaking changes

**Cons**:
- Not in interface contract
- Can't rely on lifecycle in generic code
- SkiaSharp/Avalonia implementations would be inconsistent
- Doesn't achieve architecture goal

**Decision**: ❌ Rejected - Doesn't meet requirements

### 11.3 Option C: Enhance IRenderTarget (SELECTED)

**Pros**:
- Minimal breaking changes
- Preserves existing architecture
- Formalizes existing patterns
- Clear upgrade path
- Enables future implementations

**Cons**:
- Requires updating existing implementations
- Adds 3 new methods to interface

**Decision**: ✅ **SELECTED** - Best balance of goals and risk

## 12. Appendix

### 12.1 Full Proposed Interface

See Section 3.1 for complete enhanced `IRenderTarget` interface.

### 12.2 Related Documents

- `Platform_And_Architecture_Analysis.md` - Strategic architecture plan
- `EMBEDDED_FONT_TESTING_SUMMARY.md` - Current testing status (54/54 passing)
- `Font_Integration_Plan.md` - Font rendering integration

### 12.3 References

- PDF Reference 1.7 - Graphics operators
- TrueType specification - Glyph rendering
- WPF rendering documentation
- SkiaSharp API documentation
