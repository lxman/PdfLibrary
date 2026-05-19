namespace ICCSharp.Profile;

/// <summary>ICC.1:2010 §7.2.15. Stored as the low 16 bits of the header rendering-intent uInt32.</summary>
public enum RenderingIntent
{
    Perceptual            = 0,
    RelativeColorimetric  = 1,
    Saturation            = 2,
    AbsoluteColorimetric  = 3,
}
