namespace PdfLibrary.Rendering.Icc;

/// <summary>
/// Access to ICC color profiles bundled as embedded resources in PdfLibrary.
/// </summary>
internal static class IccResources
{
    /// <summary>Manifest resource name of the bundled default CMYK profile (CC0 SWOP TR003 Coated).</summary>
    public const string DefaultCmykProfileResourceName = "PdfLibrary.Rendering.Icc.Profiles.SWOP_TR003_coated_3.icc";

    /// <summary>Reads the bundled default CMYK profile bytes. Throws if the resource is missing.</summary>
    public static byte[] ReadDefaultCmykProfile()
    {
        using Stream stream = typeof(IccResources).Assembly.GetManifestResourceStream(DefaultCmykProfileResourceName)
            ?? throw new InvalidOperationException(
                $"Bundled ICC resource '{DefaultCmykProfileResourceName}' was not found in the PdfLibrary assembly.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
