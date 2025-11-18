using System;
// ReSharper disable InconsistentNaming

// ReSharper disable CheckNamespace

#region Enums

public enum GlyfTransform : byte
{
    Transform = 0,
    Null = 3
}

public enum LocaTransform : byte
{
    Transform = 0,
    Null = 3
}

public enum HmtxTransform : byte
{
    Null = 0,
    Transform = 1
}

public enum Flavor : uint
{
    TrueType = 0x00010000,
    Cff = 0x4F54544F,
    Cff2 = 0x4F545443
}

public enum FileType
{
    Unk,
    Ttf,
    Otf,
    Ttc,
    Otc,
    Woff,
    Woff2
}

public enum FontDirectionHint : short
{
    FullyMixed = 0,
    OnlyStrongLtr = 1,
    StrongLtrAndNeutral = 2,
    OnlyStrongRtl = -1,
    StrongRtlAndNeutral = -2
}

public enum IndexToLocFormat
{
    Offset16,
    Offset32
}

public enum InstructionSet
{
    TrueType,
    Cff
}

public enum InstructionType
{
    ByteCode,
    Function
}

public enum UsWeightClass : ushort
{
    Thin = 100,
    ExtraLight = 200,
    Light = 300,
    Normal = 400,
    Medium = 500,
    DemiBold = 600,
    Bold = 700,
    UltraBold = 800,
    Black = 900
}

public enum UsWidthClass : ushort
{
    UltraCondensed = 1,
    ExtraCondensed = 2,
    Condensed = 3,
    SemiCondensed = 4,
    Medium = 5,
    SemiExpanded = 6,
    Expanded = 7,
    ExtraExpanded = 8,
    UltraExpanded = 9
}

public enum GlyphClass : byte
{
    Base = 1,
    Ligature = 2,
    Mark = 3,
    Component = 4
}

public enum GposLookupType : ushort
{
    SingleAdjustment = 1,
    PairAdjustment = 2,
    CursiveAttachment = 3,
    MarkToBaseAttachment = 4,
    MarkToLigatureAttachment = 5,
    MarkToMarkAttachment = 6,
    ContextPositioning = 7,
    ChainedContextPositioning = 8,
    PositioningExtension = 9
}

public enum GsubLookupType : ushort
{
    SingleSubstitution = 1,
    MultipleSubstitution = 2,
    AlternateSubstitution = 3,
    LigatureSubstitution = 4,
    ContextSubstitution = 5,
    ChainedContextSubstitution = 6,
    SubstitutionExtension = 7,
    ReverseChainedContexts = 8
}

public enum RoundState : byte
{
    HalfGrid = 0,
    Grid = 1,
    DoubleGrid = 2,
    DownToGrid = 3,
    UpToGrid = 4,
    Off = 5,
    Super = 6,
    Super45 = 7
}

public enum GlyphClassType : byte
{
    Base = 1,
    Ligature = 2,
    Mark = 3,
    Component = 4
}

public enum DeltaFormat : ushort
{
    Local2BitDeltas = 1,
    Local4BitDeltas = 2,
    Local8BitDeltas = 3,
    VariationIndex = 0x8000,
    Reserved = 0x7FFC
}

public enum OperandKind : byte
{
    StringId = 0,
    Boolean = 1,
    Number = 2,
    Array = 3,
    Delta = 4,
    SidSidNumber = 5,
    NumberNumber = 6
}

public enum CompositeMode : byte
{
    Clear,
    Source,
    Destination,
    SourceOver,
    DestinationOver,
    SourceIn,
    DestinationIn,
    SourceOut,
    DestinationOut,
    SourceAtop,
    DestinationAtop,
    Xor,
    Plus,
    Screen,
    Overlay,
    Darken,
    Lighten,
    ColorDodge,
    ColorBurn,
    HardLight,
    SoftLight,
    Difference,
    Exclusion,
    MulMultiply,
    Hue,
    Saturation,
    Color,
    Luminosity
}

public enum ExtendMode : byte
{
    Pad,
    Repeat,
    Reflect
}

public enum MetaSubtableType : byte
{
    Rearrangement,
    Contextual,
    Ligature,
    Reserved,
    NonContextual,
    Insertion
}

public enum ApplyDirection
{
    Horizontal,
    Vertical,
    BothHorizontalAndVertical
}

public enum ProcessDirection
{
    Ascending,
    Descending
}

public enum ProcessLogicalOrder
{
    Forward,
    Reverse,
    NA
}

public enum BitmapDepth : byte
{
    OneBit = 1,
    TwoBits = 2,
    FourBits = 4,
    EightBits = 8,
    FifteenBits = 15,
    SixteenBits = 16,
    ThirtyTwoBits = 32
}

