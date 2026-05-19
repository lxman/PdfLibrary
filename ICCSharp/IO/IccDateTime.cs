namespace ICCSharp.IO;

/// <summary>
/// ICC.1:2010 §4.2 dateTimeNumber — 12 bytes (year, month, day, hour, minute, second; each uInt16).
/// All fields kept raw; the profile may carry zeros to indicate "unspecified", which would round-trip
/// poorly through System.DateTime.
/// </summary>
public readonly record struct IccDateTime(
    ushort Year,
    ushort Month,
    ushort Day,
    ushort Hour,
    ushort Minute,
    ushort Second);
