namespace ICCSharp.IO;

/// <summary>
/// ICC.1:2010 §4.10 positionNumber — offset and size, each uInt32.
/// </summary>
public readonly record struct PositionNumber(uint Offset, uint Size);