public enum ImageFormat : ushort
{
    ProportionalFormat1 = 1,
    ProportionalFormat2 = 2,
    Unused = 3,
    MonoCompressedFormat4 = 4,
    MonoFormat5 = 5,
    ProportionalByteFormat6 = 6,
    ProportionalBitFormat7 = 7
}

public enum IndexFormat : ushort
{
    ProportionalW4ByteOffset = 1,
    Monospaced = 2,
    ProportionalW2ByteOffset = 3
}

public enum BitmapSizeFlag : byte
{
    Horizontal = 1,
    Vertical = 2
}

[Flags]
public enum TouchState : ushort
{
    None = 0,
    X = 1,
    Y = 2,
    Both = X | Y
}

public enum DistanceType : int
{
    Grey = 0,
    Black = 1,
    White = 2
}

#region Encoding

public enum PlatformId : ushort
{
    Unicode = 0,
    Macintosh = 1,
    Iso = 2,
    Windows = 3,
    Custom = 4
}

public enum UnicodeEncodingId : ushort
{
    Unicode1 = 0,
    Unicode11 = 1,
    Iso10646 = 2,
    Unicode20 = 3,
    Unicode21 = 4,
    Unicode22 = 5,
    Unicode30 = 6
}

public enum MacintoshEncodingId : ushort
{
    Roman = 0,
    Japanese = 1,
    ChineseTraditional = 2,
    Korean = 3,
    Arabic = 4,
    Hebrew = 5,
    Greek = 6,
    Russian = 7,
    RSymbol = 8,
    Devanagari = 9,
    Gurmukhi = 10,
    Gujarati = 11,
    Oriya = 12,
    Bengali = 13,
    Tamil = 14,
    Telugu = 15,
    Kannada = 16,
    Malayalam = 17,
    Sinhalese = 18,
    Burmese = 19,
    Khmer = 20,
    Thai = 21,
    Laotian = 22,
    Georgian = 23,
    Armenian = 24,
    ChineseSimplified = 25,
    Tibetan = 26,
    Mongolian = 27,
    Geez = 28,
    Slavic = 29,
    Vietnamese = 30,
    Sindhi = 31,
    Uninterpreted = 32
}

public enum IsoEncodingId : ushort
{
    Ascii7Bit = 0,
    Iso10646 = 1,
    Iso8859_1 = 2
}

public enum WindowsEncodingId : ushort
{
    UnicodeCsm = 0,
    UnicodeBmp = 1,
    ShiftJis = 2,
    Prc = 3,
    Big5 = 4,
    Wansung = 5,
    Johab = 6,
    UnicodeUCS4 = 10
}

#endregion

#endregion

#region Flags

[Flags]
public enum HeadFlags : ushort
{
    BaselineAtY0 = 1 << 0,
    LeftSidebearingAtX0 = 1 << 1,
    InstructionsDependOnPointSize = 1 << 2,
    ForcePpemToInteger = 1 << 3,
    InstructionsAlterAdvanceWidth = 1 << 4,
    UseIntegerScaling = 1 << 5,
    InstructionsAlterAdvanceHeight = 1 << 6,
    UseLinearMetrics = 1 << 7,
    UsePpem = 1 << 8,
    UseIntegerPpem = 1 << 9,
    UsePPem = 1 << 10,
    UseIntegerPPem = 1 << 11,
    UseDoubleShift = 1 << 12,
    UseFullHinting = 1 << 13,
    UseGridfit = 1 << 14,
    UseBitmaps = 1 << 15
}

[Flags]
public enum MacStyle : ushort
{
    Bold = 1 << 0,
    Italic = 1 << 1,
    Underline = 1 << 2,
    Outline = 1 << 3,
    Shadow = 1 << 4,
    Condensed = 1 << 5,
    Extended = 1 << 6
}

[Flags]
public enum SimpleGlyphFlags : byte
{
    OnCurve = 1 << 0,
    XShortVector = 1 << 1,
    YShortVector = 1 << 2,
    Repeat = 1 << 3,
    XIsSameOrPositiveXShortVector = 1 << 4,
    YIsSameOrPositiveYShortVector = 1 << 5,
    OverlapSimple = 1 << 6
}

[Flags]
public enum CompositeGlyphFlags : ushort
{
    Arg1And2AreWords = 1 << 0,
    ArgsAreXyValues = 1 << 1,
    RoundXyToGrid = 1 << 2,
    WeHaveAScale = 1 << 3,
    MoreComponents = 1 << 5,
    WeHaveAnXAndYScale = 1 << 6,
    WeHaveATwoByTwo = 1 << 7,
    WeHaveInstructions = 1 << 8,
    UseMyMetrics = 1 << 9,
    OverlapCompound = 1 << 10,
    ScaledComponentOffset = 1 << 11,
    UnscaledComponentOffset = 1 << 12
}

