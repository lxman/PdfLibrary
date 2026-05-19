namespace ICCSharp.IO;

/// <summary>
/// ICC.1:2010 §4.11 response16Number — device code (uInt16), reserved (uInt16, must be zero),
/// measurement (s15Fixed16Number).
/// </summary>
public readonly record struct Response16Number(ushort DeviceCode, ushort Reserved, double Measurement);
