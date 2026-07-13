using System.Numerics;
using PdfLibrary.Content;

namespace PdfLibrary.Tests.Content;

public class PdfGraphicsStateTests
{
    [Fact]
    public void ConcatenateMatrix_CancelingMaxMagnitudeReals_KeepsCtmFiniteAndPointStable()
    {
        // PDF/A-2 §6.1.13 allows real numbers up to ±3.4×10^38. A page may legally translate by
        // +MAX then −MAX (a no-op) — the CTM must stay finite so subsequent drawing survives.
        // Storing/accumulating the CTM in single-precision float overflows here: 3.4029e38 >
        // float.MaxValue (3.40282e38) → +∞, and the canceling second cm → NaN → the whole page
        // renders blank (observed on pdfa2-6-1-13-bfo-t06-pass, a full-page red fill).
        var gs = new PdfGraphicsState();
        gs.ConcatenateMatrix(1, 0, 0, 1, 3.4029e38, 0);    // +HUGE
        gs.ConcatenateMatrix(1, 0, 0, 1, -3.4029e38, 0);   // −HUGE, cancels the first

        // The net transform is identity; a path point must still map to itself. Today it maps to NaN.
        Vector2 p = Vector2.Transform(new Vector2(32f, 32f), gs.Ctm);
        Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"CTM produced a non-finite point ({p.X}, {p.Y})");
        Assert.Equal(32f, p.X, 2);
        Assert.Equal(32f, p.Y, 2);
    }
}