[Flags]
public enum RangeGaspBehavior : ushort
{
    Gridfit = 1 << 0,
    DoGray = 1 << 1,
    SymmetricSmoothing = 1 << 2,
    SymmetricGridfit = 1 << 3
}

[Flags]
public enum FsType : ushort
{
    Unlimited = 0,
    Reserved = 1 << 0,
    Restricted = 1 << 1,
    PreviewAndPrint = 1 << 4,
    Editable = 1 << 5,
    NoSubsetting = 1 << 6,
    BitmapEmbedding = 1 << 7
}

[Flags]
public enum MergeEntryFlags : byte
{
    MergeLtr = 1 << 0,
    GroupLtr = 1 << 1,
    SecondIsSubordinate = 1 << 2,
    Reserved = 0x08,
    MergeRtl = 1 << 4,
    GroupRtl = 1 << 5,
    SecondIsSubordinateRtl = 1 << 6,
    Reserved2 = 0x80
}

[Flags]
public enum ValueFormat : ushort
{
    XPlacement = 1 << 0,
    YPlacement = 1 << 1,
    XAdvance = 1 << 2,
    YAdvance = 1 << 3,
    XPlacementDevice = 1 << 4,
    YPlacementDevice = 1 << 5,
    XAdvanceDevice = 1 << 6,
    YAdvanceDevice = 1 << 7
}

[Flags]
public enum LookupFlag : ushort
{
    RightToLeft = 1 << 0,
    IgnoreBaseGlyphs = 1 << 1,
    IgnoreLigatures = 1 << 2,
    IgnoreMarks = 1 << 3,
    UseMarkFilteringSet = 1 << 4,
    Reserved = 1 << 5,
    MarkAttachmentType = 1 << 6,
    UseMarkFilteringSetAndMarkAttachmentType = 1 << 7
}

[Flags]
public enum PartFlags : ushort
{
    ExtenderFlag = 1 << 0,
    Reserved = 0xFFFE
}

[Flags]
public enum PaletteType
{
    Light = 1,
    Dark = 2,
    Reserved = 0xFFFC
}

[Flags]
public enum PermissionFlags : ushort
{
    CannotResign = 1 << 0,
    Reserved = 0xFFFE
}

[Flags]
public enum ActionFlags : ushort
{
    SetMark = 0x8000,
    DontAdvance = 0x4000,
    CurrentIsKashidaLike = 0x2000,
    MarkedIsKashidaLike = 0x1000,
    CurrentInsertBefore = 0x0800,
    MarkedInsertBefore = 0x0400,
    CurrentInsertCount = 0x03E0,
    MarkedInsertCount = 0x001F
}

[Flags]
public enum EntryFlags : ushort
{
    PerformAction = 0x2000,
    DontAdvance = 0x4000,
    SetComponent = 0x8000,
    Reserved = 0x3FFF
}

[Flags]
public enum AxisValueFlags : ushort
{
    OlderSibling = 1 << 0,
    ElidableAxisValueName = 1 << 1,
    Reserved = 0xFFFC
}

[Flags]
public enum AxisFlags : ushort
{
    Hidden = 1 << 0,
    Advanced = 1 << 1,
    Reserved = 0xFFFC
}

[Flags]
public enum BitmapFlags : sbyte
{
    Horizontal = 1 << 0,
    Vertical = 1 << 1,
    Reserved1 = -4
}

[Flags]
public enum TupleIndexFormat : ushort
{
    EmbeddedPeakTuple = 1 << 15,
    IntermediateRegion = 1 << 14,
    PrivatePointNumbers = 1 << 13,
    Reserved = 1 << 12
}

[Flags]
public enum KerxCoverage : int
{
    Vertical = 1 << 31,
    Horizontal = 1 << 30,
    CrossStream = 1 << 29,
    VariationIndex = 1 << 28,
    Format = 0xFF,
    Reserved = 0xFFFFF00
}

[Flags]
public enum InstructionControlFlags : ushort
{
    None,
    InhibitGridFitting = 0x1,
    UseDefaultGraphicsState = 0x2
}

[Flags]
public enum KerxSubtableActions : ushort
{
    Push = 1 << 15,
    Advance = 1 << 14,
    ClearStack = 1 << 13,
    Reserved = 0x1FFF
}

[Flags]
public enum KernCoverage : ushort
{
    Horizontal = 1 << 0,
    Minimum = 1 << 1,
    CrossStream = 1 << 2,
    Override = 1 << 3,
    Reserved = 0xFFF0
}

#endregion