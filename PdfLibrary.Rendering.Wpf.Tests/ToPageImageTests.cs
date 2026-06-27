using System.Windows;
using System.Windows.Media;
using PdfLibrary.Rendering.Wpf;
using Xunit;

namespace PdfLibrary.Rendering.Wpf.Tests;

/// <summary>
/// Regression tests for <see cref="WpfPageExtensions.ToPageImage"/> — the fix for a page whose
/// content doesn't span the full page (e.g. fw2.pdf: content in the top ~58%, blank below). Hosting
/// the raw page DrawingGroup under Stretch="Fill" stretched the CONTENT box to the page size,
/// distorting it. ToPageImage forces page-rect bounds so the stretch is a clean 1:1.
/// </summary>
public class ToPageImageTests
{
    [Fact]
    public void ToPageImage_ContentSmallerThanPage_BoundsAreFullPageRect()
    {
        DrawingImage img = Sta.Run(() =>
        {
            // Content fills only the top half of a 612x792 page (mimics a form's blank lower half).
            var content = new DrawingGroup();
            using (DrawingContext dc = content.Open())
                dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 612, 396));
            content.Freeze();

            // Sanity: the raw content bounds are NOT the page (height 396, not 792) — the bug source.
            Assert.Equal(396, content.Bounds.Height, 0);

            DrawingImage di = content.ToPageImage(612, 792);
            di.Freeze();
            return di;
        });

        // The fix: wrapped image bounds are the full page rect → Stretch=Fill is undistorted.
        Assert.Equal(0, img.Drawing.Bounds.X, 0);
        Assert.Equal(0, img.Drawing.Bounds.Y, 0);
        Assert.Equal(612, img.Drawing.Bounds.Width, 0);
        Assert.Equal(792, img.Drawing.Bounds.Height, 0);
    }

    [Fact]
    public void ToPageImage_ContentSpillingPastPage_ClippedToPageRect()
    {
        DrawingImage img = Sta.Run(() =>
        {
            var content = new DrawingGroup();
            using (DrawingContext dc = content.Open())
                dc.DrawRectangle(Brushes.Blue, null, new Rect(-50, -50, 800, 1000)); // spills past page
            content.Freeze();
            DrawingImage di = content.ToPageImage(612, 792);
            di.Freeze();
            return di;
        });

        // Clipped to the page rect, not the oversized content bounds.
        Assert.Equal(612, img.Drawing.Bounds.Width, 0);
        Assert.Equal(792, img.Drawing.Bounds.Height, 0);
    }
}
