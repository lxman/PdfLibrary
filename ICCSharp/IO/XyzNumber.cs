namespace ICCSharp.IO;

/// <summary>
/// ICC.1:2010 §4.14 XYZNumber — three s15Fixed16 values.
/// </summary>
public readonly record struct XyzNumber(double X, double Y, double Z);
