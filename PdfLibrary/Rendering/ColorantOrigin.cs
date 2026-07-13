namespace PdfLibrary.Rendering;

/// <summary>
/// The named-colorant identity of a resolved Separation/DeviceN paint, preserved alongside the
/// flattened device colour on <see cref="PdfLibrary.Content.PdfGraphicsState"/> and
/// <see cref="ShadingDescriptor"/>. Null for device (DeviceGray/RGB/CMYK) and Pattern colours.
/// Soft-Proof SP-1: the data SP-2's N-channel compositor uses to route paint to spot plates.
/// </summary>
public sealed record ColorantOrigin(
    IReadOnlyList<string> Names,   // Separation → 1 name; DeviceN → N names, in colorant order
    IReadOnlyList<double> Tints,   // the raw tint inputs supplied to the colour operator
    string AlternateSpace);        // the tint transform's alternate space name (e.g. "DeviceCMYK", "Lab")
