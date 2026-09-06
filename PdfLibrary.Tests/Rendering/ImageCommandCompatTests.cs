using System.Numerics;
using System.Reflection;
using PdfLibrary.Content;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

// 2.5.2 shipped ImageCommand with nine positional parameters. Master added a tenth (ProofCmyk). A
// consumer compiled against 2.5.2 binds to the nine-parameter constructor and Deconstruct, so both
// must exist as real members, not be satisfied by the optional parameter on the primary constructor.
public class ImageCommandCompatTests
{
    private static readonly Type[] NineParameterShape =
    [
        typeof(byte[]), typeof(int), typeof(int), typeof(AlphaMode), typeof(Matrix3x2),
        typeof(PdfGraphicsState), typeof(byte[]), typeof((bool, bool, bool, bool)?), typeof(SpotImageInk)
    ];

    [Fact]
    public void Nine_parameter_constructor_exists_as_a_real_member()
    {
        ConstructorInfo? ctor = typeof(ImageCommand).GetConstructor(NineParameterShape);
        Assert.NotNull(ctor);
    }

    [Fact]
    public void Nine_parameter_constructor_leaves_ProofCmyk_null()
    {
        var state = new PdfGraphicsState();
        var cmd = new ImageCommand(new byte[4], 1, 1, AlphaMode.Opaque, Matrix3x2.Identity, state, null, null, null);
        Assert.Null(cmd.ProofCmyk);
        Assert.Same(state, cmd.State);
    }

    [Fact]
    public void Nine_target_deconstruct_exists()
    {
        var state = new PdfGraphicsState();
        var cmd = new ImageCommand(new byte[4], 2, 3, AlphaMode.Opaque, Matrix3x2.Identity, state,
            Cmyk: null, OverprintPlates: null, Spots: null, ProofCmyk: new byte[4]);
        var (rgba, width, height, alpha, ctm, st, cmyk, plates, spots) = cmd;
        Assert.Equal(4, rgba.Length);
        Assert.Equal(2, width);
        Assert.Equal(3, height);
        Assert.Equal(AlphaMode.Opaque, alpha);
        Assert.Equal(Matrix3x2.Identity, ctm);
        Assert.Same(state, st);
        Assert.Null(cmyk);
        Assert.Null(plates);
        Assert.Null(spots);
    }

    [Fact]
    public void Ten_parameter_primary_still_carries_ProofCmyk()
    {
        var proof = new byte[4];
        var cmd = new ImageCommand(new byte[4], 1, 1, AlphaMode.Opaque, Matrix3x2.Identity, new PdfGraphicsState(),
            null, null, null, proof);
        Assert.Same(proof, cmd.ProofCmyk);
    }
}
