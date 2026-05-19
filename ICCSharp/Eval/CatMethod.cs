namespace ICCSharp.Eval;

/// <summary>Chromatic adaptation transform method (cone-response basis).</summary>
public enum CatMethod
{
    /// <summary>Bradford (the default in ICC v4 profile creation). Best general-purpose choice.</summary>
    Bradford,
    /// <summary>CIECAM02 cone-response matrix.</summary>
    Cat02,
    /// <summary>von Kries / XYZ scaling — identity cone-response basis; ratios applied directly to XYZ.</summary>
    XyzScaling,
}
