using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// JBIG2 segment types as defined in T.88 Section 7.3.
/// </summary>
internal enum SegmentType
{
    // Symbol dictionary segments
    SymbolDictionary = 0,

    // Text region segments
    IntermediateTextRegion = 4,
    ImmediateTextRegion = 6,
    ImmediateLosslessTextRegion = 7,

    // Pattern dictionary segments
    PatternDictionary = 16,

    // Halftone region segments
    IntermediateHalftoneRegion = 20,
    ImmediateHalftoneRegion = 22,
    ImmediateLosslessHalftoneRegion = 23,

    // Generic region segments
    IntermediateGenericRegion = 36,
    ImmediateGenericRegion = 38,
    ImmediateLosslessGenericRegion = 39,

    // Generic refinement region segments
    IntermediateGenericRefinementRegion = 40,
    ImmediateGenericRefinementRegion = 42,
    ImmediateLosslessGenericRefinementRegion = 43,

    // Page information segment
    PageInformation = 48,

    // End segments
    EndOfPage = 49,
    EndOfStripe = 50,
    EndOfFile = 51,

    // Profiles
    Profiles = 52,

    // Tables
    Tables = 53,

    // Extension
    Extension = 62
}

/// <summary>
/// Flags for segment header.
/// </summary>
[Flags]
internal enum SegmentFlags : byte
{
    None = 0,
    PageAssociationSizeIs4Bytes = 0x40,
    DeferredNonRetain = 0x20  // Bit 5: if set, segment is "deferred non-retain"
}
