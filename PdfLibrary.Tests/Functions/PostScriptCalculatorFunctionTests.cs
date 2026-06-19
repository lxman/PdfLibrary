using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Functions;

namespace PdfLibrary.Tests.Functions;

/// <summary>
/// Tests for the Type 4 (PostScript calculator) PDF function — the brace-delimited program subset
/// used by shadings and tint transforms.
/// </summary>
public class PostScriptCalculatorFunctionTests
{
    private static PdfArray Reals(params double[] values)
    {
        var items = new PdfObject[values.Length];
        for (var i = 0; i < values.Length; i++) items[i] = new PdfReal(values[i]);
        return new PdfArray(items);
    }

    private static PdfFunction Type4(string program, double[] domain, double[] range)
    {
        var dict = new PdfDictionary();
        dict.Add(new PdfName("FunctionType"), new PdfInteger(4));
        dict.Add(new PdfName("Domain"), Reals(domain));
        dict.Add(new PdfName("Range"), Reals(range));
        var stream = new PdfStream(dict, Encoding.Latin1.GetBytes(program));
        var fn = PdfFunction.Create(stream, null);
        Assert.NotNull(fn);
        return fn!;
    }

    [Fact]
    public void Arithmetic_DoublesInput()
    {
        PdfFunction fn = Type4("{ 2 mul }", [0, 1], [0, 2]);
        Assert.Equal(1.0, fn.Evaluate([0.5])[0], 6);
        Assert.Equal(0.5, fn.Evaluate([0.25])[0], 6);
    }

    [Fact]
    public void ExchAndSub_InvertsRamp()
    {
        // 1 exch sub: input t -> 1 - t.
        PdfFunction fn = Type4("{ 1 exch sub }", [0, 1], [0, 1]);
        Assert.Equal(0.7, fn.Evaluate([0.3])[0], 6);
        Assert.Equal(0.0, fn.Evaluate([1.0])[0], 6);
    }

    [Fact]
    public void MultiOutput_ProducesRgbFromOneInput()
    {
        // t -> (t, 1 - t, 0): a red-to-green colour ramp expressed as a calculator.
        PdfFunction fn = Type4("{ dup 1 exch sub 0 }", [0, 1], [0, 1, 0, 1, 0, 1]);
        double[] o = fn.Evaluate([0.25]);
        Assert.Equal(0.25, o[0], 6);
        Assert.Equal(0.75, o[1], 6);
        Assert.Equal(0.0, o[2], 6);
    }

    [Fact]
    public void Conditional_IfElse_StepFunction()
    {
        PdfFunction fn = Type4("{ 0.5 lt { 0 } { 1 } ifelse }", [0, 1], [0, 1]);
        Assert.Equal(0.0, fn.Evaluate([0.3])[0], 6); // 0.3 < 0.5 -> 0
        Assert.Equal(1.0, fn.Evaluate([0.7])[0], 6); // 0.7 >= 0.5 -> 1
    }

    [Fact]
    public void Roll_RotatesTopElements()
    {
        // (a b c) 3 1 roll -> (c a b).
        PdfFunction fn = Type4("{ 3 1 roll }", [0, 1, 0, 1, 0, 1], [0, 1, 0, 1, 0, 1]);
        double[] o = fn.Evaluate([0.1, 0.2, 0.3]);
        Assert.Equal(0.3, o[0], 6);
        Assert.Equal(0.1, o[1], 6);
        Assert.Equal(0.2, o[2], 6);
    }

    [Fact]
    public void Output_IsClampedToRange()
    {
        // 1.0 * 2 = 2.0, but Range caps the output at 1.0.
        PdfFunction fn = Type4("{ 2 mul }", [0, 1], [0, 1]);
        Assert.Equal(1.0, fn.Evaluate([1.0])[0], 6);
    }

    [Fact]
    public void NestedConditionals_AndArithmetic()
    {
        // A small two-segment ramp built with a conditional: t<0.5 -> 2t, else 2t-1 (sawtooth).
        PdfFunction fn = Type4("{ dup 0.5 lt { 2 mul } { 2 mul 1 sub } ifelse }", [0, 1], [0, 1]);
        Assert.Equal(0.5, fn.Evaluate([0.25])[0], 6); // 2 * 0.25
        Assert.Equal(0.4, fn.Evaluate([0.7])[0], 6);  // 2 * 0.7 - 1
    }
}
