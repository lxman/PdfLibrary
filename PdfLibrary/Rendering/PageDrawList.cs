using System.Numerics;
using PdfLibrary.Content;

namespace PdfLibrary.Rendering;

public sealed record BeginPageArgs(int PageNumber, double Width, double Height, double Scale,
    double CropOffsetX, double CropOffsetY, int Rotation);

public abstract record DrawCommand;
public sealed record FillCommand(IReadOnlyList<PathSegment> Segments, bool EvenOdd, PdfGraphicsState State) : DrawCommand;
public sealed record StrokeCommand(IReadOnlyList<PathSegment> Segments, PdfGraphicsState State) : DrawCommand;
public sealed record FillStrokeCommand(IReadOnlyList<PathSegment> Segments, bool EvenOdd, PdfGraphicsState State) : DrawCommand;
// A tiling-pattern fill. When <see cref="Content"/> is present the tile content is replayed faithfully,
// clipped to the fill path and repeated on the XStep/YStep lattice (PatternMatrix maps pattern space to
// user space); a null Content means the recorder captured no tile and the renderer falls back to a flat
// fill in the pattern-space colour.
public sealed record TilingFillCommand(IReadOnlyList<PathSegment> Segments, bool EvenOdd, PdfGraphicsState State,
    PageDrawList? Content = null, Matrix3x2 PatternMatrix = default, float XStep = 0, float YStep = 0,
    float BBoxMinX = 0, float BBoxMinY = 0, float BBoxMaxX = 0, float BBoxMaxY = 0) : DrawCommand;
public sealed record ClipCommand(IReadOnlyList<PathSegment> Segments, bool EvenOdd) : DrawCommand;
public sealed record SaveCommand : DrawCommand;
public sealed record RestoreCommand : DrawCommand;
// Per-spot image ink (Soft-Proof SP-6a). Parallel to the flattened Cmyk plane: an image whose colour space
// carries a spot colorant emits, per SPOT colorant, a W×H tint plane (0..255), plus a process-only CMYK
// plane (Cyan/Magenta/Yellow/Black at their per-pixel tint; zero for a pure-spot image). SP-6b paints
// ProcessCmyk to the process buffer and routes TintPlanes to the spot planes when the registry knows Names;
// otherwise it falls back to ImageCommand.Cmyk (today's flatten). Names order == plane order.
public sealed record SpotImageInk(
    IReadOnlyList<string> Names,   // spot colorant names, plane order
    byte[] TintPlanes,             // W*H*Names.Count, plane-interleaved (idx = (y*W+x)*N + s)
    byte[] ProcessCmyk);           // W*H*4

// State carries the constant alpha (ca), blend mode, clip, and soft-mask context the CMYK compositor
// needs to knock an image into the plate buffer. The vector consumers use only Ctm/Rgba; State is
// additive. Images NEVER overprint (ISO 32000 §8.6.6.3; GWG010 c/d/h/i) — the compositor ignores the
// overprint flag and always knocks out.
// Cmyk: optional native DeviceCMYK plane (Width*Height*4 bytes, 0..255) for images whose colour resolves
// to DeviceCMYK. When present the CMYK compositor paints native ink (no lossy RGB→CMYK round-trip) so the
// image matches adjacent DeviceCMYK vector content; alpha still comes from Rgba. Null → RGB fallback.
// ProofCmyk: optional proof-target CMYK plane (Width*Height*4 bytes, row-major, 0..255) for images whose
// colour reached sRGB through an ICC profile (ICCBased N=3/N=4 8-bit, JPX with a 3-channel PDF-level
// profile) AND the proof leg succeeded (see PdfImageToRgba.ToRgba's ProofCmykResolver overload). Alpha
// still comes from Rgba. Never set alongside Cmyk or Spots — native ink and spot routing keep priority.
public sealed record ImageCommand(
    byte[] Rgba, int Width, int Height, AlphaMode Alpha, Matrix3x2 Ctm, PdfGraphicsState State,
    byte[]? Cmyk = null, (bool C, bool M, bool Y, bool K)? OverprintPlates = null,
    SpotImageInk? Spots = null, byte[]? ProofCmyk = null) : DrawCommand
{
    // 2.5.2 binary-compatibility shapes. The primary constructor gained ProofCmyk after 2.5.2; a consumer
    // compiled against 2.5.2 binds to the nine-parameter constructor and the nine-target Deconstruct below.
    // A nine-argument call from source binds here too (no default substitution needed), which is the same
    // behaviour as before: ProofCmyk is null. Keep both until 3.0.0.
    public ImageCommand(byte[] Rgba, int Width, int Height, AlphaMode Alpha, Matrix3x2 Ctm,
        PdfGraphicsState State, byte[]? Cmyk, (bool C, bool M, bool Y, bool K)? OverprintPlates,
        SpotImageInk? Spots)
        : this(Rgba, Width, Height, Alpha, Ctm, State, Cmyk, OverprintPlates, Spots, ProofCmyk: null) { }

    public void Deconstruct(out byte[] Rgba, out int Width, out int Height, out AlphaMode Alpha,
        out Matrix3x2 Ctm, out PdfGraphicsState State, out byte[]? Cmyk,
        out (bool C, bool M, bool Y, bool K)? OverprintPlates, out SpotImageInk? Spots)
    {
        Rgba = this.Rgba; Width = this.Width; Height = this.Height; Alpha = this.Alpha; Ctm = this.Ctm;
        State = this.State; Cmyk = this.Cmyk; OverprintPlates = this.OverprintPlates; Spots = this.Spots;
    }
}
public sealed record SoftMaskPushCommand(string Subtype, PageDrawList Mask) : DrawCommand;
public sealed record SoftMaskPopCommand : DrawCommand;

// A Form XObject transparency group (ISO 32000 §11.4). Content is the group's inner draw-list (captured
// by the recorder into a sub-list); Info carries the group-level blend/alpha + isolated/knockout flags
// active at the invoking Do. A compositing consumer renders Content into an offscreen layer and composites
// the result under Info's blend/alpha and the active soft mask/clip; a non-compositing consumer flattens
// (replays Content inline) for byte-identical legacy behaviour.
public sealed record GroupCommand(TransparencyGroupInfo Info, PageDrawList Content) : DrawCommand;

// The sh operator: paint an axial/radial shading across the current clip. Coords are in the user
// space captured by State.Ctm.
public sealed record ShadingCommand(ShadingDescriptor Shading, PdfGraphicsState State) : DrawCommand;
// A PatternType 2 shading-pattern fill: clip to the path, then paint the gradient positioned by the
// descriptor's PatternMatrix (pattern space → the page's default user space).
public sealed record ShadingPatternFillCommand(
    IReadOnlyList<PathSegment> Segments, bool EvenOdd, ShadingDescriptor Shading, PdfGraphicsState State) : DrawCommand;

public sealed record PageDrawList(BeginPageArgs Begin, IReadOnlyList<DrawCommand> Commands);
