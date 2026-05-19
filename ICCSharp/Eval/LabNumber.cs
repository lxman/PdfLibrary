namespace ICCSharp.Eval;

/// <summary>
/// CIE L*a*b* triple in spec units: L* in [0, 100]; a* and b* nominally in [-128, +127] though
/// the model allows excursions outside that range.
/// </summary>
public readonly record struct LabNumber(double L, double A, double B);
